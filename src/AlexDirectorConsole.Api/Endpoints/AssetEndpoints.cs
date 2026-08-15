using System.Text.Json;
using AlexDirectorConsole.Api.Application.Assets;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Data;
using AlexDirectorConsole.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.Api.Endpoints;

public static class AssetEndpoints
{
    public static IEndpointRouteBuilder MapAssetEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/projects/{projectId:guid}/assets",
            async (Guid projectId, string? type, AppDbContext dbContext, CancellationToken cancellationToken) =>
            {
                var normalizedType = type?.Trim().ToLowerInvariant();
                var query = dbContext.Assets
                    .AsNoTracking()
                    .Where(asset => asset.ProjectId == projectId);

                if (!string.IsNullOrWhiteSpace(normalizedType))
                {
                    query = query.Where(asset => asset.Type == normalizedType);
                }

                var assets = (await query.ToListAsync(cancellationToken))
                    .GroupBy(asset => asset.ResourceId)
                    .Select(group => new
                    {
                        Latest = group
                            .OrderByDescending(asset => asset.Version)
                            .ThenByDescending(asset => asset.CreatedAtUtc)
                            .First(),
                        Count = group.Count()
                    });
                assets = normalizedType == "shot"
                    ? assets.OrderBy(item => item.Latest.Name, StringComparer.Ordinal)
                    : assets.OrderByDescending(item => item.Latest.UpdatedAtUtc);
                var assetItems = assets.ToList();
                var sourceScripts = await ResolveSourceScriptsAsync(
                    projectId,
                    assetItems.Select(item => item.Latest),
                    dbContext,
                    cancellationToken);
                var responseAssets = assetItems
                    .Select(item => AssetResponse.FromAssetWithSourceScript(
                        item.Latest,
                        item.Count,
                        sourceScripts.GetValueOrDefault(item.Latest.Id)))
                    .ToList();

                return Results.Ok(responseAssets);
            })
            .WithName("GetProjectAssets")
            .WithOpenApi();

        app.MapGet(
            "/api/projects/{projectId:guid}/assets/{assetId:guid}/versions",
            async (Guid projectId, Guid assetId, AppDbContext dbContext, CancellationToken cancellationToken) =>
            {
                var selected = await dbContext.Assets
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        asset => asset.ProjectId == projectId && asset.Id == assetId,
                        cancellationToken);
                if (selected is null)
                {
                    return Results.NotFound();
                }

                var versionAssets = (await dbContext.Assets
                        .AsNoTracking()
                        .Where(asset => asset.ProjectId == projectId && asset.ResourceId == selected.ResourceId)
                        .ToListAsync(cancellationToken))
                    .OrderByDescending(asset => asset.Version)
                    .ToList();
                var sourceScripts = await ResolveSourceScriptsAsync(
                    projectId,
                    versionAssets,
                    dbContext,
                    cancellationToken);
                return Results.Ok(versionAssets.Select(asset =>
                    AssetResponse.FromAssetWithSourceScript(
                        asset,
                        versionAssets.Count,
                        sourceScripts.GetValueOrDefault(asset.Id))));
            })
            .WithName("GetAssetVersions")
            .WithOpenApi();

        app.MapPost(
            "/api/projects/{projectId:guid}/assets",
            async (
                Guid projectId,
                HttpRequest request,
                IAssetWriter assetWriter,
                IConfiguration configuration,
                CancellationToken cancellationToken) =>
            {
                if (!request.HasFormContentType)
                {
                    return Results.BadRequest(new { error = "multipart/form-data is required." });
                }

                var form = await request.ReadFormAsync(cancellationToken);
                var file = form.Files.GetFile("file");
                if (file is null || file.Length == 0)
                {
                    return Results.BadRequest(new { error = "A non-empty file is required." });
                }

                var maxUploadBytes = configuration.GetValue<long>(
                    "BlobStorage:MaxUploadBytes",
                    104857600);
                if (file.Length > maxUploadBytes)
                {
                    return Results.BadRequest(new { error = $"File exceeds the {maxUploadBytes} byte limit." });
                }

                var type = form["type"].ToString().Trim().ToLowerInvariant();
                if (!IsValidAssetType(type))
                {
                    return Results.BadRequest(new { error = "Asset type must use 1-50 lowercase letters, numbers, hyphens, or underscores." });
                }

                var fileName = Path.GetFileName(file.FileName);
                if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 260)
                {
                    return Results.BadRequest(new { error = "File name is invalid or longer than 260 characters." });
                }

                var name = form["name"].ToString().Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = Path.GetFileNameWithoutExtension(fileName);
                }

                if (name.Length > 260)
                {
                    return Results.BadRequest(new { error = "Asset name cannot exceed 260 characters." });
                }

                var extension = Path.GetExtension(fileName).ToLowerInvariant();
                if (extension.Length > 20)
                {
                    extension = string.Empty;
                }

                await using var content = file.OpenReadStream();
                var asset = await assetWriter.CreateAsync(
                    new AssetCreateRequest(
                        projectId,
                        type,
                        name,
                        fileName,
                        extension,
                        string.IsNullOrWhiteSpace(file.ContentType)
                            ? "application/octet-stream"
                            : file.ContentType,
                        file.Length),
                    content,
                    cancellationToken);

                return Results.Created($"/api/assets/{asset.Id}", AssetResponse.FromAsset(asset));
            })
            .WithName("UploadProjectAsset")
            .DisableAntiforgery();

        app.MapGet(
            "/api/projects/{projectId:guid}/assets/{assetId:guid}/content",
            async (
                Guid projectId,
                Guid assetId,
                IAssetReader assetReader,
                CancellationToken cancellationToken) =>
            {
                var asset = await assetReader.GetAsync(projectId, assetId, cancellationToken);
                if (asset is null)
                {
                    return Results.NotFound();
                }

                var content = await assetReader.OpenReadAsync(projectId, asset, cancellationToken);
                return content is null
                    ? Results.NotFound()
                    : Results.File(
                        content,
                        asset.ContentType,
                        asset.FileName,
                        enableRangeProcessing: true);
            })
            .WithName("GetAssetContent")
            .WithOpenApi();

        app.MapDelete(
            "/api/projects/{projectId:guid}/assets/{assetId:guid}",
            async (
                Guid projectId,
                Guid assetId,
                IAssetWriter assetWriter,
                CancellationToken cancellationToken) =>
            {
                var deleted = await assetWriter.DeleteResourceAsync(
                    projectId,
                    assetId,
                    cancellationToken);
                return deleted is null ? Results.NotFound() : Results.NoContent();
            })
            .WithName("DeleteProjectAsset")
            .WithOpenApi();

        app.MapGet(
            "/api/projects/{projectId:guid}/assets/{shotAssetId:guid}/linked-assets",
            async (
                Guid projectId,
                Guid shotAssetId,
                AppDbContext dbContext,
                CancellationToken cancellationToken) =>
            {
                var shot = await dbContext.Assets
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        asset => asset.ProjectId == projectId && asset.Id == shotAssetId && asset.Type == "shot",
                        cancellationToken);
                if (shot is null)
                {
                    return Results.NotFound();
                }

                var links = await dbContext.ShotAssetLinks
                    .AsNoTracking()
                    .Where(link => link.ProjectId == shot.ProjectId && link.ShotResourceId == shot.ResourceId)
                    .ToListAsync(cancellationToken);
                links = links
                    .OrderBy(link => link.Role)
                    .ThenByDescending(link => link.CreatedAtUtc)
                    .ToList();
                var assetIds = links.Select(link => link.AssetId).Distinct().ToArray();
                var assets = await dbContext.Assets
                    .AsNoTracking()
                    .Where(asset => asset.ProjectId == projectId && assetIds.Contains(asset.Id))
                    .ToDictionaryAsync(asset => asset.Id, cancellationToken);
                var validLinks = links
                    .Where(link => assets.ContainsKey(link.AssetId))
                    .ToList();
                var exclusiveRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "first-frame",
                    "last-frame",
                    "video"
                };
                var responseLinks = validLinks
                    .Where(link => exclusiveRoles.Contains(link.Role))
                    .GroupBy(link => link.Role, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group
                        .OrderByDescending(link => link.CreatedAtUtc)
                        .ThenByDescending(link => assets[link.AssetId].Version)
                        .First())
                    .Concat(validLinks
                        .Where(link => !exclusiveRoles.Contains(link.Role))
                    .GroupBy(link => new { link.Role, assets[link.AssetId].ResourceId })
                    .Select(group => group
                        .OrderByDescending(link => assets[link.AssetId].Version)
                        .ThenByDescending(link => link.CreatedAtUtc)
                        .First()))
                    .OrderBy(link => link.Role)
                    .ThenByDescending(link => link.CreatedAtUtc);
                var response = responseLinks
                    .Select(link => new ShotAssetLinkResponse(
                        link.Id,
                        link.Role,
                        link.CreatedAtUtc,
                        AssetResponse.FromAsset(assets[link.AssetId])))
                    .ToArray();
                return Results.Ok(response);
            })
            .WithName("GetShotLinkedAssets")
            .WithOpenApi();

        return app;
    }

    private static async Task<Dictionary<Guid, Asset>> ResolveSourceScriptsAsync(
        Guid projectId,
        IEnumerable<Asset> assets,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var finalVideos = assets
            .Where(asset => asset.Type == "media" && asset.ContentType.StartsWith("video/"))
            .Select(asset => new { Asset = asset, Metadata = ParseMetadata(asset.GenerationMetadataJson) })
            .Where(item => item.Metadata is not null
                && item.Metadata.RootElement.TryGetProperty("operation", out var operation)
                && operation.GetString() == "assemble-project-video")
            .ToArray();
        if (finalVideos.Length == 0) return [];

        var scriptAssetIdsByAsset = new Dictionary<Guid, Guid>();
        var scriptResourceIdsByAsset = new Dictionary<Guid, Guid>();
        var shotAssetIdsByAsset = new Dictionary<Guid, Guid[]>();
        foreach (var item in finalVideos)
        {
            var root = item.Metadata!.RootElement;
            if (root.TryGetProperty("sourceScript", out var sourceScript)
                && sourceScript.TryGetProperty("assetId", out var assetId)
                && assetId.TryGetGuid(out var parsedAssetId))
            {
                scriptAssetIdsByAsset[item.Asset.Id] = parsedAssetId;
                continue;
            }

            if (!root.TryGetProperty("sources", out var sources) || sources.ValueKind != JsonValueKind.Array)
                continue;
            shotAssetIdsByAsset[item.Asset.Id] = sources.EnumerateArray()
                .Select(source => source.TryGetProperty("shotAssetId", out var shotAssetId)
                    && shotAssetId.TryGetGuid(out var parsedShotAssetId)
                        ? parsedShotAssetId
                        : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToArray();
        }

        var shotAssetIds = shotAssetIdsByAsset.Values.SelectMany(ids => ids).Distinct().ToArray();
        if (shotAssetIds.Length > 0)
        {
            var shotResourcesByAsset = await dbContext.Assets
                .AsNoTracking()
                .Where(asset => asset.ProjectId == projectId && shotAssetIds.Contains(asset.Id))
                .ToDictionaryAsync(asset => asset.Id, asset => asset.ResourceId, cancellationToken);
            var shotResourceIds = shotResourcesByAsset.Values.Distinct().ToArray();
            var scriptResourcesByShot = await dbContext.ShotDefinitions
                .AsNoTracking()
                .Where(definition => definition.ProjectId == projectId
                    && shotResourceIds.Contains(definition.ShotResourceId))
                .ToDictionaryAsync(
                    definition => definition.ShotResourceId,
                    definition => definition.ScriptResourceId,
                    cancellationToken);
            foreach (var (assetId, sourceShotAssetIds) in shotAssetIdsByAsset)
            {
                var sourceScriptResourceIds = sourceShotAssetIds
                    .Where(shotResourcesByAsset.ContainsKey)
                    .Select(id => shotResourcesByAsset[id])
                    .Where(scriptResourcesByShot.ContainsKey)
                    .Select(id => scriptResourcesByShot[id])
                    .Distinct()
                    .ToArray();
                var allShotsResolved = sourceShotAssetIds.Length > 0
                    && sourceShotAssetIds.All(id => shotResourcesByAsset.TryGetValue(id, out var shotResourceId)
                        && scriptResourcesByShot.ContainsKey(shotResourceId));
                if (allShotsResolved && sourceScriptResourceIds.Length == 1)
                    scriptResourceIdsByAsset[assetId] = sourceScriptResourceIds[0];
            }
        }

        var exactScriptAssetIds = scriptAssetIdsByAsset.Values.Distinct().ToArray();
        var exactScripts = await dbContext.Assets
            .AsNoTracking()
            .Where(asset => asset.ProjectId == projectId
                && asset.Type == "script"
                && exactScriptAssetIds.Contains(asset.Id))
            .ToDictionaryAsync(asset => asset.Id, cancellationToken);
        var scriptResourceIds = scriptResourceIdsByAsset.Values.Distinct().ToArray();
        var latestScripts = (await dbContext.Assets
                .AsNoTracking()
                .Where(asset => asset.ProjectId == projectId
                    && asset.Type == "script"
                    && scriptResourceIds.Contains(asset.ResourceId))
                .ToListAsync(cancellationToken))
            .GroupBy(asset => asset.ResourceId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(asset => asset.Version).First());
        var result = scriptAssetIdsByAsset
            .Where(item => exactScripts.ContainsKey(item.Value))
            .ToDictionary(item => item.Key, item => exactScripts[item.Value]);
        foreach (var item in scriptResourceIdsByAsset
            .Where(item => latestScripts.ContainsKey(item.Value))
            .Select(item => new { item.Key, Script = latestScripts[item.Value] }))
        {
            result[item.Key] = item.Script;
        }
        return result;
    }

    private static JsonDocument? ParseMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return null;
        try
        {
            return JsonDocument.Parse(metadataJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsValidAssetType(string type) =>
        type.Length is > 0 and <= 50
        && type.All(character =>
            character is >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '-'
            or '_');
}