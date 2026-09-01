using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AlexDirectorConsole.V2.Api.Features.Projects.Assets;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.Projects.Voice;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.ComfyUi;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Storyboard;

public sealed record ShotVideoPreview(
    string Prompt,
    string PreviewHash,
    int Width,
    int Height,
    int FrameCount,
    int Fps,
    double DurationSeconds,
    Guid FirstFrameAssetId,
    Guid? LastFrameAssetId,
    string WorkflowProfile);

public sealed record ShotVideoProductionView(
    Guid RunId,
    string Status,
    string CurrentStage,
    Guid? AssetId,
    string? Url,
    int? Version,
    string Prompt,
    DateTimeOffset CreatedAtUtc,
    string? Error);

public sealed record StartShotVideoRequest(string? ConfirmedPrompt, string? PreviewHash, string? Instruction);

internal sealed record ShotVideoRunSpec(
    string Mode,
    string Prompt,
    string PreviewHash,
    int Width,
    int Height,
    int FrameCount,
    int Fps,
    double DurationSeconds,
    Guid FirstFrameAssetId,
    Guid? LastFrameAssetId,
    string WorkflowProfile,
    string WorkflowHash);

public sealed record ComfyUiVideoSubmission(
    string BaseUrl,
    string WorkflowJson,
    byte[] FirstFrame,
    byte[]? LastFrame,
    string Prompt,
    int Width,
    int Height,
    int FrameCount,
    int Fps);

public sealed record ComfyUiVideoOutput(
    string FileName,
    string Subfolder,
    string Type);

public sealed record ComfyUiJobResult(
    bool IsCompleted,
    bool IsFailed,
    string? Error,
    ComfyUiVideoOutput? Output);

public interface IComfyUiVideoClient
{
    Task<string> SubmitAsync(ComfyUiVideoSubmission submission, CancellationToken cancellationToken);
    Task<ComfyUiJobResult> GetResultAsync(string baseUrl, string promptId, CancellationToken cancellationToken);
    Task<byte[]> DownloadAsync(string baseUrl, ComfyUiVideoOutput output, CancellationToken cancellationToken);
}

public interface IComfyUiWorkflowProvider
{
    Task<string> ReadAsync(string workflowProfile, CancellationToken cancellationToken);
}

public sealed class PackagedComfyUiWorkflowProvider : IComfyUiWorkflowProvider
{
    public async Task<string> ReadAsync(string workflowProfile, CancellationToken cancellationToken)
    {
        var normalized = ComfyUiConfigurationView.NormalizeVideoWorkflow(workflowProfile);
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Skills",
            "video-generation",
            "workflows",
            normalized == ComfyUiConfigurationView.Ltx23VideoWorkflow
                ? "ltx-2.3-av-i2v-api.json"
                : "minimax-h3-fl2va-api.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"内置 {ComfyUiConfigurationView.VideoModel(normalized)} workflow 不存在。", path);
        }
        return await File.ReadAllTextAsync(path, cancellationToken);
    }
}

public sealed class ComfyUiVideoClient(IHttpClientFactory httpClientFactory) : IComfyUiVideoClient
{
    public async Task<string> SubmitAsync(
        ComfyUiVideoSubmission submission,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("ComfyUiVideo");
        var root = new Uri(submission.BaseUrl.TrimEnd('/') + "/");
        var prefix = $"alex-v2-{Guid.NewGuid():N}";
        var firstFrame = await UploadImageAsync(
            client,
            root,
            submission.FirstFrame,
            $"{prefix}-first.png",
            cancellationToken);
        string? lastFrame = null;
        if (submission.LastFrame is not null)
        {
            lastFrame = await UploadImageAsync(
                client,
                root,
                submission.LastFrame,
                $"{prefix}-last.png",
                cancellationToken);
        }

        var workflow = JsonNode.Parse(submission.WorkflowJson)?.AsObject()
            ?? throw new InvalidOperationException("ComfyUI workflow JSON 为空。");
        var hasLastFrameInput = submission.WorkflowJson.Contains("{{LAST_FRAME}}", StringComparison.Ordinal);
        ReplaceTokens(workflow, new Dictionary<string, JsonNode?>
        {
            ["{{FIRST_FRAME}}"] = firstFrame,
            ["{{LAST_FRAME}}"] = lastFrame,
            ["{{PROMPT}}"] = submission.Prompt,
            ["{{WIDTH}}"] = submission.Width,
            ["{{HEIGHT}}"] = submission.Height,
            ["{{FRAME_COUNT}}"] = submission.FrameCount,
            ["{{FPS}}"] = submission.Fps,
            ["{{OUTPUT_PREFIX}}"] = prefix
        });
        if (lastFrame is null && hasLastFrameInput)
        {
            workflow.Remove("2");
            if (workflow["7"]?["inputs"] is JsonObject inputs)
            {
                inputs.Remove("last_frame");
            }
        }
        foreach (var node in workflow.Select(item => item.Value).OfType<JsonObject>())
        {
            if (node["inputs"] is not JsonObject inputs) continue;
            if (inputs.ContainsKey("noise_seed"))
                inputs["noise_seed"] = Random.Shared.NextInt64(0, long.MaxValue);
            if (inputs.ContainsKey("seed"))
                inputs["seed"] = Random.Shared.NextInt64(0, long.MaxValue);
        }
        if (workflow.ToJsonString().Contains("{{", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ComfyUI workflow 仍含未解析占位符。");
        }

        using var response = await client.PostAsJsonAsync(
            new Uri(root, "prompt"),
            new { prompt = workflow },
            cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ComfyUI 拒绝 workflow：{body}");
        }
        return body?["prompt_id"]?.GetValue<string>()
            ?? throw new InvalidOperationException("ComfyUI 未返回 prompt_id。");
    }

    public async Task<ComfyUiJobResult> GetResultAsync(
        string baseUrl,
        string promptId,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("ComfyUiVideo");
        var root = new Uri(baseUrl.TrimEnd('/') + "/");
        var history = await client.GetFromJsonAsync<JsonObject>(
            new Uri(root, $"history/{Uri.EscapeDataString(promptId)}"),
            cancellationToken);
        var record = history?[promptId] as JsonObject;
        if (record is null) return new(false, false, null, null);
        var output = FindVideoOutput(record["outputs"]);
        if (output is not null) return new(true, false, null, output);
        var status = record["status"]?["status_str"]?.GetValue<string>();
        return string.Equals(status, "error", StringComparison.OrdinalIgnoreCase)
            ? new(false, true, record["status"]?.ToJsonString(), null)
            : new(false, false, null, null);
    }

    public async Task<byte[]> DownloadAsync(
        string baseUrl,
        ComfyUiVideoOutput output,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("ComfyUiVideo");
        var root = new Uri(baseUrl.TrimEnd('/') + "/");
        var path = $"view?filename={Uri.EscapeDataString(output.FileName)}"
            + $"&subfolder={Uri.EscapeDataString(output.Subfolder)}"
            + $"&type={Uri.EscapeDataString(output.Type)}";
        var bytes = await client.GetByteArrayAsync(new Uri(root, path), cancellationToken);
        if (bytes.Length < 1024 || bytes.Length < 12 || !bytes.AsSpan(4, 4).SequenceEqual("ftyp"u8))
        {
            throw new InvalidOperationException("ComfyUI 下载结果不是有效且大小合理的 MP4 文件。");
        }
        return bytes;
    }

    private static async Task<string> UploadImageAsync(
        HttpClient client,
        Uri root,
        byte[] bytes,
        string fileName,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("image/png");
        form.Add(content, "image", fileName);
        form.Add(new StringContent("true"), "overwrite");
        using var response = await client.PostAsync(new Uri(root, "upload/image"), form, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"上传关键帧失败：{body}");
        }
        return body?["name"]?.GetValue<string>() ?? fileName;
    }

