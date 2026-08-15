using AlexDirectorConsole.Api.Application.Production;
using AlexDirectorConsole.Api.Contracts;

namespace AlexDirectorConsole.Api.Endpoints;

public static class ProductionRunEndpoints
{
    public static IEndpointRouteBuilder MapProductionRunEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/api/projects/{projectId:guid}/production-runs",
            async (
                Guid projectId,
                CreateProductionRunRequest request,
                IProductionRunService productionRuns,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var snapshot = await productionRuns.StartAsync(
                        projectId,
                        request.Instruction,
                        request.DryRun,
                        request.KeepVmRunning,
                        request.ShotNameContains,
                        cancellationToken);
                    return Results.Created(
                        $"/api/projects/{projectId}/production-runs/{snapshot.Run.Id}",
                        ProductionRunResponse.FromSnapshot(snapshot));
                }
                catch (ArgumentException exception)
                {
                    return Results.BadRequest(new { error = exception.Message });
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Conflict(new { error = exception.Message });
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound();
                }
            })
            .WithName("CreateProductionRun")
            .WithOpenApi();

        app.MapGet(
            "/api/projects/{projectId:guid}/production-runs/{runId:guid}",
            async (
                Guid projectId,
                Guid runId,
                IProductionRunService productionRuns,
                CancellationToken cancellationToken) =>
            {
                var snapshot = await productionRuns.GetAsync(projectId, runId, cancellationToken);
                return snapshot is null
                    ? Results.NotFound()
                    : Results.Ok(ProductionRunResponse.FromSnapshot(snapshot));
            })
            .WithName("GetProductionRun")
            .WithOpenApi();

        app.MapPost(
            "/api/projects/{projectId:guid}/production-runs/{runId:guid}/resume",
            async (
                Guid projectId,
                Guid runId,
                IProductionRunService productionRuns,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var snapshot = await productionRuns.ResumeAsync(
                        projectId,
                        runId,
                        cancellationToken);
                    return snapshot is null
                        ? Results.NotFound()
                        : Results.Accepted(
                            $"/api/projects/{projectId}/production-runs/{runId}",
                            ProductionRunResponse.FromSnapshot(snapshot));
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Conflict(new { error = exception.Message });
                }
            })
            .WithName("ResumeProductionRun")
            .WithOpenApi();

        return app;
    }
}