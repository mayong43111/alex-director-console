using System.Text.Json;
using AlexDirectorConsole.V2.Api.Features.Projects.Voice;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Assets;

public sealed record AudioMaterialView(
    Guid AssetId,
    Guid ResourceId,
    int Version,
    string Name,
    string Kind,
    string ContentType,
    string ContentUrl,
    string FileName,
    long SizeBytes,
    double DurationSeconds,
    string Source,
    DateTimeOffset UpdatedAtUtc);

internal static class AudioMaterialDefaults
{
    public const string AssetType = "audio-material";
    public const long MaxBytes = 50 * 1024 * 1024;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static readonly HashSet<string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/wav", "audio/x-wav", "audio/mpeg", "audio/mp4", "audio/ogg",
        "audio/flac", "audio/aac", "audio/webm"
    };
}

public static class AudioMaterialEndpoints
{
    public static IEndpointRouteBuilder MapAudioMaterials(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/projects/{projectId:guid}/audio-assets");

        group.MapGet("/", async (
            Guid projectId,
            V2DbContext dbContext,
            CancellationToken cancellationToken) => Results.Ok(
                await ListAsync(projectId, dbContext, cancellationToken)));

        group.MapPost("/", async (
            Guid projectId,
            HttpRequest request,
            V2DbContext dbContext,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "请使用 multipart/form-data 上传音频。" });
            }
            if (!await dbContext.Projects.AnyAsync(item => item.Id == projectId, cancellationToken))
            {
                return Results.NotFound();
            }

            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "请选择音频文件。" });
            }
            if (file.Length > AudioMaterialDefaults.MaxBytes)
            {
                return Results.BadRequest(new { error = "音频文件不能超过 50 MB。" });
            }
            if (!AudioMaterialDefaults.ContentTypes.Contains(file.ContentType))
            {
                return Results.BadRequest(new { error = "仅支持 WAV、MP3、M4A、OGG、FLAC、AAC 或 WebM 音频。" });
            }

            await using var source = file.OpenReadStream();
            await using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();
            var durationSeconds = ReadDuration(file.ContentType, bytes);
            var now = timeProvider.GetUtcNow();
            var resourceId = Guid.NewGuid();
            var name = form["name"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(file.FileName);
            if (name.Length is < 1 or > 100)
            {
                return Results.BadRequest(new { error = "音频名称必须为 1 至 100 个字符。" });
            }

            var asset = new Asset
            {
                ProjectId = projectId,
                ResourceId = resourceId,
                Version = 1,
                Number = (await dbContext.Assets
                    .Where(item => item.ProjectId == projectId)
                    .Select(item => (int?)item.Number)
                    .MaxAsync(cancellationToken) ?? 0) + 1,
                Type = AudioMaterialDefaults.AssetType,
                Name = name,
                BlobKey = $"audio-materials/{projectId:N}/{resourceId:N}/{Path.GetFileName(file.FileName)}",
                BlobContent = bytes,
                FileName = Path.GetFileName(file.FileName),
                ContentType = file.ContentType,
                SizeBytes = bytes.LongLength,
                GenerationMetadataJson = JsonSerializer.Serialize(new
                {
                    source = "upload",
                    durationSeconds
                }, AudioMaterialDefaults.JsonOptions),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            dbContext.Assets.Add(asset);
            dbContext.ResourceStates.Add(new ResourceState
            {
                ProjectId = projectId,
                ResourceId = resourceId,
                ResourceType = AudioMaterialDefaults.AssetType,
                CurrentAssetId = asset.Id,
                LifecycleStatus = "active",
                IsStale = false,
                UpdatedAtUtc = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Created(
                $"/api/v2/projects/{projectId}/audio-assets/{asset.Id}/content",
                ToView(asset));
        });

        group.MapGet("/{assetId:guid}/content", async (
            Guid projectId,
            Guid assetId,
            V2DbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var audio = await dbContext.Assets.AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == assetId
                    && item.ProjectId == projectId
                    && (item.Type == AudioMaterialDefaults.AssetType
                        || item.Type == VoiceProfileService.ReferenceAssetType),
                cancellationToken);
            return audio?.BlobContent is null
                ? Results.NotFound()
                : Results.File(
                    audio.BlobContent,
                    audio.ContentType ?? "application/octet-stream",
                    audio.FileName,
                    enableRangeProcessing: true);
        });
        return app;
    }

    private static async Task<AudioMaterialView[]> ListAsync(
        Guid projectId,
        V2DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from state in dbContext.ResourceStates.AsNoTracking()
            join asset in dbContext.Assets.AsNoTracking() on state.CurrentAssetId equals asset.Id
            where state.ProjectId == projectId
                && state.LifecycleStatus != "retired"
                && (state.ResourceType == AudioMaterialDefaults.AssetType
                    || state.ResourceType == VoiceProfileService.ReferenceAssetType)
                && (asset.Type == AudioMaterialDefaults.AssetType
                    || asset.Type == VoiceProfileService.ReferenceAssetType)
            select asset)
            .ToArrayAsync(cancellationToken);
        return rows
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Select(ToView)
            .ToArray();
    }

    private static AudioMaterialView ToView(Asset asset)
    {
        using var metadata = JsonDocument.Parse(asset.GenerationMetadataJson ?? "{}");
        var root = metadata.RootElement;
        var duration = root.TryGetProperty("durationSeconds", out var durationElement)
            ? durationElement.GetDouble()
            : 0;
        var generated = asset.Type == VoiceProfileService.ReferenceAssetType;
        return new(
            asset.Id,
            asset.ResourceId,
            asset.Version,
            asset.Name,
            generated ? "voice-reference" : "upload",
            asset.ContentType ?? "application/octet-stream",
            $"/api/v2/projects/{asset.ProjectId}/audio-assets/{asset.Id}/content",
            asset.FileName ?? asset.Name,
            asset.SizeBytes,
            duration,
            generated ? "角色参考音" : "上传",
            asset.UpdatedAtUtc);
    }

    private static double ReadDuration(string contentType, byte[] bytes)
    {
        if (!contentType.Equals("audio/wav", StringComparison.OrdinalIgnoreCase)
            && !contentType.Equals("audio/x-wav", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        VoiceWave.Validate(bytes);
        return VoiceWave.ReadDurationSeconds(bytes);
    }
}