using AlexDirectorConsole.Api.Application.Assets;
using AlexDirectorConsole.Api.Models;
using AlexDirectorConsole.Api.Services;

namespace AlexDirectorConsole.Api.Tools;

internal static class VideoAssetWriter
{
    public static async Task<Asset> SaveAsync(
        IAssetWriter assetWriter,
        Guid projectId,
        string resourceName,
        GeneratedVideo video,
        CancellationToken cancellationToken)
    {
        var canonicalName = $"{resourceName.Trim()} · AI 视频";
        return await assetWriter.WriteVersionAsync(
            new AssetWriteRequest(
            projectId,
                "media",
                canonicalName,
                resourceName,
                ".mp4",
                video.ContentType,
                video.Bytes,
                AssetVersionTarget.ExactName,
                FileNameFallback: "video"),
            cancellationToken);
    }
}