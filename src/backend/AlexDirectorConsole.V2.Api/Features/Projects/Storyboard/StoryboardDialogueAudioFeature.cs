using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AlexDirectorConsole.V2.Api.Features.Projects.Assets;
using AlexDirectorConsole.V2.Api.Features.Projects.Voice;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Storyboard;

public sealed record StoryboardDialogueAudioView(
    Guid AssetId,
    Guid ShotResourceId,
    int Version,
    string ContentUrl,
    string Text,
    string VoiceName,
    double DurationSeconds,
    DateTimeOffset CreatedAtUtc);

public sealed record ComfyUiDialogueRequest(
    string BaseUrl,
    string Text,
    string DesignPrompt,
    string Language,
    int Seed,
    string OutputPrefix);

public sealed record GeneratedDialogueAudio(byte[] Bytes, int SampleRate, double DurationSeconds);

public interface IComfyUiDialogueClient
{
    Task<GeneratedDialogueAudio> GenerateAsync(
        ComfyUiDialogueRequest request,
        CancellationToken cancellationToken);
}

public sealed class ComfyUiDialogueClient(IHttpClientFactory httpClientFactory) : IComfyUiDialogueClient
{
    public async Task<GeneratedDialogueAudio> GenerateAsync(
        ComfyUiDialogueRequest request,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("ComfyUiVideo");
        var root = new Uri(request.BaseUrl.TrimEnd('/') + "/");
        var workflow = new JsonObject
        {
            ["1"] = new JsonObject
            {
                ["class_type"] = "AlexQwen3TTS",
                ["inputs"] = new JsonObject
                {
                    ["text"] = request.Text,
                    ["design_prompt"] = request.DesignPrompt,
                    ["language"] = request.Language,
                    ["seed"] = request.Seed
                }
            },
            ["2"] = new JsonObject
            {
                ["class_type"] = "AlexSaveAudioWav",
                ["inputs"] = new JsonObject
                {
                    ["audio"] = new JsonArray("1", 0),
                    ["filename_prefix"] = request.OutputPrefix
                }
            }
        };
        using var response = await client.PostAsJsonAsync(
            new Uri(root, "prompt"),
            new { prompt = workflow },
            cancellationToken);
        var responseBody = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"ComfyUI 拒绝 TTS workflow：{responseBody}");
        var promptId = responseBody?["prompt_id"]?.GetValue<string>()
            ?? throw new InvalidOperationException("ComfyUI 未返回 TTS prompt_id。");

        var deadline = DateTimeOffset.UtcNow.AddMinutes(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var history = await client.GetFromJsonAsync<JsonObject>(
                new Uri(root, $"history/{Uri.EscapeDataString(promptId)}"),
                cancellationToken);
            if (history?[promptId] is JsonObject record)
            {
                if (FindAudioOutput(record["outputs"]) is { } output)
                {
                    var path = $"view?filename={Uri.EscapeDataString(output.FileName)}"
                        + $"&subfolder={Uri.EscapeDataString(output.Subfolder)}"
                        + $"&type={Uri.EscapeDataString(output.Type)}";
                    var bytes = await client.GetByteArrayAsync(new Uri(root, path), cancellationToken);
                    VoiceWave.Validate(bytes);
                    return new(bytes, VoiceWave.ReadSampleRate(bytes), VoiceWave.ReadDurationSeconds(bytes));
                }
                if (string.Equals(record["status"]?["status_str"]?.GetValue<string>(), "error", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"ComfyUI TTS 执行失败：{record["status"]}");
            }
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        throw new TimeoutException("等待 ComfyUI TTS 结果超时。");
    }

    private static ComfyUiVideoOutput? FindAudioOutput(JsonNode? outputs)
    {
        if (outputs is not JsonObject nodes) return null;
        foreach (var output in nodes.Select(item => item.Value).OfType<JsonObject>())
        {
            if (output["audio"] is not JsonArray files) continue;
            foreach (var file in files.OfType<JsonObject>())
            {
                var fileName = file["filename"]?.GetValue<string>();
                if (fileName?.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) != true) continue;
                return new(fileName, file["subfolder"]?.GetValue<string>() ?? string.Empty, file["type"]?.GetValue<string>() ?? "output");
            }
        }
        return null;
    }
}

public interface IStoryboardDialogueAudioService
{
    Task<StoryboardDialogueAudioView> GenerateAsync(Guid projectId, Guid productionEpisodeId, Guid shotResourceId, CancellationToken cancellationToken);
    Task<StoryboardDialogueAudioView?> GetCurrentAsync(Guid projectId, Guid shotResourceId, CancellationToken cancellationToken);
    Task<BatchStoryboardMediaResult> GenerateMissingAsync(Guid projectId, Guid productionEpisodeId, CancellationToken cancellationToken);
}

