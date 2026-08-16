using AlexDirectorConsole.V2.Api.Application.Cqrs;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Queries;

public static class ProjectQueryEndpoints
{
    public static IEndpointRouteBuilder MapProjectQueries(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v2/projects",
                async (IQueryDispatcher dispatcher, CancellationToken cancellationToken) =>
                    Results.Ok(await dispatcher.QueryAsync(
                        new ListProjectsQuery(),
                        cancellationToken)))
            .WithName("ListV2Projects")
            .Produces<IReadOnlyList<ProjectView>>();

        endpoints.MapGet(
                "/api/v2/projects/{projectId:guid}",
                async (
                    Guid projectId,
                    IQueryDispatcher dispatcher,
                    CancellationToken cancellationToken) =>
                {
                    var project = await dispatcher.QueryAsync(
                        new GetProjectQuery(projectId),
                        cancellationToken);
                    return project is null ? Results.NotFound() : Results.Ok(project);
                })
            .WithName("GetV2Project")
            .Produces<ProjectView>()
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapGet(
                "/api/v2/projects/{projectId:guid}/production-episodes",
                async (
                    Guid projectId,
                    IQueryDispatcher dispatcher,
                    CancellationToken cancellationToken) =>
                    Results.Ok(await dispatcher.QueryAsync(
                        new ListProductionEpisodesQuery(projectId),
                        cancellationToken)))
            .WithName("ListV2ProductionEpisodes")
            .Produces<IReadOnlyList<ProductionEpisodeView>>();

        return endpoints;
    }
}