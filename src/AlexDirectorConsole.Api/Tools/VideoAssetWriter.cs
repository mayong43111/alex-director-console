using AlexDirectorConsole.Api.Models;
using AlexDirectorConsole.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.Api.Tools;

internal static class VideoAssetWriter
{
    public static async Task<Asset> SaveAsync(
        DirectorToolContext context,
        string resourceName,
        GeneratedVideo video,
        CancellationToken cancellationToken)
    {
        var assetId = Guid.NewGuid();
        var canonicalName = $"{resourceName.Trim()} · AI 视频";
        var versions = await context.DbContext.Assets
            .Where(asset => asset.ProjectId == context.ProjectId && asset.Type == "media" && asset.Name == canonicalName)
            .OrderBy(asset => asset.Version)
            .ToListAsync(cancellationToken);
        var latestVersion = versions.LastOrDefault();
        var version = (latestVersion?.Version ?? 0) + 1;
        var now = DateTimeOffset.UtcNow;
        var asset = new Asset
        {
            Id = assetId,
            ResourceId = latestVersion?.ResourceId ?? assetId,
            Version = version,
            ProjectId = context.ProjectId,
            Type = "media",
            Name = canonicalName,
            BlobKey = $"{context.ProjectId:N}/media/{assetId:N}.mp4",
            FileName = $"{SanitizeFileName(resourceName)}-v{version}.mp4",
            ContentType = video.ContentType,
            SizeBytes = video.Bytes.LongLength,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        await using var stream = new MemoryStream(video.Bytes, writable: false);
        await context.BlobStorage.SaveAsync(asset.BlobKey, stream, cancellationToken);
        try
        {
            context.DbContext.Assets.Add(asset);
            await context.DbContext.SaveChangesAsync(cancellationToken);
            return asset;
        }
        catch
        {
            await context.BlobStorage.DeleteAsync(asset.BlobKey, CancellationToken.None);
            throw;
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "video" : sanitized.Trim();
    }
}