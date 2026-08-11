using AlexDirectorConsole.Api.Models;
using AlexDirectorConsole.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.Api.Tools;

internal static class ImageAssetWriter
{
    public static async Task<Asset> SaveAsync(
        DirectorToolContext context,
        string resourceName,
        GeneratedImage generatedImage,
        CancellationToken cancellationToken)
    {
        var assetId = Guid.NewGuid();
        var canonicalName = $"{resourceName} · AI 图片";
        var subject = GetResourceSubject(canonicalName);
        var versions = (await context.DbContext.Assets
                .Where(asset => asset.ProjectId == context.ProjectId && asset.Type == "media")
                .ToListAsync(cancellationToken))
            .Where(asset => GetResourceSubject(asset.Name)
                .Equals(subject, StringComparison.OrdinalIgnoreCase))
            .OrderBy(asset => asset.Version)
            .ToList();
        var latestVersion = versions.LastOrDefault();
        var version = (latestVersion?.Version ?? 0) + 1;
        var now = DateTimeOffset.UtcNow;
        var imageAsset = new Asset
        {
            Id = assetId,
            ResourceId = latestVersion?.ResourceId ?? assetId,
            Version = version,
            ProjectId = context.ProjectId,
            Type = "media",
            Name = latestVersion?.Name ?? canonicalName,
            BlobKey = $"{context.ProjectId:N}/media/{assetId:N}{generatedImage.Extension}",
            FileName = $"{SanitizeFileName(resourceName)}-v{version}{generatedImage.Extension}",
            ContentType = generatedImage.ContentType,
            SizeBytes = generatedImage.Bytes.LongLength,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        await using var imageStream = new MemoryStream(generatedImage.Bytes, writable: false);
        await context.BlobStorage.SaveAsync(imageAsset.BlobKey, imageStream, cancellationToken);
        try
        {
            context.DbContext.Assets.Add(imageAsset);
            await context.DbContext.SaveChangesAsync(cancellationToken);
            return imageAsset;
        }
        catch
        {
            await context.BlobStorage.DeleteAsync(imageAsset.BlobKey, CancellationToken.None);
            throw;
        }
    }

    private static string GetResourceSubject(string value) =>
        value.Split('·', StringSplitOptions.TrimEntries)[0];

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "未命名" : sanitized.Trim();
    }
}
