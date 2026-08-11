using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text;
using AlexDirectorConsole.Api.Data;
using AlexDirectorConsole.Api.Models;
using AlexDirectorConsole.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Services;

public static class AgentSkillIds
{
    public const string ScriptBreakdown = "script-breakdown";
    public const string CharacterTurnaround = "character-turnaround";
}

public sealed record SkillExecutionResult(
    SkillRun Run,
    Asset? OutputAsset,
    IReadOnlyList<Asset> GeneratedAssets);

public sealed record SkillProgress(string Type, string Message, object? Data = null);

public interface IAgentSkillExecutor
{
    Task<SkillExecutionResult> ExecuteScriptBreakdownAsync(
        Guid projectId,
        Guid assetId,
        string directorInstruction,
        string? requestedDeployment,
        Func<SkillProgress, ValueTask>? progress = null,
        CancellationToken cancellationToken = default);

    Task<int> BackfillAnalysisAssetsAsync(CancellationToken cancellationToken = default);
}

public sealed class AgentSkillExecutor(
    AppDbContext dbContext,
    IBlobStorage blobStorage,
    IDirectorAgent directorAgent) : IAgentSkillExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public async Task<SkillExecutionResult> ExecuteScriptBreakdownAsync(
        Guid projectId,
        Guid assetId,
        string directorInstruction,
        string? requestedDeployment,
        Func<SkillProgress, ValueTask>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var skill = await dbContext.SkillDefinitions
            .SingleOrDefaultAsync(item => item.Id == AgentSkillIds.ScriptBreakdown, cancellationToken)
            ?? throw new InvalidOperationException("剧本拆解技能不存在。");
        if (!skill.IsEnabled)
        {
            throw new InvalidOperationException("剧本拆解技能已停用。");
        }

        var asset = await dbContext.Assets
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == assetId && item.ProjectId == projectId,
                cancellationToken)
            ?? throw new InvalidOperationException("找不到当前项目中的输入资源。");
        if (asset.Type != "script" || !IsTextAsset(asset))
        {
            throw new InvalidOperationException("剧本拆解技能只接受文本剧本资源。");
        }

        var run = new SkillRun
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            SkillId = skill.Id,
            InputAssetId = asset.Id,
            Status = "running",
            DirectorInstruction = directorInstruction,
            Model = string.IsNullOrWhiteSpace(requestedDeployment)
                ? directorAgent.Deployment
                : requestedDeployment.Trim(),
            StartedAtUtc = DateTime.UtcNow
        };
        dbContext.SkillRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
        await ReportAsync(progress, new("skill.started", "已启动「剧本拆解」技能", new { runId = run.Id }));

        try
        {
            await ReportAsync(progress, new("tool.started", $"正在读取剧本资源：{asset.FileName}"));
            await using var content = await blobStorage.OpenReadAsync(asset.BlobKey, cancellationToken)
                ?? throw new InvalidOperationException("剧本 Blob 不存在。");
            using var reader = new StreamReader(content, detectEncodingFromByteOrderMarks: true);
            var script = await reader.ReadToEndAsync(cancellationToken);
            await ReportAsync(progress, new("tool.completed", $"已读取剧本，共 {script.Length:N0} 个字符"));
            await ReportAsync(progress, new("agent.started", $"正在调用 {run.Model} 分析剧本并编写资源"));
            var generatedAssets = new List<Asset>();
            using var toolLock = new SemaphoreSlim(1, 1);
            var writeResourceTool = AIFunctionFactory.Create(
                (Func<string, string, string, CancellationToken, Task<string>>)(async (
                    resourceType,
                    resourceName,
                    markdownContent,
                    toolCancellationToken) =>
                {
                    await toolLock.WaitAsync(toolCancellationToken);
                    try
                    {
                        var writtenAsset = await WriteProjectResourceAsync(
                            projectId,
                            asset,
                            run.Id,
                            resourceType,
                            resourceName,
                            markdownContent,
                            progress,
                            toolCancellationToken);
                        if (generatedAssets.All(item => item.Id != writtenAsset.Id))
                        {
                            generatedAssets.Add(writtenAsset);
                        }
                        return JsonSerializer.Serialize(Contracts.AssetResponse.FromAsset(writtenAsset), JsonOptions);
                    }
                    finally
                    {
                        toolLock.Release();
                    }
                }),
                name: "write_project_resource",
                description: "将 Agent 已完成的 Markdown 内容写入当前项目资源。resourceType 只能是 analysis、character、scene 或 prop；每个实体必须单独调用一次。",
                serializerOptions: JsonOptions);
            var input = $"导演令：{directorInstruction}\n\n剧本文件：{asset.FileName}\n\n剧本原文：\n{script}";
            await ReportAsync(progress, new("agent.stage.started", "Agent 开始拆解剧本并编写制作资源"));
            await foreach (var _ in directorAgent.StreamSkillWithToolsAsync(
                skill.Name,
                ScriptBreakdownInstructions,
                input,
                requestedDeployment,
                [writeResourceTool],
                cancellationToken))
            {
            }
            await ReportAsync(progress, new("agent.stage.completed", "Agent 已完成剧本拆解"));

            var outputAsset = generatedAssets.FirstOrDefault(item => item.Type == "analysis")
                ?? generatedAssets.FirstOrDefault();
            var counts = generatedAssets
                .GroupBy(item => item.Type)
                .ToDictionary(group => group.Key, group => group.Count());
            await ReportAsync(progress, new(
                "resources.completed",
                $"Agent 已通过工具写入 {generatedAssets.Count} 个资源：人物 {GetCount(counts, "character")}、场景 {GetCount(counts, "scene")}、道具 {GetCount(counts, "prop")}"));

            run.Status = "succeeded";
            run.ResultJson = JsonSerializer.Serialize(
                generatedAssets.Select(Contracts.AssetResponse.FromAsset),
                JsonOptions);
            run.OutputAssetId = outputAsset?.Id;
            run.CompletedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await ReportAsync(progress, new("skill.completed", "剧本拆解技能执行完成"));

            return new SkillExecutionResult(run, outputAsset, generatedAssets);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            run.Status = "failed";
            run.Error = exception.Message;
            run.CompletedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private static ValueTask ReportAsync(
        Func<SkillProgress, ValueTask>? progress,
        SkillProgress update) =>
        progress is null ? ValueTask.CompletedTask : progress(update);

    public async Task<int> BackfillAnalysisAssetsAsync(
        CancellationToken cancellationToken = default)
    {
        var runs = await dbContext.SkillRuns
            .Where(run =>
                run.SkillId == AgentSkillIds.ScriptBreakdown
                && run.Status == "succeeded"
                && run.OutputAssetId == null
                && run.ResultJson != null)
            .ToListAsync(cancellationToken);
        var created = 0;

        foreach (var run in runs)
        {
            var inputAsset = await dbContext.Assets
                .AsNoTracking()
                .SingleOrDefaultAsync(asset => asset.Id == run.InputAssetId, cancellationToken);
            if (inputAsset is null)
            {
                continue;
            }

            var outputAsset = await SaveAnalysisAssetAsync(
                run.ProjectId,
                inputAsset,
                run.Id,
                run.ResultJson!,
                cancellationToken,
                run.Id);
            run.OutputAssetId = outputAsset.Id;
            await dbContext.SaveChangesAsync(cancellationToken);
            created++;
        }

        return created;
    }

    private static bool IsTextAsset(Asset asset) =>
        asset.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(asset.FileName).Equals(".md", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(asset.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase);

    private const string ScriptBreakdownInstructions = """
        你是 Script Agent，负责拆解剧本并建立制作资源。
        只能依据剧本原文，不得补写剧情，不得把推断包装成事实；不确定信息必须明确标为“待导演确认”。

        自主分析剧本，并按实际需要调用 write_project_resource：
        - analysis：完整剧本分析稿，包含故事梗概、逐场事件、人物、场景、关键道具和待确认项。
        - character：每个有名字且参与剧情的人物分别建立资源。
        - scene：每个独立拍摄场景分别建立资源。
        - prop：为需要制作、采购或保持连续性的关键道具分别建立资源，不为普通背景物件建稿。

        直接调用工具写入最终资源，不要只给摘要，也不要让宿主替你生成内容。
        markdownContent 必须是完整 Markdown 正文。
        resourceName 只填写剧本中的实体名称，不附加“设定稿”等后缀；合并同一实体的重复出现。
        完成所有工具调用后，只用一句话报告实际写入数量。
        """;

    private async Task<Asset> SaveAnalysisAssetAsync(
        Guid projectId,
        Asset inputAsset,
        Guid runId,
        string resultJson,
        CancellationToken cancellationToken,
        Guid? requestedAssetId = null)
    {
        var assetId = requestedAssetId ?? Guid.NewGuid();
        var existing = await dbContext.Assets
            .SingleOrDefaultAsync(asset => asset.Id == assetId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var fileName = $"{Path.GetFileNameWithoutExtension(inputAsset.FileName)}-剧本分析.json";
        var resourceName = $"{inputAsset.Name} · 剧本分析";
        var versions = await FindResourceVersionsAsync(
            projectId,
            "analysis",
            resourceName,
            cancellationToken);
        var latestVersion = versions.LastOrDefault();
        var bytes = System.Text.Encoding.UTF8.GetBytes(resultJson);
        var now = DateTimeOffset.UtcNow;
        var outputAsset = new Asset
        {
            Id = assetId,
            ResourceId = latestVersion?.ResourceId ?? assetId,
            Version = (latestVersion?.Version ?? 0) + 1,
            ProjectId = projectId,
            Type = "analysis",
            Name = latestVersion?.Name ?? resourceName,
            BlobKey = $"{projectId:N}/analysis/{assetId:N}.json",
            FileName = fileName,
            ContentType = "application/json",
            SizeBytes = bytes.LongLength,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await using var stream = new MemoryStream(bytes, writable: false);
        await blobStorage.SaveAsync(outputAsset.BlobKey, stream, cancellationToken);
        try
        {
            dbContext.Assets.Add(outputAsset);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await blobStorage.DeleteAsync(outputAsset.BlobKey, CancellationToken.None);
            throw;
        }

        return outputAsset;
    }

    private async Task<Asset> WriteProjectResourceAsync(
        Guid projectId,
        Asset inputAsset,
        Guid runId,
        string resourceType,
        string resourceName,
        string markdownContent,
        Func<SkillProgress, ValueTask>? progress,
        CancellationToken cancellationToken)
    {
        var type = resourceType.Trim().ToLowerInvariant();
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["analysis"] = "剧本分析稿",
            ["character"] = "人物设定稿",
            ["scene"] = "场景设定稿",
            ["prop"] = "道具设定稿"
        };
        if (!labels.TryGetValue(type, out var label))
        {
            throw new ArgumentException("resourceType 只能是 analysis、character、scene 或 prop。", nameof(resourceType));
        }

        var name = resourceName.Trim();
        var content = markdownContent.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
        {
            throw new ArgumentException("resourceName 不能为空且不能超过 160 个字符。", nameof(resourceName));
        }
        if (string.IsNullOrWhiteSpace(content) || content.Length > 200000)
        {
            throw new ArgumentException("markdownContent 不能为空且不能超过 200,000 个字符。", nameof(markdownContent));
        }

        await ReportAsync(progress, new(
            "tool.started",
            $"Agent 调用 write_project_resource：{label}「{name}」"));
        var assetId = CreateDeterministicAssetId(runId, type, name);
        var existing = await dbContext.Assets
            .AsNoTracking()
            .SingleOrDefaultAsync(asset => asset.Id == assetId, cancellationToken);
        if (existing is not null)
        {
            await ReportAsync(progress, new("tool.completed", $"资源已存在：{existing.Name}"));
            return existing;
        }

        var bytes = Encoding.UTF8.GetBytes(content + Environment.NewLine);
        var now = DateTimeOffset.UtcNow;
        var canonicalName = type == "analysis" ? $"{inputAsset.Name} · {label}" : $"{name} · {label}";
        var versions = await FindResourceVersionsAsync(
            projectId,
            type,
            canonicalName,
            cancellationToken);
        var latestVersion = versions.LastOrDefault();
        var outputAsset = new Asset
        {
            Id = assetId,
            ResourceId = latestVersion?.ResourceId ?? assetId,
            Version = (latestVersion?.Version ?? 0) + 1,
            ProjectId = projectId,
            Type = type,
            Name = latestVersion?.Name ?? canonicalName,
            BlobKey = $"{projectId:N}/{type}/{assetId:N}.md",
            FileName = $"{SanitizeFileName(name)}-{label}-v{(latestVersion?.Version ?? 0) + 1}.md",
            ContentType = "text/markdown; charset=utf-8",
            SizeBytes = bytes.LongLength,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await using var stream = new MemoryStream(bytes, writable: false);
        await blobStorage.SaveAsync(outputAsset.BlobKey, stream, cancellationToken);
        try
        {
            dbContext.Assets.Add(outputAsset);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await blobStorage.DeleteAsync(outputAsset.BlobKey, CancellationToken.None);
            throw;
        }

        await ReportAsync(progress, new(
            "tool.completed",
            $"Agent 已写入资源：{outputAsset.Name}",
            new { asset = Contracts.AssetResponse.FromAsset(outputAsset) }));
        return outputAsset;
    }

    private async Task<List<Asset>> FindResourceVersionsAsync(
        Guid projectId,
        string type,
        string resourceName,
        CancellationToken cancellationToken)
    {
        var subject = GetResourceSubject(resourceName);
        return (await dbContext.Assets
                .AsNoTracking()
                .Where(asset => asset.ProjectId == projectId && asset.Type == type)
                .ToListAsync(cancellationToken))
            .Where(asset => GetResourceSubject(asset.Name)
                .Equals(subject, StringComparison.OrdinalIgnoreCase))
            .OrderBy(asset => asset.Version)
            .ToList();
    }

    private static string GetResourceSubject(string value) =>
        value.Split('·', StringSplitOptions.TrimEntries)[0];

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "未命名" : sanitized.Trim();
    }

    private static Guid CreateDeterministicAssetId(Guid runId, string type, string name)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes($"{runId:N}:{type}:{name.Trim().ToUpperInvariant()}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static int GetCount(IReadOnlyDictionary<string, int> counts, string type) =>
        counts.TryGetValue(type, out var count) ? count : 0;
}