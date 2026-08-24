using AlexDirectorConsole.V2.Api.Application.Cqrs;

namespace AlexDirectorConsole.V2.Api.Features.Projects.ManageProject;

public static class ProjectManagementEndpoint
{
    public static IEndpointRouteBuilder MapProjectManagement(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut(
                "/api/v2/projects/{projectId:guid}",
                async (
                    Guid projectId,
                    UpdateProjectRequest request,
                    ICommandDispatcher dispatcher,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.SendAsync(
                        new UpdateProjectCommand(projectId, request.Name, request.Description),
                        cancellationToken);

                    if (result.NotFound)
                    {
                        return Results.NotFound();
                    }

                    return result.IsSuccess
                        ? Results.Ok(result.Project)
                        : Results.ValidationProblem(result.Errors);
                })
            .WithName("UpdateV2Project")
            .Produces<ProjectView>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        endpoints.MapDelete(
                "/api/v2/projects/{projectId:guid}",
                async (
                    Guid projectId,
                    bool? force,
                    ICommandDispatcher dispatcher,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.SendAsync(
                        new DeleteProjectCommand(projectId, force ?? false),
                        cancellationToken);

                    return result switch
                    {
                        DeleteProjectResult.Deleted => Results.NoContent(),
                        DeleteProjectResult.NotFound => Results.NotFound(),
                        _ => Results.Problem(
                            detail: "项目已有设定、资产或生产数据。确认后可删除项目及全部关联数据。",
                            statusCode: StatusCodes.Status409Conflict)
                    };
                })
            .WithName("DeleteV2Project")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    public sealed record UpdateProjectRequest(string? Name, string? Description);
}