    private static ComfyUiVideoOutput? FindVideoOutput(JsonNode? outputs)
    {
        if (outputs is not JsonObject nodes) return null;
        foreach (var node in nodes)
        {
            if (node.Value is not JsonObject output) continue;
            foreach (var key in new[] { "videos", "gifs", "images" })
            {
                if (output[key] is not JsonArray files) continue;
                foreach (var file in files.OfType<JsonObject>())
                {
                    var fileName = file["filename"]?.GetValue<string>();
                    if (fileName?.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) != true) continue;
                    return new(
                        fileName,
                        file["subfolder"]?.GetValue<string>() ?? string.Empty,
                        file["type"]?.GetValue<string>() ?? "output");
                }
            }
        }
        return null;
    }

    private static void ReplaceTokens(JsonNode node, IReadOnlyDictionary<string, JsonNode?> replacements)
    {
        if (node is JsonObject valueObject)
        {
            foreach (var property in valueObject.ToArray())
            {
                if (property.Value is JsonValue value
                    && value.TryGetValue<string>(out var text)
                    && replacements.TryGetValue(text, out var replacement))
                {
                    valueObject[property.Key] = replacement?.DeepClone();
                }
                else if (property.Value is not null)
                {
                    ReplaceTokens(property.Value, replacements);
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is JsonValue value
                    && value.TryGetValue<string>(out var text)
                    && replacements.TryGetValue(text, out var replacement))
                {
                    array[index] = replacement?.DeepClone();
                }
                else if (array[index] is not null)
                {
                    ReplaceTokens(array[index]!, replacements);
                }
            }
        }
    }
}

public interface IShotVideoService
{
    Task<ShotVideoPreview?> PreviewAsync(Guid projectId, Guid episodeId, Guid shotResourceId, string? instruction, CancellationToken cancellationToken);
    Task<ShotVideoPreview?> PreviewConfirmedAsync(Guid projectId, Guid episodeId, Guid shotResourceId, string prompt, CancellationToken cancellationToken);
    Task<ShotVideoProductionView?> StartAsync(Guid projectId, Guid episodeId, Guid shotResourceId, string prompt, string previewHash, string? instruction, CancellationToken cancellationToken);
    Task<ShotVideoProductionView> WaitForCompletionAsync(
        Guid projectId,
        Guid episodeId,
        Guid shotResourceId,
        Guid runId,
        Func<ShotVideoProductionView, Task>? reportProgress,
        CancellationToken cancellationToken);
    Task<bool> ProcessNextAsync(CancellationToken cancellationToken);
}

