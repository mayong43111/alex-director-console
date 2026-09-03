using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

public sealed record GptSoVitsDialogueRequest(
    Guid VoicePackageId,
    string Text,
    string GptWeightsPath,
    string SoVitsWeightsPath,
    byte[] ReferenceAudio,
    string ReferenceAudioFileName,
    string ReferenceText,
    string ReferenceLanguage,
    string TargetLanguage,
    double Speed);

public sealed record GeneratedDialogueAudio(byte[] Bytes, int SampleRate, double DurationSeconds);

public sealed record VoicePackageDialogueRequest(
    Guid VoicePackageId,
    string Engine,
    string Text,
    string BaseModelVersion,
    string GptWeightsPath,
    string SoVitsWeightsPath,
    byte[] ReferenceAudio,
    string ReferenceAudioFileName,
    string ReferenceText,
    string ReferenceLanguage,
    string TargetLanguage,
    double Speed);

public interface IGptSoVitsDialogueClient
{
    Task<GeneratedDialogueAudio> GenerateAsync(
        GptSoVitsDialogueRequest request,
        CancellationToken cancellationToken);
}

public sealed record CosyVoiceDialogueRequest(
    Guid VoicePackageId,
    string Text,
    string Model,
    byte[] ReferenceAudio,
    string ReferenceAudioFileName,
    string ReferenceText);

public interface ICosyVoiceDialogueClient
{
    Task<GeneratedDialogueAudio> GenerateAsync(
        CosyVoiceDialogueRequest request,
        CancellationToken cancellationToken);
}

public sealed class CosyVoiceDialogueClient(HttpClient httpClient) : ICosyVoiceDialogueClient
{
    private const int SampleRate = 22050;

    public async Task<GeneratedDialogueAudio> GenerateAsync(
        CosyVoiceDialogueRequest request,
        CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(request.Text), "tts_text");
        content.Add(new StringContent(request.ReferenceText), "prompt_text");
        var promptAudio = new ByteArrayContent(request.ReferenceAudio);
        promptAudio.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(promptAudio, "prompt_wav", request.ReferenceAudioFileName);
        using var message = new HttpRequestMessage(HttpMethod.Get, "inference_zero_shot")
        {
            Content = content
        };
        using var response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"CosyVoice 生成对白失败（{(int)response.StatusCode}）：{detail}");
        }

        var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var wavBytes = responseBytes.AsSpan().StartsWith("RIFF"u8)
            ? responseBytes
            : VoiceWave.FromPcm16Mono(responseBytes, SampleRate);
        VoiceWave.Validate(wavBytes);
        return new(
            wavBytes,
            VoiceWave.ReadSampleRate(wavBytes),
            VoiceWave.ReadDurationSeconds(wavBytes));
    }
}

public interface IVoicePackageDialogueGenerator
{
    Task<GeneratedDialogueAudio> GenerateAsync(
        VoicePackageDialogueRequest request,
        CancellationToken cancellationToken);
}

public sealed class VoicePackageDialogueGenerator(
    IGptSoVitsDialogueClient gptSoVitsClient,
    ICosyVoiceDialogueClient cosyVoiceClient) : IVoicePackageDialogueGenerator
{
    public Task<GeneratedDialogueAudio> GenerateAsync(
        VoicePackageDialogueRequest request,
        CancellationToken cancellationToken) => request.Engine.ToLowerInvariant() switch
    {
        "gpt-sovits" => gptSoVitsClient.GenerateAsync(
            new(
                request.VoicePackageId,
                request.Text,
                request.GptWeightsPath,
                request.SoVitsWeightsPath,
                request.ReferenceAudio,
                request.ReferenceAudioFileName,
                request.ReferenceText,
                request.ReferenceLanguage,
                request.TargetLanguage,
                request.Speed),
            cancellationToken),
        "cosyvoice" => cosyVoiceClient.GenerateAsync(
            new(
                request.VoicePackageId,
                request.Text,
                request.BaseModelVersion,
                request.ReferenceAudio,
                request.ReferenceAudioFileName,
                request.ReferenceText),
            cancellationToken),
        _ => throw new InvalidOperationException($"语音包使用了不支持的 TTS 引擎：{request.Engine}。")
    };
}

