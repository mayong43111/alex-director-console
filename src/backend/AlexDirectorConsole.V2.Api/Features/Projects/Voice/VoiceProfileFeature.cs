using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AlexDirectorConsole.V2.Api.Features.Projects.Assets;
using AlexDirectorConsole.V2.Api.Features.Generation;
using AlexDirectorConsole.V2.Api.Features.Projects.Generation;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Voice;

public sealed record SaveVoiceProfileRequest(
    string Name,
    string DesignPrompt,
    string SampleText,
    string? Language,
    int? Seed);

public sealed record VoiceReferenceView(
    Guid AssetId,
    int Version,
    string ContentType,
    string ContentUrl,
    string Model,
    string Device,
    double DurationSeconds,
    DateTimeOffset CreatedAtUtc);

public sealed record VoiceProfileView(
    Guid AssetId,
    Guid ResourceId,
    Guid CharacterResourceId,
    int Version,
    string Name,
    string DesignPrompt,
    string SampleText,
    string Language,
    int Seed,
    string Status,
    DateTimeOffset UpdatedAtUtc,
    VoiceReferenceView? Reference);

internal sealed record VoiceProfileDocument(
    Guid CharacterResourceId,
    string Name,
    string DesignPrompt,
    string SampleText,
    string Language,
    int Seed,
    string Provider,
    string Model);

public sealed record LocalVoiceDesignRequest(
    string Text,
    string DesignPrompt,
    string Language,
    int Seed);

public sealed record GeneratedVoiceReference(
    byte[] Bytes,
    string ContentType,
    string Model,
    string Device,
    int SampleRate,
    double DurationSeconds);

public interface ILocalVoiceDesigner
{
    Task<GeneratedVoiceReference> GenerateAsync(
        LocalVoiceDesignRequest request,
        CancellationToken cancellationToken);
}

public sealed class LocalQwenVoiceDesigner(HttpClient httpClient) : ILocalVoiceDesigner
{
    public async Task<GeneratedVoiceReference> GenerateAsync(
        LocalVoiceDesignRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "v1/voice-design",
            request,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"本地 Qwen3-TTS 返回 {(int)response.StatusCode}：{detail}");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        VoiceWave.Validate(bytes);
        var model = ReadHeader(response, "X-TTS-Model") ?? "Qwen3-TTS-12Hz-1.7B-VoiceDesign";
        var device = ReadHeader(response, "X-TTS-Device") ?? "unknown";
        var sampleRate = int.TryParse(ReadHeader(response, "X-Audio-Sample-Rate"), out var parsedRate)
            ? parsedRate
            : VoiceWave.ReadSampleRate(bytes);
        var duration = double.TryParse(
            ReadHeader(response, "X-Audio-Duration-Seconds"),
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsedDuration)
            ? parsedDuration
            : VoiceWave.ReadDurationSeconds(bytes);
        return new(bytes, "audio/wav", model, device, sampleRate, duration);
    }

    private static string? ReadHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}

public interface IVoiceProfileService
{
    Task<VoiceProfileView?> GetAsync(Guid projectId, Guid characterResourceId, CancellationToken cancellationToken);
    Task<VoiceProfileView> SaveAsync(Guid projectId, Guid characterResourceId, SaveVoiceProfileRequest request, CancellationToken cancellationToken);
    Task<VoiceProfileView> GenerateAsync(Guid projectId, Guid characterResourceId, CancellationToken cancellationToken);
}

