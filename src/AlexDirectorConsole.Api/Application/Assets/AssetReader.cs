using AlexDirectorConsole.Api.Data;
using AlexDirectorConsole.Api.Models;
using AlexDirectorConsole.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.Api.Application.Assets;

public sealed class AssetReader(AppDbContext dbContext, IBlobStorage blobStorage) : IAssetReader
{
    public async Task<IReadOnlyList<Asset>> ListAsync(
        Guid projectId,
        string? type = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Assets
            .AsNoTracking()
            .Where(asset => asset.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(type))
        {
            query = query.Where(asset => asset.Type == type);
        }
        return await query.ToListAsync(cancellationToken);
    }

    public Task<Asset?> GetAsync(
        Guid projectId,
        Guid assetId,
        CancellationToken cancellationToken = default) =>
        dbContext.Assets
            .AsNoTracking()
            .SingleOrDefaultAsync(
                asset => asset.ProjectId == projectId && asset.Id == assetId,
                cancellationToken);

    public Task<int> CountVersionsAsync(
        Guid projectId,
        Guid resourceId,
        CancellationToken cancellationToken = default) =>
        dbContext.Assets.CountAsync(
            asset => asset.ProjectId == projectId && asset.ResourceId == resourceId,
            cancellationToken);

    public async Task<Stream?> OpenReadAsync(
        Guid projectId,
        Asset asset,
        CancellationToken cancellationToken = default)
    {
        if (asset.ProjectId != projectId || !await dbContext.Assets.AsNoTracking().AnyAsync(
            item => item.ProjectId == projectId && item.Id == asset.Id && item.BlobKey == asset.BlobKey,
            cancellationToken))
        {
            throw new InvalidOperationException("The asset does not belong to the requested project.");
        }

        return await blobStorage.OpenReadAsync(asset.BlobKey, cancellationToken);
    }
}