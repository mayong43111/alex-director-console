using System.Text.Json;
using AlexDirectorConsole.Api.Application.Assets;
using AlexDirectorConsole.Api.Models;
using AlexDirectorConsole.Api.Services;

namespace AlexDirectorConsole.Api.Tools;

internal sealed record ImageGenerationParameters(
    string Size,
    string Quality,
    int Count,
    string OutputFormat,
    string? ApiVersion);

internal sealed record ImageGenerationSource(
    Guid AssetId,
    string Name,
    int Version,
    string? Description);

internal sealed record ImageGenerationMetadata(
    int SchemaVersion,
    string Operation,
    string Provider,
    string Model,
    string? Prompt,
    string? RevisedPrompt,
    ImageGenerationParameters Parameters,
    IReadOnlyList<ImageGenerationSource> Sources);

internal static class ImageAssetWriter
{
    public static async Task<Asset> SaveAsync(
        IAssetWriter assetWriter,
        Guid projectId,
        string resourceName,
        GeneratedImage generatedImage,
        ImageGenerationMetadata generationMetadata,
        CancellationToken cancellationToken)
    {
        var resourceKey = GetResourceKey(resourceName);
        var canonicalName = $"{resourceKey} · AI 图片";
        return await assetWriter.WriteVersionAsync(
            new AssetWriteRequest(
            projectId,
                "media",
                canonicalName,
                resourceName,
                generatedImage.Extension,
                generatedImage.ContentType,
                generatedImage.Bytes,
                AssetVersionTarget.NormalizedImageName,
                FileNameFallback: "未命名",
                GenerationMetadataJson: JsonSerializer.Serialize(
                    generationMetadata,
                    JsonSerializerOptions.Web)),
            cancellationToken);
    }

    private static string GetResourceKey(string value)
    {
        var segments = value
            .Split('·', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        while (segments.Count > 0
            && segments[^1].Equals("AI 图片", StringComparison.OrdinalIgnoreCase))
        {
            segments.RemoveAt(segments.Count - 1);
        }
        return string.Join(" · ", segments);
    }
}
