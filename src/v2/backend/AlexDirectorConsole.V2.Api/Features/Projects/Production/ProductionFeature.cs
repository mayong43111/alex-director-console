using System.Text.Json;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Production;

public sealed record ProductionRunItemView(
    Guid Id,
    Guid ShotResourceId,
    Guid ShotAssetId,
    string ShotName,
    string Stage,
    string Status,
    int Attempt,
    Guid? OutputAssetId,
    string? OutputUrl,
    string? ErrorCode,
    string? ErrorDetail,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record ProductionRunView(
    Guid Id,
    Guid ProductionEpisodeId,
    int EpisodeNumber,
    string EpisodeTitle,
    string Mode,
    string Status,
    string CurrentStage,
    string OriginalInstruction,
    string? LastError,
    Guid? FinalAssetId,
    IReadOnlyList<ProductionRunItemView> Items,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset UpdatedAtUtc);

internal static class ProductionQueries
{
    public static async Task<IReadOnlyList<ProductionRunView>> ListAsync(
        V2DbContext dbContext,
        Guid projectId,
        Guid? productionEpisodeId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ProductionRuns.AsNoTracking()
            .Where(item => item.ProjectId == projectId);
        if (productionEpisodeId is not null)
        {
            query = query.Where(item => item.ProductionEpisodeId == productionEpisodeId);
        }
        var runs = await query.ToListAsync(cancellationToken);
        if (runs.Count == 0) return [];
        var runIds = runs.Select(item => item.Id).ToArray();
        var episodeIds = runs.Select(item => item.ProductionEpisodeId).Distinct().ToArray();
        var items = await dbContext.ProductionRunItems.AsNoTracking()
            .Where(item => runIds.Contains(item.RunId))
            .ToListAsync(cancellationToken);
        var episodes = await dbContext.ProductionEpisodes.AsNoTracking()
            .Where(item => item.ProjectId == projectId && episodeIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        return runs
            .OrderByDescending(item => item.CreatedAtUtc)
            .Select(run => ToView(
                run,
                episodes[run.ProductionEpisodeId],
                items.Where(item => item.RunId == run.Id)
                    .OrderBy(item => item.CreatedAtUtc)
                    .ToArray()))
            .ToArray();
    }

    public static async Task<ProductionRunView?> GetAsync(
        V2DbContext dbContext,
        Guid projectId,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.ProductionRuns.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == runId && item.ProjectId == projectId,
            cancellationToken);
        if (run is null) return null;
        var episode = await dbContext.ProductionEpisodes.AsNoTracking().SingleAsync(
            item => item.Id == run.ProductionEpisodeId,
            cancellationToken);
        var items = await dbContext.ProductionRunItems.AsNoTracking()
            .Where(item => item.RunId == run.Id)
            .ToListAsync(cancellationToken);
        return ToView(run, episode, items.OrderBy(item => item.CreatedAtUtc).ToArray());
    }

    private static ProductionRunView ToView(
        ProductionRun run,
        ProductionEpisode episode,
        IReadOnlyList<ProductionRunItem> items) => new(
            run.Id,
            run.ProductionEpisodeId,
            episode.EpisodeNumber,
            episode.Title,
            ReadMode(run.SpecJson),
            run.Status,
            run.CurrentStage,
            run.OriginalInstruction,
            run.LastError,
            run.FinalAssetId,
            items.Select(item => new ProductionRunItemView(
                item.Id,
                item.ShotResourceId,
                item.ShotAssetId,
                item.ShotName,
                item.Stage,
                item.Status,
                item.Attempt,
                item.OutputAssetId,
                item.OutputAssetId is null
                    ? null
                    : $"/api/v2/projects/{run.ProjectId}/storyboard/frames/{item.OutputAssetId}/content",
                item.ErrorCode,
                item.ErrorDetail,
                item.CreatedAtUtc,
                item.StartedAtUtc,
                item.CompletedAtUtc)).ToArray(),
            run.CreatedAtUtc,
            run.StartedAtUtc,
            run.CompletedAtUtc,
            run.UpdatedAtUtc);

    private static string ReadMode(string specJson)
    {
        try
        {
            using var document = JsonDocument.Parse(specJson);
            return document.RootElement.TryGetProperty("mode", out var mode)
                ? mode.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}

public static class ProductionEndpoints
{
    public static IEndpointRouteBuilder MapProduction(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/projects/{projectId:guid}/production-runs");
        group.MapGet("/", async (
            Guid projectId,
            Guid? productionEpisodeId,
            V2DbContext dbContext,
            CancellationToken cancellationToken) => Results.Ok(
                await ProductionQueries.ListAsync(
                    dbContext,
                    projectId,
                    productionEpisodeId,
                    cancellationToken)));
        group.MapGet("/{runId:guid}", async (
            Guid projectId,
            Guid runId,
            V2DbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var run = await ProductionQueries.GetAsync(
                dbContext,
                projectId,
                runId,
                cancellationToken);
            return run is null ? Results.NotFound() : Results.Ok(run);
        });
        return app;
    }
}