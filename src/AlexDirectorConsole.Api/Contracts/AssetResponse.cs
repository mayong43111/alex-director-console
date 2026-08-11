using AlexDirectorConsole.Api.Models;

namespace AlexDirectorConsole.Api.Contracts;

public sealed record AssetResponse(
    Guid Id,
    Guid ResourceId,
    int Version,
    int VersionCount,
    Guid ProjectId,
    string Type,
    string Name,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc,
    string ContentUrl)
{
    public static AssetResponse FromAsset(Asset asset, int versionCount = 1) => new(
        asset.Id,
        asset.ResourceId,
        asset.Version,
        versionCount,
        asset.ProjectId,
        asset.Type,
        asset.Name,
        asset.FileName,
        asset.ContentType,
        asset.SizeBytes,
        asset.CreatedAtUtc,
        $"/api/assets/{asset.Id}/content");
}