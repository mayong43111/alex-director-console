using AlexDirectorConsole.Api.Models;

namespace AlexDirectorConsole.Api.Application.Assets;

public interface IAssetReader
{
    Task<IReadOnlyList<Asset>> ListAsync(
        Guid projectId,
        string? type = null,
        CancellationToken cancellationToken = default);

    Task<Asset?> GetAsync(
        Guid projectId,
        Guid assetId,
        CancellationToken cancellationToken = default);

    Task<int> CountVersionsAsync(
        Guid projectId,
        Guid resourceId,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(
        Guid projectId,
        Asset asset,
        CancellationToken cancellationToken = default);
}