public sealed class ShotVideoService(
    V2DbContext dbContext,
    IComfyUiVideoClient client,
    IComfyUiWorkflowProvider workflowProvider,
    IComfyUiConnectionTester connectionTester,
    IShotVideoPromptAgent promptAgent,
    IVoiceProfileService voiceProfileService,
    TimeProvider timeProvider,
    ILogger<ShotVideoService> logger) : IShotVideoService
{
    public const string AssetType = "storyboard-shot-video";
    public const string RunType = "shot-video";
    private const int FramesPerSecond = 24;
    private const int DimensionMultiple = 32;
    private const string MiniMaxH3ExecutionMarker = "MINIMAX H3 EXECUTION REQUIREMENTS:";
    private const string MiniMaxH3ContinuousShotRequirement = "This is one uninterrupted continuous shot in one physically continuous space and one continuous timeline. Preserve a single camera trajectory from the supplied first frame to the final frame. No cuts, shot changes, angle changes, reframing jumps, transitions, montage, inserts, split screen, flashbacks, time jumps, location changes, or replacement backgrounds. Do not switch to another character's viewpoint or introduce a second composition.";

    public async Task<ShotVideoPreview?> PreviewAsync(
        Guid projectId,
        Guid episodeId,
        Guid shotResourceId,
        string? instruction,
        CancellationToken cancellationToken)
    {
        var context = await ResolveContextAsync(projectId, episodeId, shotResourceId, cancellationToken);
        if (context is null) return null;
        var configuration = await dbContext.ComfyUiConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        var workflowProfile = ComfyUiConfigurationView.NormalizeVideoWorkflow(configuration?.WorkflowProfile);
        var workflow = await workflowProvider.ReadAsync(workflowProfile, cancellationToken);
        var draft = await promptAgent.GenerateAsync(BuildAgentInput(context, instruction), cancellationToken);
        var prompt = BuildPrompt(context.Shot, draft, context.Settings.VideoPromptModel);
        return BuildPreview(context, prompt, workflow, workflowProfile);
    }

    public async Task<ShotVideoPreview?> PreviewConfirmedAsync(
        Guid projectId,
        Guid episodeId,
        Guid shotResourceId,
        string prompt,
        CancellationToken cancellationToken)
    {
        var context = await ResolveContextAsync(projectId, episodeId, shotResourceId, cancellationToken);
        if (context is null) return null;
        var configuration = await dbContext.ComfyUiConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        var workflowProfile = ComfyUiConfigurationView.NormalizeVideoWorkflow(configuration?.WorkflowProfile);
        var workflow = await workflowProvider.ReadAsync(workflowProfile, cancellationToken);
        return BuildPreview(context, prompt, workflow, workflowProfile);
    }

    private static ShotVideoPreview BuildPreview(
        ShotVideoContext context,
        string prompt,
        string workflow,
        string workflowProfile)
    {
        var durationSeconds = CalculateEffectiveDuration(context.Shot, workflowProfile);
        prompt = ApplyWorkflowPrompt(context.Shot, prompt, workflowProfile, durationSeconds);
        var frameCount = CalculateFrameCount(durationSeconds, FramesPerSecond, workflowProfile);
        var width = NormalizeDimension(context.Settings.OutputWidth);
        var height = NormalizeDimension(context.Settings.OutputHeight);
        var workflowHash = Hash(workflow);
        var previewHash = Hash(JsonSerializer.Serialize(new
        {
            context.Definition.ShotAssetId,
            settingsAssetId = context.SettingsAsset.Id,
            firstFrameAssetId = context.FirstFrame.Id,
            lastFrameAssetId = context.LastFrame?.Id,
            characterAssetIds = context.Characters.Select(item => item.AssetId),
            voiceProfileAssetIds = context.Characters.Select(item => item.VoiceProfileAssetId),
            prompt,
            width,
            height,
            frameCount,
            fps = FramesPerSecond,
            workflowHash
        }, StoryboardDefaults.JsonOptions));
        return new(
            prompt,
            previewHash,
            width,
            height,
            frameCount,
            FramesPerSecond,
            durationSeconds,
            context.FirstFrame.Id,
            context.LastFrame?.Id,
            workflowProfile);
    }

    public async Task<ShotVideoProductionView?> StartAsync(
        Guid projectId,
        Guid episodeId,
        Guid shotResourceId,
        string prompt,
        string previewHash,
        string? instruction,
        CancellationToken cancellationToken)
    {
        var context = await ResolveContextAsync(projectId, episodeId, shotResourceId, cancellationToken);
        if (context is null) return null;
        var configuration = await dbContext.ComfyUiConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        var workflowProfile = ComfyUiConfigurationView.NormalizeVideoWorkflow(configuration?.WorkflowProfile);
        var workflow = await workflowProvider.ReadAsync(workflowProfile, cancellationToken);
        var preview = BuildPreview(context, prompt, workflow, workflowProfile);
        if (!string.Equals(previewHash, preview.PreviewHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("镜头、项目设定、关键帧或 workflow 已变化，请重新预览。");
        }
        if (configuration is null || !configuration.IsEnabled)
        {
            throw new InvalidOperationException("请先在系统设置中启用本地 ComfyUI。");
        }
        var currentShotAssetId = await dbContext.ShotDefinitions.AsNoTracking()
            .Where(item => item.ProjectId == projectId
                && item.ProductionEpisodeId == episodeId
                && item.ShotResourceId == shotResourceId)
            .Select(item => item.ShotAssetId)
            .SingleAsync(cancellationToken);
        var activeRuns = await dbContext.ProductionRuns.AsNoTracking()
            .Where(item => item.ProjectId == projectId
                && item.ProductionEpisodeId == episodeId
                && item.RunType == RunType
                && (item.Status == "queued" || item.Status == "running"))
            .Join(
                dbContext.ProductionRunItems.AsNoTracking().Where(item =>
                    item.ShotResourceId == shotResourceId && item.ShotAssetId == currentShotAssetId),
                run => run.Id,
                item => item.RunId,
                (run, item) => run)
            .ToListAsync(cancellationToken);
        var active = activeRuns.OrderByDescending(item => item.CreatedAtUtc).FirstOrDefault();
        if (active is not null)
        {
            return await ShotVideoQueries.GetAsync(dbContext, projectId, episodeId, shotResourceId, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var spec = new ShotVideoRunSpec(
            RunType,
            preview.Prompt,
            preview.PreviewHash,
            preview.Width,
            preview.Height,
            preview.FrameCount,
            preview.Fps,
            preview.DurationSeconds,
            preview.FirstFrameAssetId,
            preview.LastFrameAssetId,
            preview.WorkflowProfile,
            Hash(workflow));
        var run = new ProductionRun
        {
            ProjectId = projectId,
            ProductionEpisodeId = episodeId,
            ScriptPackageAssetId = context.Definition.ScriptPackageAssetId,
            CreativeSettingsAssetId = context.SettingsAsset.Id,
            RunType = RunType,
            Status = "queued",
            CurrentStage = "queued",
            SpecJson = JsonSerializer.Serialize(spec, StoryboardDefaults.JsonOptions),
            OriginalInstruction = "使用本地 ComfyUI 和 MiniMax H3 Turbo 4-step 生成单镜视频。",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.ProductionRuns.Add(run);
        dbContext.ProductionRunItems.Add(new ProductionRunItem
        {
            RunId = run.Id,
            ProjectId = projectId,
            ProductionEpisodeId = episodeId,
            ShotResourceId = shotResourceId,
            ShotAssetId = context.Definition.ShotAssetId,
            ShotName = context.ShotAsset.Name,
            Stage = RunType,
            Status = "queued",
            Attempt = 0,
            InputAssetIdsJson = JsonSerializer.Serialize(
                new Guid?[]
                {
                    context.Definition.ShotAssetId,
                    context.SettingsAsset.Id,
                    context.FirstFrame.Id,
                    context.LastFrame?.Id
                }.Where(item => item.HasValue).Select(item => item!.Value),
                StoryboardDefaults.JsonOptions),
            InputFingerprint = preview.PreviewHash,
            CreatedAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(run.Id, run.Status, run.CurrentStage, null, null, null, preview.Prompt, now, null);
    }

    public async Task<ShotVideoProductionView> WaitForCompletionAsync(
        Guid projectId,
        Guid episodeId,
        Guid shotResourceId,
        Guid runId,
        Func<ShotVideoProductionView, Task>? reportProgress,
        CancellationToken cancellationToken)
    {
        var deadline = timeProvider.GetUtcNow().AddHours(1);
        string? reportedStage = null;
        while (timeProvider.GetUtcNow() < deadline)
        {
            var current = await ShotVideoQueries.GetAsync(
                dbContext,
                projectId,
                episodeId,
                shotResourceId,
                cancellationToken)
                ?? throw new InvalidOperationException("视频生产任务不存在。");
            if (current.RunId != runId)
            {
                throw new InvalidOperationException("视频生产任务已被其他运行替换。");
            }
            if (reportProgress is not null
                && !string.Equals(reportedStage, current.CurrentStage, StringComparison.Ordinal))
            {
                reportedStage = current.CurrentStage;
                await reportProgress(current);
            }
            if (current.Status == "completed") return current;
            if (current.Status is "failed" or "cancelled")
            {
                throw new InvalidOperationException(current.Error ?? "视频生成失败。");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        throw new TimeoutException("等待 ComfyUI 视频生成完成超时。");
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var availableRuns = await dbContext.ProductionRuns
            .Where(item => item.RunType == RunType
                && (item.Status == "queued" || item.Status == "running"))
            .ToListAsync(cancellationToken);
        var run = availableRuns
            .Where(item => item.LeaseExpiresAtUtc is null || item.LeaseExpiresAtUtc < now)
            .OrderBy(item => item.CreatedAtUtc)
            .FirstOrDefault();
        if (run is null) return false;
        var item = await dbContext.ProductionRunItems.SingleAsync(
            candidate => candidate.RunId == run.Id && candidate.Stage == RunType,
            cancellationToken);
        run.LeaseOwner = Environment.MachineName;
        run.LeaseExpiresAtUtc = now.AddMinutes(2);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var spec = JsonSerializer.Deserialize<ShotVideoRunSpec>(run.SpecJson, StoryboardDefaults.JsonOptions)
                ?? throw new InvalidOperationException("视频生产规格无效。");
            var configuration = await dbContext.ComfyUiConfigurations.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == 1, cancellationToken);
            if (configuration is null || !configuration.IsEnabled)
            {
                throw new InvalidOperationException("本地 ComfyUI 未启用。");
            }

            if (string.IsNullOrWhiteSpace(item.ExternalJobId))
            {
                await SubmitAsync(run, item, spec, configuration, cancellationToken);
            }
            else
            {
                await PollAsync(run, item, spec, configuration, cancellationToken);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError(error, "Shot video run {RunId} failed.", run.Id);
            now = timeProvider.GetUtcNow();
            run.Status = "failed";
            run.CurrentStage = "failed";
            run.LastError = error.Message;
            run.CompletedAtUtc = now;
            item.Status = "failed";
            item.ErrorCode = error.GetType().Name;
            item.ErrorDetail = error.Message;
            item.CompletedAtUtc = now;
        }
        finally
        {
            run.LeaseOwner = null;
            run.LeaseExpiresAtUtc = null;
            run.UpdatedAtUtc = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        return true;
    }

    private async Task SubmitAsync(
        ProductionRun run,
        ProductionRunItem item,
        ShotVideoRunSpec spec,
        ComfyUiConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var firstFrame = await dbContext.Assets.AsNoTracking().SingleAsync(
            asset => asset.Id == spec.FirstFrameAssetId && asset.ProjectId == run.ProjectId,
            cancellationToken);
        Asset? lastFrame = null;
        if (spec.LastFrameAssetId is Guid lastFrameId)
        {
            lastFrame = await dbContext.Assets.AsNoTracking().SingleAsync(
                asset => asset.Id == lastFrameId && asset.ProjectId == run.ProjectId,
                cancellationToken);
        }
        var now = timeProvider.GetUtcNow();
        run.Status = "running";
        run.CurrentStage = "validating-capabilities";
        run.StartedAtUtc ??= now;
        item.Status = "running";
        item.Attempt += 1;
        item.StartedAtUtc ??= now;
        await dbContext.SaveChangesAsync(cancellationToken);
        var capabilities = await connectionTester.TestAsync(
            configuration.BaseUrl,
            configuration.ImageEditWorkflow,
            configuration.WorkflowProfile,
            cancellationToken);
        if (!capabilities.IsSuccess)
        {
            throw new InvalidOperationException(capabilities.Message);
        }
        run.CurrentStage = "uploading-inputs";
        await dbContext.SaveChangesAsync(cancellationToken);
        var promptId = await client.SubmitAsync(
            new(
                configuration.BaseUrl,
                await workflowProvider.ReadAsync(spec.WorkflowProfile, cancellationToken),
                firstFrame.BlobContent ?? throw new InvalidOperationException("首帧文件为空。"),
                lastFrame?.BlobContent,
                spec.Prompt,
                spec.Width,
                spec.Height,
                spec.FrameCount,
                spec.Fps),
            cancellationToken);
        item.ExternalJobId = promptId;
        run.CurrentStage = "polling";
        run.UpdatedAtUtc = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PollAsync(
        ProductionRun run,
        ProductionRunItem item,
        ShotVideoRunSpec spec,
        ComfyUiConfiguration configuration,
        CancellationToken cancellationToken)
    {
        run.CurrentStage = "polling";
        var result = await client.GetResultAsync(
            configuration.BaseUrl,
            item.ExternalJobId!,
            cancellationToken);
        if (result.IsFailed)
        {
            throw new InvalidOperationException($"ComfyUI 视频任务失败：{result.Error}");
        }
        if (!result.IsCompleted || result.Output is null) return;
        run.CurrentStage = "downloading";
        await dbContext.SaveChangesAsync(cancellationToken);
        var bytes = await client.DownloadAsync(configuration.BaseUrl, result.Output, cancellationToken);
        if (bytes.Length < 1024 || bytes.Length < 12 || !bytes.AsSpan(4, 4).SequenceEqual("ftyp"u8))
        {
            throw new InvalidOperationException("ComfyUI 下载结果不是有效且大小合理的 MP4 文件。");
        }
        run.CurrentStage = "persisting";
        await dbContext.SaveChangesAsync(cancellationToken);
        await PersistAsync(run, item, spec, bytes, cancellationToken);
    }

    private async Task PersistAsync(
        ProductionRun run,
        ProductionRunItem item,
        ShotVideoRunSpec spec,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var shotAsset = await dbContext.Assets.AsNoTracking().SingleAsync(
            asset => asset.Id == item.ShotAssetId,
            cancellationToken);
        var previous = await (
            from dependency in dbContext.AssetDependencies.AsNoTracking()
            join asset in dbContext.Assets.AsNoTracking() on dependency.ConsumerAssetId equals asset.Id
            where dependency.ProjectId == run.ProjectId
                && dependency.SourceAssetId == item.ShotAssetId
                && dependency.Role == "video-for-shot"
                && asset.Type == AssetType
            orderby asset.Version descending
            select asset).FirstOrDefaultAsync(cancellationToken);
        var resourceId = previous?.ResourceId ?? Guid.NewGuid();
        var version = (previous?.Version ?? 0) + 1;
        var number = previous?.Number
            ?? (await dbContext.Assets.Where(asset => asset.ProjectId == run.ProjectId)
                .Select(asset => (int?)asset.Number)
                .MaxAsync(cancellationToken) ?? 0) + 1;
        var inputIds = JsonSerializer.Deserialize<Guid[]>(item.InputAssetIdsJson, StoryboardDefaults.JsonOptions) ?? [];
        var inputs = await dbContext.Assets.AsNoTracking()
            .Where(asset => inputIds.Contains(asset.Id))
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var output = new Asset
        {
            ProjectId = run.ProjectId,
            ProductionEpisodeId = run.ProductionEpisodeId,
            ResourceId = resourceId,
            Version = version,
            Number = number,
            Type = AssetType,
            Name = $"{shotAsset.Name}视频",
            BlobKey = $"storyboard-videos/{run.ProjectId:N}/{item.ShotResourceId:N}/v{version}.mp4",
            BlobContent = bytes,
            FileName = $"{shotAsset.Name}-视频-v{version}.mp4",
            ContentType = "video/mp4",
            SizeBytes = bytes.LongLength,
            GenerationMetadataJson = JsonSerializer.Serialize(new
            {
                operation = "generate-storyboard-shot-video",
                runId = run.Id,
                itemId = item.Id,
                promptId = item.ExternalJobId,
                prompt = spec.Prompt,
                workflowProfile = spec.WorkflowProfile,
                workflowHash = spec.WorkflowHash,
                parameters = new
                {
                    spec.Width,
                    spec.Height,
                    spec.FrameCount,
                    spec.Fps,
                    spec.DurationSeconds,
                    sampler = "euler",
                    scheduler = "simple",
                    steps = 4,
                    denoise = 1.0,
                    loraStrength = 1.0,
                    shiftVideo = 6.0,
                    shiftAudio = 3.0,
                    hasLastFrame = spec.LastFrameAssetId is not null
                },
                inputs = inputs.Select(asset => new
                {
                    asset.Id,
                    asset.ResourceId,
                    asset.Version,
                    asset.Type
                })
            }, StoryboardDefaults.JsonOptions),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.Assets.Add(output);
        var state = await dbContext.ResourceStates.SingleOrDefaultAsync(
            candidate => candidate.ProjectId == run.ProjectId
                && candidate.ResourceId == resourceId,
            cancellationToken);
        if (state is null)
        {
            state = new ResourceState
            {
                ProjectId = run.ProjectId,
                ResourceId = resourceId,
                ResourceType = AssetType
            };
            dbContext.ResourceStates.Add(state);
        }
        state.CurrentAssetId = output.Id;
        state.LifecycleStatus = "active";
        state.IsStale = false;
        state.UpdatedAtUtc = now;
        AddDependency(run.ProjectId, output.Id, item.ShotAssetId, "video-for-shot", now);
        AddDependency(run.ProjectId, output.Id, run.CreativeSettingsAssetId, "uses-settings", now);
        AddDependency(run.ProjectId, output.Id, spec.FirstFrameAssetId, "uses-first-frame", now);
        if (spec.LastFrameAssetId is Guid lastFrameId)
        {
            AddDependency(run.ProjectId, output.Id, lastFrameId, "uses-last-frame", now);
        }
        item.OutputAssetId = output.Id;
        item.Status = "completed";
        item.CompletedAtUtc = now;
        item.ErrorCode = null;
        item.ErrorDetail = null;
        run.Status = "completed";
        run.CurrentStage = "completed";
        run.FinalAssetId = output.Id;
        run.CompletedAtUtc = now;
        run.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ShotVideoContext?> ResolveContextAsync(
        Guid projectId,
        Guid episodeId,
        Guid shotResourceId,
        CancellationToken cancellationToken)
    {
        var definition = await dbContext.ShotDefinitions.AsNoTracking().SingleOrDefaultAsync(
            item => item.ProjectId == projectId
                && item.ProductionEpisodeId == episodeId
                && item.ShotResourceId == shotResourceId,
            cancellationToken);
        if (definition is null) return null;
        var project = await dbContext.Projects.AsNoTracking().SingleAsync(
            item => item.Id == projectId,
            cancellationToken);
        if (project.CurrentCreativeSettingsId is not Guid settingsAssetId)
        {
            throw new InvalidOperationException("生成视频前必须先保存项目设定。");
        }
        var shotAsset = await dbContext.Assets.AsNoTracking().SingleAsync(
            item => item.Id == definition.ShotAssetId,
            cancellationToken);
        var settingsAsset = await dbContext.Assets.AsNoTracking().SingleAsync(
            item => item.Id == settingsAssetId,
            cancellationToken);
        var shot = JsonSerializer.Deserialize<StoryboardShotDocument>(
            shotAsset.DocumentJson ?? "{}",
            StoryboardDefaults.JsonOptions)
            ?? throw new InvalidOperationException("当前镜头内容无法读取。");
        var settings = JsonSerializer.Deserialize<ProjectSettingsDocument>(
            settingsAsset.DocumentJson ?? "{}",
            ProjectSettingsDefaults.JsonOptions)
            ?? throw new InvalidOperationException("当前项目设定无法读取。");
        var firstFrame = await ResolveCurrentFrameAsync(
            projectId,
            shotAsset.ResourceId,
            "frame-for-shot",
            cancellationToken)
            ?? throw new InvalidOperationException("生成视频前必须先完成首帧制作。");
        var lastFrame = ShotProductionModes.ForShot(shot) == ShotProductionModes.FirstLastContinuous
            ? await ResolveCurrentFrameAsync(
                projectId,
                shotAsset.ResourceId,
                "last-frame-for-shot",
                cancellationToken)
            : null;
        var linkedAssetIds = await StoryboardQueries.GetLinkedAssetIdsAsync(
            dbContext, definition, cancellationToken);
        var linkedAssets = await dbContext.Assets.AsNoTracking()
            .Where(item => linkedAssetIds.Contains(item.Id))
            .ToListAsync(cancellationToken);
        var characters = new List<ShotVideoPromptCharacterContext>();
        foreach (var asset in linkedAssets)
        {
            var document = VisualAssetMapper.ReadDocument(asset);
            if (document.Kind != "character") continue;
            var voice = await voiceProfileService.GetAsync(projectId, asset.ResourceId, cancellationToken);
            characters.Add(new(
                asset.Id, asset.ResourceId, document.Name, document.Summary,
                document.VisualDescription, document.MustKeep, document.Avoid,
                voice?.AssetId, voice?.Name, voice?.DesignPrompt, voice?.Language, voice?.Seed));
        }
        return new(definition, shotAsset, settingsAsset, shot, settings, firstFrame, lastFrame, characters);
    }

    private async Task<Asset?> ResolveCurrentFrameAsync(
        Guid projectId,
        Guid shotResourceId,
        string role,
        CancellationToken cancellationToken)
    {
        var resources = await (
            from dependency in dbContext.AssetDependencies.AsNoTracking()
            join source in dbContext.Assets.AsNoTracking() on dependency.SourceAssetId equals source.Id
            join asset in dbContext.Assets.AsNoTracking() on dependency.ConsumerAssetId equals asset.Id
            where dependency.ProjectId == projectId
                && source.ResourceId == shotResourceId
                && dependency.Role == role
                && asset.Type == ShotFrameService.AssetType
            select asset.ResourceId).Distinct().ToArrayAsync(cancellationToken);
        if (resources.Length == 0) return null;
        var currentIds = await dbContext.ResourceStates.AsNoTracking()
            .Where(state => state.ProjectId == projectId
                && state.ResourceType == ShotFrameService.AssetType
                && resources.Contains(state.ResourceId))
            .Select(state => state.CurrentAssetId)
            .ToArrayAsync(cancellationToken);
        var currentFrames = await dbContext.Assets.AsNoTracking()
            .Where(asset => currentIds.Contains(asset.Id))
            .ToListAsync(cancellationToken);
        return currentFrames.OrderByDescending(asset => asset.UpdatedAtUtc).FirstOrDefault();
    }

    private static ShotVideoPromptAgentInput BuildAgentInput(ShotVideoContext context, string? instruction) => new(
        context.Settings.ProjectName,
        context.Settings.VideoPromptModel,
        context.Settings.VisualStyle,
        context.Settings.ArtDirection,
        context.Settings.CameraLanguage,
        context.Settings.SoundStrategy,
        context.Shot.DurationSeconds,
        context.Shot.ShotSize,
        context.Shot.CameraAngle,
        context.Shot.CameraMovement,
        context.Shot.Composition,
        context.Shot.VisualDescription,
        context.Shot.Action,
        context.Shot.Dialogue,
        context.Shot.Sound,
        context.Shot.FirstFrameDescription,
        context.Shot.LastFrameDescription,
        context.Shot.CutDescription,
        context.Characters,
        string.IsNullOrWhiteSpace(instruction) ? null : instruction.Trim(),
        StoryboardDialogue.From(context.Shot.DialogueCharacter, context.Shot.Dialogue).Character);

    private static string BuildPrompt(
        StoryboardShotDocument shot,
        ShotVideoPromptDraft draft,
        string? videoPromptModel) => ShotVideoPromptInstructions.UsesMiniMaxH3Format(videoPromptModel)
        ? BuildMiniMaxH3Prompt(shot, draft)
        : BuildDefaultPrompt(shot, draft);

    private static string BuildDefaultPrompt(
        StoryboardShotDocument shot,
        ShotVideoPromptDraft draft) => $$"""
        Create one continuous {{shot.DurationSeconds:0.###}}-second cinematic take.

        VISUAL: {{RemoveVisualControlInstructions(draft.VisualMotionPrompt)}}
        CAMERA: Preserve the supplied first frame's exact framing and camera axis.
        CONTINUITY: {{RemoveWrittenContentInstructions(draft.ContinuityNotes)}}

        AUDIO TIMELINE: {{BuildDialogueTimeline(shot)}}
        VOICE: {{draft.VoicePerformancePrompt}}
        AMBIENCE: {{draft.SoundPrompt}} Keep the single voice clear and centered above all other sound.

        IMAGE RESULT: A clean picture with a completely blank lower third and no readable glyphs anywhere. No titles, logos, watermarks, interface elements, speech bubbles, or written overlays. Treat every token in this prompt as an instruction only and never render prompt wording in the image.
        """;

    private static string BuildMiniMaxH3Prompt(
        StoryboardShotDocument shot,
        ShotVideoPromptDraft draft)
    {
        var duration = shot.DurationSeconds.ToString("0.00", CultureInfo.InvariantCulture);
        var alignment = ShotProductionModes.ForShot(shot) == ShotProductionModes.FirstLastContinuous
            ? $"How the reference pictures align with the target video — Picture 1 (from Shot 1) aligns with the 0.00-second mark of the target video; Picture 2 (from Shot 1) aligns with the {duration}-second mark of the target video."
            : "For the target video, at 0.00 seconds into the target video, <Picture 1> (from [Shot 1]) is fully referenced.";
        return $$"""
            {{alignment}}

            integrated_multimodal_description: [Shot 1] {{RemoveVisualControlInstructions(draft.VisualMotionPrompt)}} The camera preserves the supplied first frame's exact framing and axis. {{RemoveWrittenContentInstructions(draft.ContinuityNotes)}} Voice performance: {{draft.VoicePerformancePrompt}} The image result is a clean picture with a completely blank lower third and no readable glyphs anywhere. No titles, logos, watermarks, interface elements, speech bubbles, or written overlays.

            overall_soundscape: Audio must be present and clearly audible from 0.00 seconds. {{draft.SoundPrompt}} Keep the single voice clear and centered above all other sound.

            non_diegetic_music: N/A
            """;
    }

    private static string BuildMiniMaxH3AudioRequirement(StoryboardShotDocument shot)
    {
        var dialogue = StoryboardDialogue.From(shot.DialogueCharacter, shot.Dialogue);
        return string.IsNullOrWhiteSpace(dialogue.Text)
            ? "Audio is mandatory. Generate clearly audible, non-silent synchronized ambient sound beginning at exactly 0.00 seconds and continuing naturally through the clip. There is no spoken dialogue, but no dialogue does not mean silence."
            : $"Audio is mandatory. {dialogue.Character}'s clearly audible first spoken syllable begins immediately at exactly 0.00 seconds. Do not delay the voice, begin with silence, or move the dialogue toward the end of the clip.";
    }

    private static string BuildMiniMaxH3DialogueTimeline(StoryboardShotDocument shot)
    {
        var dialogue = StoryboardDialogue.From(shot.DialogueCharacter, shot.Dialogue);
        if (string.IsNullOrWhiteSpace(dialogue.Text))
            return "There is no spoken dialogue. Generate the specified synchronized ambient sound throughout the clip.";
        var speechEnd = shot.DurationSeconds - StoryboardDialogue.TailBufferSeconds;
        return $"At 0.00 seconds, the on-screen character {dialogue.Character} (S1) begins speaking immediately in Mandarin Chinese: <d>[Chinese] {dialogue.Text}</d> "
            + $"The complete <d> line and final syllable end no later than {speechEnd:0.#} seconds. From {speechEnd:0.#} to {shot.DurationSeconds:0.###} seconds, {dialogue.Character} (S1) is vocally silent with closed lips while natural ambience remains audible.";
    }

    private static string BuildDialogueTimeline(StoryboardShotDocument shot)
    {
        var dialogue = StoryboardDialogue.From(shot.DialogueCharacter, shot.Dialogue);
        if (string.IsNullOrWhiteSpace(dialogue.Text))
            return "No human voice for the entire clip. All faces remain naturally closed-mouth.";
        var speechEnd = shot.DurationSeconds - StoryboardDialogue.TailBufferSeconds;
        return $"From 0.0 to {speechEnd:0.#} seconds, character {dialogue.Character} says exactly once in Mandarin Chinese: \"{dialogue.Text}\" "
            + $"Every written character and the final syllable must be complete by {speechEnd:0.#} seconds. From {speechEnd:0.#} to {shot.DurationSeconds:0.###} seconds there is absolute vocal silence: no omission, paraphrase, extra syllables, repetition, humming, or vocalization. {dialogue.Character}'s lips remain closed after the sentence.";
    }

    private static string RemoveWrittenContentInstructions(string value)
    {
        var forbiddenTerms = new[]
        {
            "subtitle", "caption", "text", "word", "glyph", "title",
            "logo", "watermark", "interface", "speech bubble"
        };
        return RemoveControlSentences(value, forbiddenTerms, "Preserve identity, wardrobe, lighting, spatial relationships, and camera axis.");
    }

    private static string RemoveVisualControlInstructions(string value) => RemoveControlSentences(
        value,
        [
            "speak", "speech", "voice", "dialogue", "deliver", "direct address",
            "mouth", "lip", "word", "text", "subtitle", "caption"
        ],
        "Maintain restrained natural body motion while simple scene icons appear sequentially around the host.");

    private static string RemoveControlSentences(
        string value,
        IReadOnlyList<string> forbiddenTerms,
        string fallback)
    {
        var result = string.Join(" ", Regex.Split(value, @"(?<=[.!?])\s+")
            .Where(sentence => !forbiddenTerms.Any(term =>
                sentence.Contains(term, StringComparison.OrdinalIgnoreCase))));
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }

    private static int CalculateFrameCount(double durationSeconds, int fps, string workflowProfile)
    {
        var requestedFrames = Math.Max(9, (int)Math.Ceiling(durationSeconds * fps));
        if (ComfyUiConfigurationView.NormalizeVideoWorkflow(workflowProfile)
            == ComfyUiConfigurationView.Ltx23VideoWorkflow)
        {
            return 8 * (int)Math.Ceiling((requestedFrames - 1) / 8d) + 1;
        }
        return 17 * (int)Math.Ceiling((requestedFrames - 5) / 17d) + 5;
    }

    private static double CalculateEffectiveDuration(StoryboardShotDocument shot, string workflowProfile)
    {
        if (ComfyUiConfigurationView.NormalizeVideoWorkflow(workflowProfile)
            != ComfyUiConfigurationView.Ltx23VideoWorkflow)
            return shot.DurationSeconds;
        var dialogue = StoryboardDialogue.From(shot.DialogueCharacter, shot.Dialogue);
        var spokenCharacters = dialogue.SpokenCharacterCount;
        if (spokenCharacters == 0) return shot.DurationSeconds;
        var required = spokenCharacters / 2.5 + 1.0;
        return Math.Max(shot.DurationSeconds, Math.Ceiling(required * 10) / 10);
    }

    private static string ApplyWorkflowPrompt(
        StoryboardShotDocument shot,
        string prompt,
        string workflowProfile,
        double durationSeconds)
    {
        var normalizedWorkflow = ComfyUiConfigurationView.NormalizeVideoWorkflow(workflowProfile);
        if (normalizedWorkflow == ComfyUiConfigurationView.MinimaxVideoWorkflow)
            return ApplyMiniMaxH3ExecutionRequirements(shot, prompt);
        if (normalizedWorkflow != ComfyUiConfigurationView.Ltx23VideoWorkflow) return prompt;
        var dialogue = StoryboardDialogue.From(shot.DialogueCharacter, shot.Dialogue);
        if (string.IsNullOrWhiteSpace(dialogue.Text) || prompt.Contains("LTX AUDIO TIMING:", StringComparison.Ordinal))
            return prompt;
        var speechEnd = durationSeconds - 1.0;
        return $"{prompt}\n\nLTX AUDIO TIMING: Character {dialogue.Character} begins speaking immediately at 0.0 seconds. Say exactly once in Mandarin Chinese: \"{dialogue.Text}\" Complete every written character and the final syllable no later than {speechEnd:0.0} seconds. Do not omit, shorten, paraphrase, repeat, or trail off. From {speechEnd:0.0} to {durationSeconds:0.0} seconds, keep the voice silent and retain only natural ambience.";
    }

    private static string ApplyMiniMaxH3ExecutionRequirements(StoryboardShotDocument shot, string prompt)
    {
        if (prompt.Contains(MiniMaxH3ExecutionMarker, StringComparison.Ordinal)) return prompt;

        var dialogue = StoryboardDialogue.From(shot.DialogueCharacter, shot.Dialogue);
        prompt = prompt
            .Replace(BuildDialogueTimeline(shot), string.Empty, StringComparison.Ordinal)
            .Replace(BuildMiniMaxH3DialogueTimeline(shot), string.Empty, StringComparison.Ordinal)
            .Replace(BuildMiniMaxH3AudioRequirement(shot), string.Empty, StringComparison.Ordinal)
            .Replace(MiniMaxH3ContinuousShotRequirement, string.Empty, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(dialogue.Text))
        {
            var legacyTimelinePattern = $"From 0\\.0 to .*?character {Regex.Escape(dialogue.Character)} says exactly once in Mandarin Chinese: \"{Regex.Escape(dialogue.Text)}\".*?{Regex.Escape(dialogue.Character)}'s lips remain closed after the sentence\\.";
            prompt = Regex.Replace(prompt, legacyTimelinePattern, string.Empty, RegexOptions.Singleline);
        }
        var requirements = $"{MiniMaxH3ExecutionMarker} {BuildMiniMaxH3AudioRequirement(shot)} {MiniMaxH3ContinuousShotRequirement} {BuildMiniMaxH3DialogueTimeline(shot)} ";
        const string shotPrefix = "integrated_multimodal_description: [Shot 1] ";
        var insertionIndex = prompt.IndexOf(shotPrefix, StringComparison.Ordinal);
        return insertionIndex < 0
            ? $"{prompt.Trim()}\n\n{requirements.Trim()}"
            : prompt.Insert(insertionIndex + shotPrefix.Length, requirements);
    }

    private static int NormalizeDimension(int value) =>
        Math.Max(DimensionMultiple, ((value + DimensionMultiple / 2) / DimensionMultiple) * DimensionMultiple);

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private void AddDependency(Guid projectId, Guid consumerId, Guid sourceId, string role, DateTimeOffset now) =>
        dbContext.AssetDependencies.Add(new AssetDependency
        {
            ProjectId = projectId,
            ConsumerAssetId = consumerId,
            SourceAssetId = sourceId,
            Role = role,
            IsRequired = true,
            CreatedAtUtc = now
        });

    private sealed record ShotVideoContext(
        ShotDefinition Definition,
        Asset ShotAsset,
        Asset SettingsAsset,
        StoryboardShotDocument Shot,
        ProjectSettingsDocument Settings,
        Asset FirstFrame,
        Asset? LastFrame,
        IReadOnlyList<ShotVideoPromptCharacterContext> Characters);
}

internal static class ShotVideoQueries
{
    public static async Task<ShotVideoProductionView?> GetAsync(
        V2DbContext dbContext,
        Guid projectId,
        Guid episodeId,
        Guid shotResourceId,
        CancellationToken cancellationToken)
    {
        var currentShotAssetId = await dbContext.ShotDefinitions.AsNoTracking()
            .Where(item => item.ProjectId == projectId
                && item.ProductionEpisodeId == episodeId
                && item.ShotResourceId == shotResourceId)
            .Select(item => (Guid?)item.ShotAssetId)
            .SingleOrDefaultAsync(cancellationToken);
        if (currentShotAssetId is null) return null;
        var rows = await (
            from run in dbContext.ProductionRuns.AsNoTracking()
            join item in dbContext.ProductionRunItems.AsNoTracking() on run.Id equals item.RunId
            where run.ProjectId == projectId
                && run.ProductionEpisodeId == episodeId
                && run.RunType == ShotVideoService.RunType
                && item.ShotResourceId == shotResourceId
                && item.ShotAssetId == currentShotAssetId
            select new { Run = run, Item = item }).ToListAsync(cancellationToken);
        var row = rows
            .OrderByDescending(candidate => candidate.Run.Status is "queued" or "running" ? 2 : candidate.Run.Status == "completed" ? 1 : 0)
            .ThenByDescending(candidate => candidate.Run.CreatedAtUtc)
            .FirstOrDefault();
        if (row is null) return null;
        var spec = JsonSerializer.Deserialize<ShotVideoRunSpec>(row.Run.SpecJson, StoryboardDefaults.JsonOptions);
        Asset? output = null;
        if (row.Item.OutputAssetId is Guid outputId)
        {
            var source = await dbContext.Assets.AsNoTracking().SingleOrDefaultAsync(
                asset => asset.Id == outputId && asset.Type == ShotVideoService.AssetType,
                cancellationToken);
            if (source is not null)
            {
                var currentId = await dbContext.ResourceStates.AsNoTracking()
                    .Where(state => state.ProjectId == projectId
                        && state.ResourceId == source.ResourceId
                        && state.ResourceType == ShotVideoService.AssetType)
                    .Select(state => (Guid?)state.CurrentAssetId)
                    .SingleOrDefaultAsync(cancellationToken);
                output = await dbContext.Assets.AsNoTracking().SingleOrDefaultAsync(
                    asset => asset.Id == (currentId ?? source.Id),
                    cancellationToken);
            }
        }
        return new(
            row.Run.Id,
            row.Run.Status,
            row.Run.CurrentStage,
            output?.Id,
            output is null
                ? null
                : $"/api/v2/projects/{projectId}/storyboard/videos/{output.Id}/content",
            output?.Version,
            spec?.Prompt ?? string.Empty,
            row.Run.CreatedAtUtc,
            row.Run.LastError);
    }
}

public sealed class ShotVideoWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ShotVideoWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IShotVideoService>();
                var processed = await service.ProcessNextAsync(stoppingToken);
                await Task.Delay(processed ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Shot video worker iteration failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}

public static class ShotVideoEndpoints
{
    public static IEndpointRouteBuilder MapShotVideos(this IEndpointRouteBuilder app)
    {
        var route = "/api/v2/projects/{projectId:guid}/production-episodes/{productionEpisodeId:guid}/storyboard/shots/{shotResourceId:guid}/video";
        app.MapPost($"{route}/preview", async (
            Guid projectId,
            Guid productionEpisodeId,
            Guid shotResourceId,
            string? instruction,
            IShotVideoService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var preview = await service.PreviewAsync(projectId, productionEpisodeId, shotResourceId, instruction, cancellationToken);
                return preview is null ? Results.NotFound() : Results.Ok(preview);
            }
            catch (InvalidOperationException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
        });
        app.MapPost($"{route}/start", async (
            Guid projectId,
            Guid productionEpisodeId,
            Guid shotResourceId,
            StartShotVideoRequest request,
            IShotVideoService service,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.ConfirmedPrompt)
                || string.IsNullOrWhiteSpace(request.PreviewHash))
            {
                return Results.BadRequest(new { error = "请先预览并确认视频提示词和参数。" });
            }
            try
            {
                var production = await service.StartAsync(
                    projectId,
                    productionEpisodeId,
                    shotResourceId,
                    request.ConfirmedPrompt,
                    request.PreviewHash,
                    request.Instruction,
                    cancellationToken);
                return production is null ? Results.NotFound() : Results.Accepted(value: production);
            }
            catch (InvalidOperationException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
        });
        app.MapGet($"{route}", async (
            Guid projectId,
            Guid productionEpisodeId,
            Guid shotResourceId,
            V2DbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var production = await ShotVideoQueries.GetAsync(
                dbContext,
                projectId,
                productionEpisodeId,
                shotResourceId,
                cancellationToken);
            return production is null ? Results.NotFound() : Results.Ok(production);
        });
        app.MapGet("/api/v2/projects/{projectId:guid}/storyboard/videos/{assetId:guid}/content", async (
            Guid projectId,
            Guid assetId,
            V2DbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var video = await dbContext.Assets.AsNoTracking().SingleOrDefaultAsync(
                asset => asset.Id == assetId
                    && asset.ProjectId == projectId
                    && asset.Type == ShotVideoService.AssetType,
                cancellationToken);
            return video?.BlobContent is null
                ? Results.NotFound()
                : Results.File(
                    video.BlobContent,
                    video.ContentType ?? "video/mp4",
                    video.FileName,
                    enableRangeProcessing: true);
        });
        return app;
    }
}