public sealed class GptSoVitsDialogueClient(
    HttpClient httpClient,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IHostEnvironment hostEnvironment) : IGptSoVitsDialogueClient
{
    private static readonly SemaphoreSlim ModelSwitchLock = new(1, 1);

    public async Task<GeneratedDialogueAudio> GenerateAsync(
        GptSoVitsDialogueRequest request,
        CancellationToken cancellationToken)
    {
        await ModelSwitchLock.WaitAsync(cancellationToken);
        try
        {
            var referencePath = await MaterializeReferenceAsync(request, cancellationToken);
            await EnsureSuccessAsync(
                await httpClient.GetAsync(
                    $"set_gpt_weights?weights_path={Uri.EscapeDataString(request.GptWeightsPath)}",
                    cancellationToken),
                "加载 GPT 权重",
                cancellationToken);
            await EnsureSuccessAsync(
                await httpClient.GetAsync(
                    $"set_sovits_weights?weights_path={Uri.EscapeDataString(request.SoVitsWeightsPath)}",
                    cancellationToken),
                "加载 SoVITS 权重",
                cancellationToken);
            using var response = await httpClient.PostAsJsonAsync(
                "tts",
                new
                {
                    text = request.Text,
                    text_lang = NormalizeLanguage(request.TargetLanguage),
                    ref_audio_path = referencePath,
                    prompt_text = request.ReferenceText,
                    prompt_lang = NormalizeLanguage(request.ReferenceLanguage),
                    speed_factor = request.Speed,
                    media_type = "wav",
                    streaming_mode = false
                },
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"GPT-SoVITS 生成对白失败（{(int)response.StatusCode}）：{detail}");
            }
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            VoiceWave.Validate(bytes);
            return new(bytes, VoiceWave.ReadSampleRate(bytes), VoiceWave.ReadDurationSeconds(bytes));
        }
        finally
        {
            ModelSwitchLock.Release();
        }
    }

    private static string NormalizeLanguage(string language)
    {
        var normalized = language.Trim().ToLowerInvariant();
        return normalized.StartsWith("zh-", StringComparison.Ordinal) ? "zh" : normalized;
    }

    private async Task<string> MaterializeReferenceAsync(
        GptSoVitsDialogueRequest request,
        CancellationToken cancellationToken)
    {
        var uploadBaseUrl = configuration["GptSoVits:ReferenceUploadBaseUrl"];
        if (!string.IsNullOrWhiteSpace(uploadBaseUrl))
        {
            var referenceFileName = $"{request.VoicePackageId:N}.wav";
            using var audio = new ByteArrayContent(request.ReferenceAudio);
            audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            using var response = await httpClientFactory.CreateClient("GptSoVitsReferenceUpload").PutAsync(
                $"v1/reference-audio/{referenceFileName}",
                audio,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"GPT-SoVITS 上传参考音失败（{(int)response.StatusCode}）：{detail}");
            }
            var result = await response.Content.ReadFromJsonAsync<ReferenceAudioUploadResult>(
                cancellationToken: cancellationToken);
            return result?.RuntimePath
                ?? throw new InvalidOperationException("GPT-SoVITS 参考音上传响应缺少 runtimePath。");
        }
        var sharedDirectory = configuration["GptSoVits:SharedVoiceDirectory"];
        if (string.IsNullOrWhiteSpace(sharedDirectory))
            sharedDirectory = Path.Combine(hostEnvironment.ContentRootPath, "App_Data", "GptSoVitsVoices");
        sharedDirectory = Path.GetFullPath(sharedDirectory);
        Directory.CreateDirectory(sharedDirectory);
        var fileName = $"{request.VoicePackageId:N}.wav";
        var storagePath = Path.Combine(sharedDirectory, fileName);
        await File.WriteAllBytesAsync(storagePath, request.ReferenceAudio, cancellationToken);
        var runtimeDirectory = configuration["GptSoVits:RuntimeVoiceDirectory"];
        return string.IsNullOrWhiteSpace(runtimeDirectory)
            ? storagePath
            : Path.Combine(runtimeDirectory, fileName);
    }

            private sealed record ReferenceAudioUploadResult(string RuntimePath);

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            if (response.IsSuccessStatusCode) return;
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"GPT-SoVITS {operation}失败（{(int)response.StatusCode}）：{detail}");
        }
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
    IVoicePackageDialogueGenerator dialogueGenerator,
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
        if (voice.VoicePackageId is null)
            throw new InvalidOperationException($"角色“{character.Name}”仍使用旧音色设计，请绑定全局语音包。");
        var voicePackage = await dbContext.VoicePackages.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == voice.VoicePackageId,
            cancellationToken)
            ?? throw new InvalidOperationException($"角色“{character.Name}”绑定的语音包版本不存在。");

        var generated = await dialogueGenerator.GenerateAsync(
            new(
                voicePackage.Id,
            voicePackage.Engine,
                dialogue.Text,
            voicePackage.BaseModelVersion,
                voicePackage.GptWeightsPath,
                voicePackage.SoVitsWeightsPath,
                voicePackage.ReferenceAudioContent,
                voicePackage.ReferenceAudioFileName,
                voicePackage.ReferenceText,
                voicePackage.Language,
                voice.Language,
                voicePackage.DefaultSpeed),
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
                operation = $"{voicePackage.Engine}-dialogue",
                voicePackage.Engine,
                character = dialogue.Character,
                text = dialogue.Text,
                shotAssetId = shotAsset.Id,
                voiceProfileAssetId = voice.AssetId,
                voicePackageId = voicePackage.Id,
                voice.Name,
                voice.Language,
                voicePackage.BaseModelVersion,
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