public sealed class VoiceProfileService(
    V2DbContext dbContext,
    ILocalVoiceDesigner designer,
    TimeProvider timeProvider) : IVoiceProfileService
{
    public const string ProfileAssetType = "voice-profile";
    public const string ReferenceAssetType = "voice-reference";
    private const string Model = "Qwen3-TTS-12Hz-1.7B-VoiceDesign";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<VoiceProfileView?> GetAsync(
        Guid projectId,
        Guid characterResourceId,
        CancellationToken cancellationToken)
    {
        var current = await FindCurrentProfileAsync(projectId, characterResourceId, cancellationToken);
        return current is null
            ? null
            : await ToViewAsync(current.Value.Asset, current.Value.State, cancellationToken);
    }

    public async Task<VoiceProfileView> SaveAsync(
        Guid projectId,
        Guid characterResourceId,
        SaveVoiceProfileRequest request,
        CancellationToken cancellationToken)
    {
        var character = await FindCharacterAsync(projectId, characterResourceId, cancellationToken)
            ?? throw new KeyNotFoundException("角色资产不存在或已退休。");
        var name = request.Name.Trim();
        var prompt = request.DesignPrompt.Trim();
        var sampleText = request.SampleText.Trim();
        var language = string.IsNullOrWhiteSpace(request.Language) ? "Chinese" : request.Language.Trim();
        if (name.Length is < 1 or > 100) throw new ArgumentException("音色名称必须为 1 至 100 个字符。");
        if (prompt.Length is < 10 or > 2000) throw new ArgumentException("音色设计描述必须为 10 至 2000 个字符。");
        if (sampleText.Length is < 1 or > 1000) throw new ArgumentException("试音文本必须为 1 至 1000 个字符。");
        if (language.Length > 40) throw new ArgumentException("语言名称不能超过 40 个字符。");

        var previous = await FindCurrentProfileAsync(projectId, characterResourceId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var document = new VoiceProfileDocument(
            characterResourceId,
            name,
            prompt,
            sampleText,
            language,
            request.Seed ?? Random.Shared.Next(),
            "local-qwen3-tts",
            Model);
        var documentJson = JsonSerializer.Serialize(document, JsonOptions);
        var asset = new Asset
        {
            ProjectId = projectId,
            ResourceId = previous?.Asset.ResourceId ?? Guid.NewGuid(),
            Version = (previous?.Asset.Version ?? 0) + 1,
            Number = previous?.Asset.Number ?? await NextNumberAsync(projectId, cancellationToken),
            Type = ProfileAssetType,
            Name = name,
            DocumentJson = documentJson,
            ContentType = "application/json",
            SizeBytes = Encoding.UTF8.GetByteCount(documentJson),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.Assets.Add(asset);
        var state = previous?.State ?? new ResourceState
        {
            ProjectId = projectId,
            ResourceId = asset.ResourceId,
            ResourceType = ProfileAssetType
        };
        if (previous is null) dbContext.ResourceStates.Add(state);
        state.CurrentAssetId = asset.Id;
        state.LifecycleStatus = "draft";
        state.IsStale = false;
        state.StaleReason = null;
        state.StaleSinceUtc = null;
        state.UpdatedAtUtc = now;
        dbContext.AssetDependencies.Add(new AssetDependency
        {
            ProjectId = projectId,
            ConsumerAssetId = asset.Id,
            SourceAssetId = character.Id,
            Role = "voice-for-character",
            IsRequired = true,
            CreatedAtUtc = now
        });
        if (previous is not null)
        {
            await AssetStalenessPropagation.MarkRequiredDependentsStaleAsync(
                dbContext,
                previous.Value.Asset,
                asset,
                now,
                cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ToViewAsync(asset, state, cancellationToken);
    }

    public async Task<VoiceProfileView> GenerateAsync(
        Guid projectId,
        Guid characterResourceId,
        CancellationToken cancellationToken)
    {
        var current = await FindCurrentProfileAsync(projectId, characterResourceId, cancellationToken)
            ?? throw new InvalidOperationException("请先为角色保存音色设计。");
        var character = await FindCharacterAsync(projectId, characterResourceId, cancellationToken)
            ?? throw new KeyNotFoundException("角色资产不存在或已退休。");
        var document = ReadDocument(current.Asset);
        var generated = await designer.GenerateAsync(
            new(document.SampleText, document.DesignPrompt, document.Language, document.Seed),
            cancellationToken);
        VoiceWave.Validate(generated.Bytes);

        var previousReference = await FindCurrentReferenceAsync(
            projectId,
            current.Asset.ResourceId,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        var version = (previousReference?.Version ?? 0) + 1;
        var referenceResourceId = previousReference?.ResourceId ?? Guid.NewGuid();
        var reference = new Asset
        {
            ProjectId = projectId,
            ResourceId = referenceResourceId,
            Version = version,
            Number = previousReference?.Number ?? await NextNumberAsync(projectId, cancellationToken),
            Type = ReferenceAssetType,
            Name = $"{document.Name}参考音频",
            BlobKey = $"voice-references/{projectId:N}/{characterResourceId:N}/v{version}.wav",
            BlobContent = generated.Bytes,
            FileName = $"{document.Name}-v{version}.wav",
            ContentType = "audio/wav",
            SizeBytes = generated.Bytes.LongLength,
            GenerationMetadataJson = JsonSerializer.Serialize(new
            {
                operation = "qwen3-tts-voice-design",
                profileAssetId = current.Asset.Id,
                characterAssetId = character.Id,
                characterResourceId,
                document.DesignPrompt,
                document.SampleText,
                document.Language,
                document.Seed,
                generated.Model,
                generated.Device,
                generated.SampleRate,
                generated.DurationSeconds
            }, JsonOptions),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.Assets.Add(reference);
        var referenceState = await dbContext.ResourceStates.SingleOrDefaultAsync(
            item => item.ProjectId == projectId
                && item.ResourceId == referenceResourceId
                && item.ResourceType == ReferenceAssetType,
            cancellationToken);
        referenceState ??= new ResourceState
        {
            ProjectId = projectId,
            ResourceId = referenceResourceId,
            ResourceType = ReferenceAssetType
        };
        if (referenceState.CurrentAssetId == Guid.Empty) dbContext.ResourceStates.Add(referenceState);
        referenceState.CurrentAssetId = reference.Id;
        referenceState.LifecycleStatus = "active";
        referenceState.IsStale = false;
        referenceState.UpdatedAtUtc = now;
        dbContext.AssetDependencies.AddRange(
            new AssetDependency
            {
                ProjectId = projectId,
                ConsumerAssetId = reference.Id,
                SourceAssetId = current.Asset.Id,
                Role = "uses-voice-profile",
                IsRequired = true,
                CreatedAtUtc = now
            },
            new AssetDependency
            {
                ProjectId = projectId,
                ConsumerAssetId = reference.Id,
                SourceAssetId = character.Id,
                Role = "voices-character",
                IsRequired = true,
                CreatedAtUtc = now
            });
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ToViewAsync(current.Asset, current.State, cancellationToken);
    }

    private async Task<VoiceProfileView> ToViewAsync(
        Asset asset,
        ResourceState state,
        CancellationToken cancellationToken)
    {
        var document = ReadDocument(asset);
        var reference = await FindCurrentReferenceAsync(asset.ProjectId, asset.ResourceId, cancellationToken);
        return new(
            asset.Id,
            asset.ResourceId,
            document.CharacterResourceId,
            asset.Version,
            document.Name,
            document.DesignPrompt,
            document.SampleText,
            document.Language,
            document.Seed,
            state.LifecycleStatus,
            asset.UpdatedAtUtc,
            reference is null ? null : ToReferenceView(reference));
    }

    private async Task<(Asset Asset, ResourceState State)?> FindCurrentProfileAsync(
        Guid projectId,
        Guid characterResourceId,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from state in dbContext.ResourceStates
            join asset in dbContext.Assets on state.CurrentAssetId equals asset.Id
            where state.ProjectId == projectId
                && state.ResourceType == ProfileAssetType
                && asset.Type == ProfileAssetType
            select new { Asset = asset, State = state })
            .ToListAsync(cancellationToken);
        var match = rows.SingleOrDefault(item =>
            ReadDocument(item.Asset).CharacterResourceId == characterResourceId);
        return match is null ? null : (match.Asset, match.State);
    }

    private async Task<Asset?> FindCharacterAsync(
        Guid projectId,
        Guid characterResourceId,
        CancellationToken cancellationToken)
    {
        var asset = await (
            from state in dbContext.ResourceStates.AsNoTracking()
            join current in dbContext.Assets.AsNoTracking() on state.CurrentAssetId equals current.Id
            where state.ProjectId == projectId
                && state.ResourceId == characterResourceId
                && state.ResourceType == VisualAssetDefaults.AssetType
                && state.LifecycleStatus != "retired"
            select current)
            .SingleOrDefaultAsync(cancellationToken);
        return asset is not null && VisualAssetMapper.ReadDocument(asset).Kind == "character"
            ? asset
            : null;
    }

    private Task<Asset?> FindCurrentReferenceAsync(
        Guid projectId,
        Guid profileResourceId,
        CancellationToken cancellationToken) => (
        from dependency in dbContext.AssetDependencies.AsNoTracking()
        join profile in dbContext.Assets.AsNoTracking() on dependency.SourceAssetId equals profile.Id
        join state in dbContext.ResourceStates.AsNoTracking() on dependency.ConsumerAssetId equals state.CurrentAssetId
        join reference in dbContext.Assets.AsNoTracking() on state.CurrentAssetId equals reference.Id
        where dependency.ProjectId == projectId
            && dependency.Role == "uses-voice-profile"
            && profile.ResourceId == profileResourceId
            && state.ResourceType == ReferenceAssetType
            && reference.Type == ReferenceAssetType
        select reference).SingleOrDefaultAsync(cancellationToken);

    private async Task<int> NextNumberAsync(Guid projectId, CancellationToken cancellationToken) =>
        (await dbContext.Assets
            .Where(item => item.ProjectId == projectId)
            .Select(item => (int?)item.Number)
            .MaxAsync(cancellationToken) ?? 0) + 1;

    private static VoiceProfileDocument ReadDocument(Asset asset) =>
        JsonSerializer.Deserialize<VoiceProfileDocument>(asset.DocumentJson ?? "{}", JsonOptions)
        ?? throw new InvalidOperationException("音色资产内容无效。");

    private static VoiceReferenceView ToReferenceView(Asset asset)
    {
        using var metadata = JsonDocument.Parse(asset.GenerationMetadataJson ?? "{}");
        var root = metadata.RootElement;
        return new(
            asset.Id,
            asset.Version,
            asset.ContentType ?? "audio/wav",
            $"/api/v2/projects/{asset.ProjectId}/visual-assets/voice-profiles/references/{asset.Id}/content",
            root.TryGetProperty("model", out var model) ? model.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("device", out var device) ? device.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("durationSeconds", out var duration) ? duration.GetDouble() : 0,
            asset.CreatedAtUtc);
    }
}

internal static class VoiceWave
{
    public static void Validate(byte[] bytes)
    {
        if (bytes.Length < 44
            || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidOperationException("本地 TTS 返回的不是有效 WAV 文件。");
        }
    }

    public static int ReadSampleRate(byte[] bytes) => BitConverter.ToInt32(bytes, 24);

    public static double ReadDurationSeconds(byte[] bytes)
    {
        var byteRate = BitConverter.ToInt32(bytes, 28);
        var dataSize = BitConverter.ToInt32(bytes, 40);
        return byteRate > 0 ? (double)dataSize / byteRate : 0;
    }
}

public static class VoiceProfileEndpoints
{
    public static RouteGroupBuilder MapVoiceProfileEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{characterResourceId:guid}/voice-profile", async (
            Guid projectId,
            Guid characterResourceId,
            IVoiceProfileService service,
            CancellationToken cancellationToken) =>
        {
            var profile = await service.GetAsync(projectId, characterResourceId, cancellationToken);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        });
        group.MapPut("/{characterResourceId:guid}/voice-profile", async (
            Guid projectId,
            Guid characterResourceId,
            SaveVoiceProfileRequest request,
            IVoiceProfileService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.SaveAsync(
                    projectId,
                    characterResourceId,
                    request,
                    cancellationToken));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
        });
        group.MapPost("/{characterResourceId:guid}/voice-profile/generate", async (
            Guid projectId,
            Guid characterResourceId,
            IGenerationTaskScheduler scheduler,
            CancellationToken cancellationToken) => Results.Accepted(value: await scheduler.EnqueueAsync(
                GenerationTaskTypes.VoiceProfile,
                "生成角色音色",
                new(projectId, ResourceId: characterResourceId),
                cancellationToken)));
        group.MapGet("/voice-profiles/references/{assetId:guid}/content", async (
            Guid projectId,
            Guid assetId,
            V2DbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var audio = await dbContext.Assets.AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == assetId
                    && item.ProjectId == projectId
                    && item.Type == VoiceProfileService.ReferenceAssetType,
                cancellationToken);
            return audio?.BlobContent is null
                ? Results.NotFound()
                : Results.File(
                    audio.BlobContent,
                    audio.ContentType ?? "audio/wav",
                    audio.FileName,
                    enableRangeProcessing: true);
        });
        return group;
    }
}