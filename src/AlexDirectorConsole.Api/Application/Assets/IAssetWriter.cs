using AlexDirectorConsole.Api.Models;

namespace AlexDirectorConsole.Api.Application.Assets;

public enum AssetVersionTarget
{
    ExistingResource,
    ExactName,
    CaseInsensitiveName,
    NormalizedImageName,
    ResourceSubject
}

public sealed record AssetWriteRequest(
    Guid ProjectId,
    string Type,
    string CanonicalName,
    string FileNameBase,
    string Extension,
    string ContentType,
    byte[] Content,
    AssetVersionTarget Target,
    Guid? ResourceId = null,
    Guid? AssetId = null,
    string FileNameFallback = "asset",
    string? GenerationMetadataJson = null);

public sealed record AssetCreateRequest(
    Guid ProjectId,
    string Type,
    string Name,
    string FileName,
    string Extension,
    string ContentType,
    long SizeBytes);

public sealed record DeletedAssetResource(
    Guid ResourceId,
    string Name,
    int VersionCount);

public interface IAssetWriter
{
    Task<Asset> CreateAsync(
        AssetCreateRequest request,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<Asset> WriteVersionAsync(
        AssetWriteRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Asset>> WriteVersionsAsync(
        IReadOnlyList<AssetWriteRequest> requests,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid projectId,
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default);

    Task<DeletedAssetResource?> DeleteResourceAsync(
        Guid projectId,
        Guid assetId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeletedAssetResource>> DeleteResourcesAsync(
        Guid projectId,
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default);
}