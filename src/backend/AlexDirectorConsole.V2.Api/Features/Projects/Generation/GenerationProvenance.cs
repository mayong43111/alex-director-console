using AlexDirectorConsole.V2.Database.Models;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Generation;

public sealed record GenerationAssetReferenceView(
    Guid AssetId,
    Guid ResourceId,
    int Version,
    string Name,
    string Type,
    string Role,
    string? ContentUrl = null);

public sealed record ImageGenerationParametersView(
    string Deployment,
    string Quality,
    string ModelSize,
    string OutputFormat,
    int OutputWidth,
    int OutputHeight,
    string? ProductionMode = null,
    double? DurationSeconds = null,
    IReadOnlyList<string>? Stages = null);

public sealed record ImageGenerationPreviewView(
    string Operation,
    string Prompt,
    ImageGenerationParametersView Parameters,
    IReadOnlyList<GenerationAssetReferenceView> References);

public static class GenerationProvenance
{
    public static GenerationAssetReferenceView Reference(
        Asset asset,
        string role,
        string? contentUrl = null) => new(
            asset.Id,
            asset.ResourceId,
            asset.Version,
            asset.Name,
            asset.Type,
            role,
            contentUrl);
}