internal static class StoryboardDialogueAudioQueries
{
    public static async Task<IReadOnlyDictionary<Guid, StoryboardDialogueAudioView>> GetCurrentByShotAsync(
        V2DbContext dbContext,
        Guid projectId,
        IReadOnlyDictionary<Guid, Guid> shotResourceIdsByAssetId,
        CancellationToken cancellationToken)
    {
        if (shotResourceIdsByAssetId.Count == 0) return new Dictionary<Guid, StoryboardDialogueAudioView>();
        var shotAssetIds = shotResourceIdsByAssetId.Keys.ToArray();
        var rows = await (
            from dependency in dbContext.AssetDependencies.AsNoTracking()
            join audio in dbContext.Assets.AsNoTracking() on dependency.ConsumerAssetId equals audio.Id
            join state in dbContext.ResourceStates.AsNoTracking() on audio.Id equals state.CurrentAssetId
            where dependency.ProjectId == projectId
                && dependency.Role == "dialogue-for-shot"
                && shotAssetIds.Contains(dependency.SourceAssetId)
                && audio.Type == StoryboardDialogueAudioService.AssetType
                && state.ResourceType == StoryboardDialogueAudioService.AssetType
            select new { dependency.SourceAssetId, Audio = audio })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(
            item => shotResourceIdsByAssetId[item.SourceAssetId],
            item => StoryboardDialogueAudioService.ToView(item.Audio, shotResourceIdsByAssetId[item.SourceAssetId]));
    }
}

