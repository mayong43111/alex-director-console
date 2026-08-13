using System.Text.Json;
using AlexDirectorConsole.Api.Application.Assets;
using AlexDirectorConsole.Api.Models;
using AlexDirectorConsole.Api.Services;

namespace AlexDirectorConsole.Api.Tools;

internal sealed record VideoGenerationParameters(
    string Workflow,
    int Width,
    int Height,
    int FrameCount,
    int Fps,
    string FrameFitMode);

internal sealed record VideoGenerationSource(
    Guid AssetId,
    string Role);

internal sealed record VideoGenerationMetadata(
    int SchemaVersion,
    string Operation,
    string Provider,
    string Model,
    string Prompt,
    VideoGenerationParameters Parameters,
    IReadOnlyList<VideoGenerationSource> Sources);

internal static class VideoAssetWriter
{
    public static async Task<Asset> SaveAsync(
        IAssetWriter assetWriter,
        Guid projectId,
        string resourceName,
        GeneratedVideo video,
        CancellationToken cancellationToken,
        VideoGenerationMetadata? generationMetadata = null)
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
                FileNameFallback: "video",
                GenerationMetadataJson: generationMetadata is null
                    ? null
                    : JsonSerializer.Serialize(generationMetadata, JsonSerializerOptions.Web)),
            cancellationToken);
    }
}