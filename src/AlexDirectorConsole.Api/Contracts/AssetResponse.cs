using AlexDirectorConsole.Api.Models;

namespace AlexDirectorConsole.Api.Contracts;

public sealed record AssetResponse(
    Guid Id,
    Guid ResourceId,
    int Number,
    int Version,
    int VersionCount,
    Guid ProjectId,
    string Type,
    string Name,
    string FileName,
    string ContentType,
    string? GenerationMetadataJson,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc,
    string ContentUrl,
    AssetSourceScriptResponse? SourceScript)
{
    public static AssetResponse FromAsset(Asset asset, int versionCount = 1) =>
        FromAssetWithSourceScript(asset, versionCount, null);

    public static AssetResponse FromAssetWithSourceScript(
        Asset asset,
        int versionCount,
        Asset? sourceScript) => new(
        asset.Id,
        asset.ResourceId,
        asset.Number,
        asset.Version,
        versionCount,
        asset.ProjectId,
        asset.Type,
        asset.Name,
        asset.FileName,
        asset.ContentType,
        asset.GenerationMetadataJson,
        asset.SizeBytes,
        asset.CreatedAtUtc,
        $"/api/projects/{asset.ProjectId}/assets/{asset.Id}/content",
        sourceScript is null
            ? null
            : new AssetSourceScriptResponse(
                sourceScript.Id,
                sourceScript.ResourceId,
                sourceScript.Name,
                sourceScript.Version));
}

public sealed record AssetSourceScriptResponse(
    Guid AssetId,
    Guid ResourceId,
    string Name,
    int Version);

public sealed record ShotAssetLinkResponse(
    Guid Id,
    string Role,
    DateTimeOffset CreatedAtUtc,
    AssetResponse Asset);