using AlexDirectorConsole.Api.Data;
using AlexDirectorConsole.Api.Models;
using AlexDirectorConsole.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.Api.Application.Assets;

public sealed class AssetWriter(AppDbContext dbContext, IBlobStorage blobStorage) : IAssetWriter
{
    private const int MaxWriteAttempts = 3;

    public async Task<Asset> CreateAsync(
        AssetCreateRequest request,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var assetId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var asset = new Asset
        {
            Id = assetId,
            ResourceId = assetId,
            Version = 1,
            ProjectId = request.ProjectId,
            Type = request.Type,
            Name = request.Name,
            BlobKey = $"{request.ProjectId:N}/{request.Type}/{assetId:N}{request.Extension}",
            FileName = request.FileName,
            ContentType = request.ContentType,
            SizeBytes = request.SizeBytes,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        try
        {
            await blobStorage.SaveAsync(asset.BlobKey, content, cancellationToken);
            dbContext.Assets.Add(asset);
            await dbContext.SaveChangesAsync(cancellationToken);
            return asset;
        }
        catch
        {
            if (dbContext.Entry(asset).State != EntityState.Detached)
            {
                dbContext.Entry(asset).State = EntityState.Detached;
            }
            await blobStorage.DeleteAsync(asset.BlobKey, CancellationToken.None);
            throw;
        }
    }

    public async Task<Asset> WriteVersionAsync(
        AssetWriteRequest request,
        CancellationToken cancellationToken = default) =>
        (await WriteVersionsAsync([request], cancellationToken))[0];

    public async Task<IReadOnlyList<Asset>> WriteVersionsAsync(
        IReadOnlyList<AssetWriteRequest> requests,
        CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0)
        {
            return [];
        }
        foreach (var request in requests)
        {
            Validate(request);
        }

        for (var attempt = 1; attempt <= MaxWriteAttempts; attempt++)
        {
            var assets = new Asset[requests.Count];
            var newAssets = new List<Asset>(requests.Count);
            for (var index = 0; index < requests.Count; index++)
            {
                var request = requests[index];
                var existingAsset = await FindExistingAssetAsync(request, cancellationToken);
                if (existingAsset is not null)
                {
                    assets[index] = existingAsset;
                    continue;
                }

                var latestVersion = await FindLatestVersionAsync(request, cancellationToken);
                var asset = CreateAsset(request, latestVersion);
                assets[index] = asset;
                newAssets.Add(asset);
            }

            if (newAssets.Count == 0)
            {
                return assets;
            }

            var attemptedAssets = new List<Asset>(newAssets.Count);
            try
            {
                foreach (var asset in newAssets)
                {
                    attemptedAssets.Add(asset);
                    var request = requests[Array.IndexOf(assets, asset)];
                    await using var stream = new MemoryStream(request.Content, writable: false);
                    await blobStorage.SaveAsync(asset.BlobKey, stream, cancellationToken);
                }
                dbContext.Assets.AddRange(newAssets);
                await dbContext.SaveChangesAsync(cancellationToken);
                return assets;
            }
            catch (DbUpdateException) when (attempt < MaxWriteAttempts)
            {
                Detach(newAssets);
                await DeleteBlobsAsync(attemptedAssets);
            }
            catch
            {
                Detach(newAssets);
                await DeleteBlobsAsync(attemptedAssets);
                throw;
            }
        }

        throw new InvalidOperationException("Asset version write exhausted all retry attempts.");
    }

