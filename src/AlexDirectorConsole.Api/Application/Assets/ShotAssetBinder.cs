using AlexDirectorConsole.Api.Data;
using AlexDirectorConsole.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.Api.Application.Assets;

public sealed class ShotAssetBinder(AppDbContext dbContext) : IShotAssetBinder
{
    public async Task<ShotAssetLink> BindAsync(
        Guid projectId,
        Guid shotResourceId,
        Guid assetId,
        string role,
        bool exclusive,
        CancellationToken cancellationToken = default)
    {
        var shotExists = await dbContext.Assets
            .AsNoTracking()
            .AnyAsync(asset =>
                asset.ProjectId == projectId
                && asset.ResourceId == shotResourceId
                && asset.Type == "shot",
                cancellationToken);
        if (!shotExists)
        {
            throw new InvalidOperationException("The shot resource does not belong to the requested project.");
        }

        var assetExists = await dbContext.Assets
            .AsNoTracking()
            .AnyAsync(asset => asset.ProjectId == projectId && asset.Id == assetId, cancellationToken);
        if (!assetExists)
        {
            throw new InvalidOperationException("The linked asset does not belong to the requested project.");
        }

        var existing = await dbContext.ShotAssetLinks.SingleOrDefaultAsync(
            link => link.ProjectId == projectId
                && link.ShotResourceId == shotResourceId
                && link.AssetId == assetId
                && link.Role == role,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        if (exclusive)
        {
            var replacedLinks = await dbContext.ShotAssetLinks
                .Where(link =>
                    link.ProjectId == projectId
                    && link.ShotResourceId == shotResourceId
                    && link.Role == role)
                .ToListAsync(cancellationToken);
            dbContext.ShotAssetLinks.RemoveRange(replacedLinks);
        }

        var link = new ShotAssetLink
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            ShotResourceId = shotResourceId,
            AssetId = assetId,
            Role = role,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.ShotAssetLinks.Add(link);
        await dbContext.SaveChangesAsync(cancellationToken);
        return link;
    }
}