public sealed class StoryboardDialogueAudioService(
    V2DbContext dbContext,
    IVoiceProfileService voiceProfileService,
    IComfyUiDialogueClient comfyUiClient,
    TimeProvider timeProvider) : IStoryboardDialogueAudioService
{
    public const string AssetType = "storyboard-dialogue-audio";

    public async Task<StoryboardDialogueAudioView> GenerateAsync(
        Guid projectId,
        Guid productionEpisodeId,
        Guid shotResourceId,
        CancellationToken cancellationToken)
    {
        var definition = await dbContext.ShotDefinitions.AsNoTracking().SingleOrDefaultAsync(
            item => item.ProjectId == projectId
                && item.ProductionEpisodeId == productionEpisodeId
                && item.ShotResourceId == shotResourceId,
            cancellationToken)
            ?? throw new InvalidOperationException("镜头不存在。");
        var shotAsset = await dbContext.Assets.AsNoTracking().SingleAsync(item => item.Id == definition.ShotAssetId, cancellationToken);
        var shot = JsonSerializer.Deserialize<StoryboardShotDocument>(shotAsset.DocumentJson ?? "{}", StoryboardDefaults.JsonOptions)
            ?? throw new InvalidOperationException("镜头内容无效。");
        var dialogue = StoryboardDialogue.From(shot.DialogueCharacter, shot.Dialogue);
        if (string.IsNullOrWhiteSpace(dialogue.Text)) throw new InvalidOperationException("当前镜头没有对白。");
        if (string.IsNullOrWhiteSpace(dialogue.Character)) throw new InvalidOperationException("当前镜头对白缺少角色。");

        var linkedAssetIds = await StoryboardQueries.GetLinkedAssetIdsAsync(dbContext, definition, cancellationToken);
        var linkedAssets = await dbContext.Assets.AsNoTracking()
            .Where(item => linkedAssetIds.Contains(item.Id))
            .ToListAsync(cancellationToken);
        var character = linkedAssets.FirstOrDefault(item =>
        {
            var document = VisualAssetMapper.ReadDocument(item);
            return document.Kind == "character"
                && string.Equals(document.Name, dialogue.Character, StringComparison.OrdinalIgnoreCase);
        });
        if (character is null)
            throw new InvalidOperationException($"对白角色“{dialogue.Character}”没有绑定到当前镜头的角色资产。");
        var voice = await voiceProfileService.GetAsync(projectId, character.ResourceId, cancellationToken);
        if (voice is null)
        {
            throw new InvalidOperationException($"角色“{character.Name}”没有音色设定。");
        }
        var configuration = await dbContext.ComfyUiConfigurations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (configuration is null || !configuration.IsEnabled) throw new InvalidOperationException("请先启用 ComfyUI。");

        var generated = await comfyUiClient.GenerateAsync(
            new(
                configuration.BaseUrl,
                dialogue.Text,
                voice.DesignPrompt,
                voice.Language,
                voice.Seed,
                $"alex-dialogue/{projectId:N}/S{shot.SceneNumber:D2}-{shot.ShotNumber:D2}"),
            cancellationToken);
        var previous = await FindCurrentAssetAsync(projectId, shotResourceId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var asset = new Asset
        {
            ProjectId = projectId,
            ProductionEpisodeId = productionEpisodeId,
            ResourceId = previous?.ResourceId ?? Guid.NewGuid(),
            Version = (previous?.Version ?? 0) + 1,
            Number = previous?.Number ?? await NextNumberAsync(projectId, cancellationToken),
            Type = AssetType,
            Name = $"S{shot.SceneNumber:D2}-{shot.ShotNumber:D2} 对白配音",
            BlobKey = $"storyboard-dialogue/{projectId:N}/{shotResourceId:N}/v{(previous?.Version ?? 0) + 1}.wav",
            BlobContent = generated.Bytes,
            FileName = $"S{shot.SceneNumber:D2}-{shot.ShotNumber:D2}-dialogue-v{(previous?.Version ?? 0) + 1}.wav",
            ContentType = "audio/wav",
            SizeBytes = generated.Bytes.LongLength,
            GenerationMetadataJson = JsonSerializer.Serialize(new
            {
                operation = "comfyui-qwen3-tts-dialogue",
                character = dialogue.Character,
                text = dialogue.Text,
                shotAssetId = shotAsset.Id,
                voiceProfileAssetId = voice.AssetId,
                voice.Name,
                voice.Language,
                voice.Seed,
                generated.SampleRate,
                generated.DurationSeconds
            }, StoryboardDefaults.JsonOptions),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.Assets.Add(asset);
        var state = await dbContext.ResourceStates.SingleOrDefaultAsync(
            item => item.ProjectId == projectId && item.ResourceId == asset.ResourceId && item.ResourceType == AssetType,
            cancellationToken);
        state ??= new ResourceState { ProjectId = projectId, ResourceId = asset.ResourceId, ResourceType = AssetType };
        if (state.CurrentAssetId == Guid.Empty) dbContext.ResourceStates.Add(state);
        state.CurrentAssetId = asset.Id;
        state.LifecycleStatus = "active";
        state.IsStale = false;
        state.UpdatedAtUtc = now;
        dbContext.AssetDependencies.AddRange(
            new AssetDependency { ProjectId = projectId, ConsumerAssetId = asset.Id, SourceAssetId = shotAsset.Id, Role = "dialogue-for-shot", IsRequired = true, CreatedAtUtc = now },
            new AssetDependency { ProjectId = projectId, ConsumerAssetId = asset.Id, SourceAssetId = voice.AssetId, Role = "uses-voice-profile", IsRequired = true, CreatedAtUtc = now });
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToView(asset, shotResourceId);
    }

    public async Task<StoryboardDialogueAudioView?> GetCurrentAsync(Guid projectId, Guid shotResourceId, CancellationToken cancellationToken)
    {
        var asset = await FindCurrentAssetAsync(projectId, shotResourceId, cancellationToken);
        return asset is null ? null : ToView(asset, shotResourceId);
    }

    public async Task<BatchStoryboardMediaResult> GenerateMissingAsync(Guid projectId, Guid productionEpisodeId, CancellationToken cancellationToken)
    {
        var shots = await dbContext.ShotDefinitions.AsNoTracking()
            .Where(item => item.ProjectId == projectId && item.ProductionEpisodeId == productionEpisodeId)
            .OrderBy(item => item.SceneNumber).ThenBy(item => item.ShotNumber)
            .ToListAsync(cancellationToken);
        var generated = 0;
        var skipped = 0;
        var errors = new List<string>();
        foreach (var shot in shots)
        {
            try
            {
                if (await GetCurrentAsync(projectId, shot.ShotResourceId, cancellationToken) is not null) { skipped++; continue; }
                var shotAsset = await dbContext.Assets.AsNoTracking().SingleAsync(item => item.Id == shot.ShotAssetId, cancellationToken);
                var document = JsonSerializer.Deserialize<StoryboardShotDocument>(shotAsset.DocumentJson ?? "{}", StoryboardDefaults.JsonOptions);
                if (document is null || string.IsNullOrWhiteSpace(StoryboardDialogue.From(document.DialogueCharacter, document.Dialogue).Text)) { skipped++; continue; }
                await GenerateAsync(projectId, productionEpisodeId, shot.ShotResourceId, cancellationToken);
                generated++;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                errors.Add($"S{shot.SceneNumber:D2}-{shot.ShotNumber:D2}: {error.Message}");
            }
        }
        return new(generated, skipped, errors.Count, errors);
    }

    private async Task<Asset?> FindCurrentAssetAsync(Guid projectId, Guid shotResourceId, CancellationToken cancellationToken) => await (
        from definition in dbContext.ShotDefinitions.AsNoTracking()
        join dependency in dbContext.AssetDependencies.AsNoTracking() on definition.ShotAssetId equals dependency.SourceAssetId
        join audio in dbContext.Assets.AsNoTracking() on dependency.ConsumerAssetId equals audio.Id
        join state in dbContext.ResourceStates.AsNoTracking() on audio.Id equals state.CurrentAssetId
        where definition.ProjectId == projectId
            && definition.ShotResourceId == shotResourceId
            && dependency.Role == "dialogue-for-shot"
            && audio.Type == AssetType
            && state.ResourceType == AssetType
        select audio).SingleOrDefaultAsync(cancellationToken);

    private async Task<int> NextNumberAsync(Guid projectId, CancellationToken cancellationToken) =>
        (await dbContext.Assets.Where(item => item.ProjectId == projectId).Select(item => (int?)item.Number).MaxAsync(cancellationToken) ?? 0) + 1;

    private async Task<VoiceProfileView?> GetOnlyProjectVoiceAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var profiles = await (
            from state in dbContext.ResourceStates.AsNoTracking()
            join asset in dbContext.Assets.AsNoTracking() on state.CurrentAssetId equals asset.Id
            where state.ProjectId == projectId
                && state.ResourceType == VoiceProfileService.ProfileAssetType
                && state.LifecycleStatus != "retired"
            select asset)
            .ToListAsync(cancellationToken);
        if (profiles.Count != 1) return null;
        using var document = JsonDocument.Parse(profiles[0].DocumentJson ?? "{}");
        if (!document.RootElement.TryGetProperty("characterResourceId", out var characterResourceId)
            || !characterResourceId.TryGetGuid(out var resourceId))
            return null;
        return await voiceProfileService.GetAsync(projectId, resourceId, cancellationToken);
    }

    internal static StoryboardDialogueAudioView ToView(Asset asset, Guid shotResourceId)
    {
        using var metadata = JsonDocument.Parse(asset.GenerationMetadataJson ?? "{}");
        var root = metadata.RootElement;
        return new(
            asset.Id,
            shotResourceId,
            asset.Version,
            $"/api/v2/projects/{asset.ProjectId}/storyboard/dialogue-audio/{asset.Id}/content",
            root.GetProperty("text").GetString() ?? string.Empty,
            root.GetProperty("name").GetString() ?? string.Empty,
            root.GetProperty("durationSeconds").GetDouble(),
            asset.CreatedAtUtc);
    }
}

public static class StoryboardDialogueAudioEndpoints
{
    public static IEndpointRouteBuilder MapStoryboardDialogueAudio(this IEndpointRouteBuilder app)
    {
        const string route = "/api/v2/projects/{projectId:guid}/production-episodes/{productionEpisodeId:guid}/storyboard";
        app.MapPost($"{route}/shots/{{shotResourceId:guid}}/dialogue-audio/generate", async (
            Guid projectId, Guid productionEpisodeId, Guid shotResourceId, IStoryboardDialogueAudioService service, CancellationToken token) =>
            Results.Ok(await service.GenerateAsync(projectId, productionEpisodeId, shotResourceId, token)));
        app.MapPost($"{route}/batch/dialogue-audio", async (
            Guid projectId, Guid productionEpisodeId, IStoryboardDialogueAudioService service, CancellationToken token) =>
            Results.Ok(await service.GenerateMissingAsync(projectId, productionEpisodeId, token)));
        app.MapGet("/api/v2/projects/{projectId:guid}/storyboard/dialogue-audio/{assetId:guid}/content", async (
            Guid projectId, Guid assetId, V2DbContext dbContext, CancellationToken token) =>
        {
            var audio = await dbContext.Assets.AsNoTracking().SingleOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == assetId && item.Type == StoryboardDialogueAudioService.AssetType,
                token);
            return audio?.BlobContent is null
                ? Results.NotFound()
                : Results.File(audio.BlobContent, audio.ContentType ?? "audio/wav", audio.FileName, enableRangeProcessing: true);
        });
        return app;
    }
}