    public async Task DeleteAsync(
        Guid projectId,
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default)
    {
        if (assetIds.Count == 0)
        {
            return;
        }

        var requestedAssetIds = assetIds.Distinct().ToArray();
        var assets = await dbContext.Assets
            .Where(asset => asset.ProjectId == projectId && requestedAssetIds.Contains(asset.Id))
            .ToListAsync(cancellationToken);
        if (assets.Count != requestedAssetIds.Length)
        {
            throw new InvalidOperationException(
                "At least one asset does not belong to the requested project; no assets were deleted.");
        }
        dbContext.Assets.RemoveRange(assets);
        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var asset in assets)
        {
            await blobStorage.DeleteAsync(asset.BlobKey, cancellationToken);
        }
    }

    public async Task<DeletedAssetResource?> DeleteResourceAsync(
        Guid projectId,
        Guid assetId,
        CancellationToken cancellationToken = default) =>
        (await DeleteResourcesAsync(projectId, [assetId], cancellationToken)).SingleOrDefault();

    public async Task<IReadOnlyList<DeletedAssetResource>> DeleteResourcesAsync(
        Guid projectId,
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default)
    {
        if (assetIds.Count == 0)
        {
            return [];
        }

        var requestedAssetIds = assetIds.Distinct().ToArray();
        var selectedAssets = await dbContext.Assets
            .AsNoTracking()
            .Where(asset => asset.ProjectId == projectId && requestedAssetIds.Contains(asset.Id))
            .ToListAsync(cancellationToken);
        if (selectedAssets.Count != requestedAssetIds.Length)
        {
            return [];
        }

        var resourceIds = selectedAssets.Select(asset => asset.ResourceId).Distinct().ToArray();
        var versions = await dbContext.Assets
            .Where(asset => asset.ProjectId == projectId && resourceIds.Contains(asset.ResourceId))
            .ToListAsync(cancellationToken);
        var versionIds = versions.Select(asset => asset.Id).ToArray();
        var links = await dbContext.ShotAssetLinks
            .Where(link =>
                link.ProjectId == projectId
                && (resourceIds.Contains(link.ShotResourceId) || versionIds.Contains(link.AssetId)))
            .ToListAsync(cancellationToken);
        var shotDefinitions = await dbContext.ShotDefinitions
            .Where(shot => shot.ProjectId == projectId
                && (resourceIds.Contains(shot.ShotResourceId)
                    || resourceIds.Contains(shot.ScriptResourceId)))
            .ToListAsync(cancellationToken);

        dbContext.ShotAssetLinks.RemoveRange(links);
        dbContext.ShotDefinitions.RemoveRange(shotDefinitions);
        dbContext.Assets.RemoveRange(versions);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var version in versions)
        {
            await blobStorage.DeleteAsync(version.BlobKey, cancellationToken);
        }

        return resourceIds.Select(resourceId =>
        {
            var selected = selectedAssets.First(asset => asset.ResourceId == resourceId);
            return new DeletedAssetResource(
                resourceId,
                selected.Name,
                versions.Count(asset => asset.ResourceId == resourceId));
        }).ToArray();
    }

    public async Task<IReadOnlyList<PurgedAssetVersions>> PurgeOlderVersionsAsync(
        Guid projectId,
        IReadOnlyCollection<Guid> assetIds,
        string requiredType,
        CancellationToken cancellationToken = default)
    {
        if (assetIds.Count == 0)
        {
            return [];
        }

        var requestedAssetIds = assetIds.Distinct().ToArray();
        var selectedAssets = await dbContext.Assets
            .AsNoTracking()
            .Where(asset => asset.ProjectId == projectId && requestedAssetIds.Contains(asset.Id))
            .ToListAsync(cancellationToken);
        if (selectedAssets.Count != requestedAssetIds.Length
            || selectedAssets.Any(asset => !asset.Type.Equals(requiredType, StringComparison.OrdinalIgnoreCase)))
        {
            return [];
        }

        var resourceIds = selectedAssets.Select(asset => asset.ResourceId).Distinct().ToArray();
        var versions = await dbContext.Assets
            .Where(asset => asset.ProjectId == projectId && resourceIds.Contains(asset.ResourceId))
            .ToListAsync(cancellationToken);
        var latestVersions = versions
            .GroupBy(asset => asset.ResourceId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(asset => asset.Version).First());
        var obsoleteVersions = versions
            .Where(asset => latestVersions[asset.ResourceId].Id != asset.Id)
            .ToList();
        var obsoleteIds = obsoleteVersions.Select(asset => asset.Id).ToArray();
        var obsoleteLinks = await dbContext.ShotAssetLinks
            .Where(link => link.ProjectId == projectId && obsoleteIds.Contains(link.AssetId))
            .ToListAsync(cancellationToken);

        dbContext.ShotAssetLinks.RemoveRange(obsoleteLinks);
        dbContext.Assets.RemoveRange(obsoleteVersions);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var version in obsoleteVersions)
        {
            await blobStorage.DeleteAsync(version.BlobKey, cancellationToken);
        }

        return latestVersions.Values.Select(latest => new PurgedAssetVersions(
            latest.ResourceId,
            latest.Name,
            latest.Id,
            latest.Version,
            obsoleteVersions.Count(asset => asset.ResourceId == latest.ResourceId))).ToArray();
    }

    private static Asset CreateAsset(AssetWriteRequest request, Asset? latestVersion)
    {
        var assetId = request.AssetId ?? Guid.NewGuid();
        var version = (latestVersion?.Version ?? 0) + 1;
        var now = DateTimeOffset.UtcNow;
        return new Asset
        {
            Id = assetId,
            ResourceId = request.ResourceId ?? latestVersion?.ResourceId ?? assetId,
            Version = version,
            ProjectId = request.ProjectId,
            Type = request.Type,
            Name = latestVersion?.Name ?? request.CanonicalName,
            BlobKey = $"{request.ProjectId:N}/{request.Type}/{assetId:N}{request.Extension}",
            FileName = $"{SanitizeFileName(request.FileNameBase, request.FileNameFallback)}-v{version}{request.Extension}",
            ContentType = request.ContentType,
            GenerationMetadataJson = request.GenerationMetadataJson,
            SizeBytes = request.Content.LongLength,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    private void Detach(IEnumerable<Asset> assets)
    {
        foreach (var asset in assets)
        {
            dbContext.Entry(asset).State = EntityState.Detached;
        }
    }

    private async Task DeleteBlobsAsync(IEnumerable<Asset> assets)
    {
        foreach (var asset in assets)
        {
            await blobStorage.DeleteAsync(asset.BlobKey, CancellationToken.None);
        }
    }

    private async Task<Asset?> FindExistingAssetAsync(
        AssetWriteRequest request,
        CancellationToken cancellationToken) =>
        request.AssetId is null
            ? null
            : await dbContext.Assets
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    asset => asset.ProjectId == request.ProjectId && asset.Id == request.AssetId,
                    cancellationToken);

    private async Task<Asset?> FindLatestVersionAsync(
        AssetWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Target == AssetVersionTarget.ExistingResource)
        {
            var latestVersion = await dbContext.Assets
                .AsNoTracking()
                .Where(asset =>
                    asset.ProjectId == request.ProjectId
                    && asset.ResourceId == request.ResourceId)
                .OrderByDescending(asset => asset.Version)
                .FirstOrDefaultAsync(cancellationToken);
            return latestVersion ?? throw new InvalidOperationException(
                "The target resource does not belong to the requested project.");
        }

        var candidates = await dbContext.Assets
            .AsNoTracking()
            .Where(asset => asset.ProjectId == request.ProjectId && asset.Type == request.Type)
            .ToListAsync(cancellationToken);
        return candidates
            .Where(asset => NamesMatch(asset.Name, request.CanonicalName, request.Target))
            .OrderByDescending(asset => asset.Version)
            .FirstOrDefault();
    }

    private static bool NamesMatch(
        string existingName,
        string canonicalName,
        AssetVersionTarget target) =>
        target switch
        {
            AssetVersionTarget.ExactName => existingName.Equals(canonicalName, StringComparison.Ordinal),
            AssetVersionTarget.CaseInsensitiveName => existingName.Equals(
                canonicalName,
                StringComparison.OrdinalIgnoreCase),
            AssetVersionTarget.ResourceSubject => GetResourceSubject(existingName).Equals(
                GetResourceSubject(canonicalName),
                StringComparison.OrdinalIgnoreCase),
            _ => GetImageResourceKey(existingName).Equals(
                GetImageResourceKey(canonicalName),
                StringComparison.OrdinalIgnoreCase)
        };

    private static string GetResourceSubject(string value) =>
        value.Split('·', StringSplitOptions.TrimEntries)[0];

    private static string GetImageResourceKey(string value)
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

    private static string SanitizeFileName(string value, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized.Trim();
    }

    private static void Validate(AssetWriteRequest request)
    {
        if (request.Target == AssetVersionTarget.ExistingResource && request.ResourceId is null)
        {
            throw new ArgumentException("Existing resource writes require a resource ID.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.Extension) || !request.Extension.StartsWith('.'))
        {
            throw new ArgumentException("Asset extension must start with a period.", nameof(request));
        }
    }
}