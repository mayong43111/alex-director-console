using System.Text.Json;
using AlexDirectorConsole.V2.Api.Features.Projects.Generation;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Versions;

public sealed record ResourceVersionView(
    Guid AssetId,
    Guid ResourceId,
    int Version,
    string Type,
    string Name,
    bool IsCurrent,
    DateTimeOffset CreatedAtUtc);

public sealed record SetCurrentResourceVersionRequest(Guid AssetId);

public static class ResourceVersionEndpoints
{
    public static IEndpointRouteBuilder MapResourceVersions(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/projects/{projectId:guid}/assets/{assetId:guid}/versions");
        group.MapGet("/", ListVersionsAsync);
        group.MapPut("/current", SetCurrentAsync);
        return app;
    }

    private static async Task<IResult> ListVersionsAsync(
        Guid projectId,
        Guid assetId,
        V2DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var anchor = await dbContext.Assets.AsNoTracking().SingleOrDefaultAsync(
            item => item.ProjectId == projectId && item.Id == assetId,
            cancellationToken);
        if (anchor is null) return Results.NotFound();

        var assets = await dbContext.Assets
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId
                && item.ResourceId == anchor.ResourceId
                && item.Type == anchor.Type)
            .OrderByDescending(item => item.Version)
            .ToListAsync(cancellationToken);

        var currentAssetId = await ResolveCurrentAssetIdAsync(
            dbContext,
            projectId,
            anchor.ResourceId,
            anchor.Type,
            assets[0].Id,
            cancellationToken);
        return Results.Ok(assets.Select(item => new ResourceVersionView(
            item.Id,
            item.ResourceId,
            item.Version,
            item.Type,
            item.Name,
            item.Id == currentAssetId,
            item.CreatedAtUtc)));
    }

    private static async Task<IResult> SetCurrentAsync(
        Guid projectId,
        Guid assetId,
        SetCurrentResourceVersionRequest request,
        V2DbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var anchor = await dbContext.Assets.SingleOrDefaultAsync(
            item => item.ProjectId == projectId && item.Id == assetId,
            cancellationToken);
        if (anchor is null) return Results.NotFound();

        var target = await dbContext.Assets.SingleOrDefaultAsync(
            item => item.Id == request.AssetId
                && item.ProjectId == projectId
                && item.ResourceId == anchor.ResourceId
                && item.Type == anchor.Type,
            cancellationToken);
        if (target is null) return Results.NotFound();

        var now = timeProvider.GetUtcNow();
        var latestAssetId = await dbContext.Assets.AsNoTracking()
            .Where(item => item.ProjectId == projectId
                && item.ResourceId == anchor.ResourceId
                && item.Type == anchor.Type)
            .OrderByDescending(item => item.Version)
            .Select(item => item.Id)
            .FirstAsync(cancellationToken);
        var previousAssetId = await ResolveCurrentAssetIdAsync(
            dbContext,
            projectId,
            anchor.ResourceId,
            anchor.Type,
            latestAssetId,
            cancellationToken);
        var state = await dbContext.ResourceStates.SingleOrDefaultAsync(
            item => item.ProjectId == projectId && item.ResourceId == anchor.ResourceId,
            cancellationToken);
        if (state is null)
        {
            state = new ResourceState
            {
                ProjectId = projectId,
                ResourceId = anchor.ResourceId,
                ResourceType = target.Type
            };
            dbContext.ResourceStates.Add(state);
        }
        else if (state.ResourceType != target.Type)
        {
            return Results.Conflict(new { error = "该资源的当前状态类型与目标版本不一致。" });
        }

        var previousAsset = previousAssetId == Guid.Empty
            ? null
            : await dbContext.Assets.SingleOrDefaultAsync(
            item => item.Id == previousAssetId && item.ProjectId == projectId,
                cancellationToken);
        state.CurrentAssetId = target.Id;
        state.LifecycleStatus = "active";
        state.IsStale = false;
        state.StaleReason = null;
        state.StaleSinceUtc = null;
        state.UpdatedAtUtc = now;

        if (target.Type == ProjectSettingsDefaults.AssetType)
        {
            var project = await dbContext.Projects.SingleAsync(
                item => item.Id == projectId,
                cancellationToken);
            var document = JsonSerializer.Deserialize<ProjectSettingsDocument>(
                target.DocumentJson ?? "{}",
                ProjectSettingsDefaults.JsonOptions);
            if (document is null) return Results.Conflict(new { error = "目标项目设定版本无法读取。" });
            project.CurrentCreativeSettingsId = target.Id;
            project.Name = document.ProjectName;
            project.Description = document.Description;
            project.UpdatedAtUtc = now;
        }

        var definition = await dbContext.ShotDefinitions.SingleOrDefaultAsync(
            item => item.ProjectId == projectId && item.ShotResourceId == anchor.ResourceId,
            cancellationToken);
        if (definition is not null)
        {
            using var documentJson = JsonDocument.Parse(target.DocumentJson ?? "{}");
            var document = documentJson.RootElement;
            definition.ShotAssetId = target.Id;
            definition.SceneNumber = document.GetProperty("sceneNumber").GetInt32();
            definition.ShotNumber = document.GetProperty("shotNumber").GetInt32();
            definition.DurationSeconds = document.GetProperty("durationSeconds").GetDouble();
            definition.UpdatedAtUtc = now;
            var claims = await dbContext.ShotBeatClaims
                .Where(item => item.ProjectId == projectId && item.ShotResourceId == anchor.ResourceId)
                .ToListAsync(cancellationToken);
            foreach (var claim in claims) claim.ShotAssetId = target.Id;
        }

        if (previousAsset is not null && previousAsset.Id != target.Id)
        {
            await AssetStalenessPropagation.MarkRequiredDependentsStaleAsync(
                dbContext,
                previousAsset,
                target,
                now,
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new ResourceVersionView(
            target.Id,
            target.ResourceId,
            target.Version,
            target.Type,
            target.Name,
            true,
            target.CreatedAtUtc));
    }

    private static async Task<Guid> ResolveCurrentAssetIdAsync(
        V2DbContext dbContext,
        Guid projectId,
        Guid resourceId,
        string resourceType,
        Guid fallbackAssetId,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.ResourceStates.AsNoTracking().SingleOrDefaultAsync(
            item => item.ProjectId == projectId
                && item.ResourceId == resourceId
                && item.ResourceType == resourceType,
            cancellationToken);
        if (state is not null) return state.CurrentAssetId;

        var shotAssetId = await dbContext.ShotDefinitions.AsNoTracking()
            .Where(item => item.ProjectId == projectId && item.ShotResourceId == resourceId)
            .Select(item => (Guid?)item.ShotAssetId)
            .SingleOrDefaultAsync(cancellationToken);
        return shotAssetId ?? fallbackAssetId;
    }
}