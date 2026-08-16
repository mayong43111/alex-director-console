using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Projects;

namespace AlexDirectorConsole.V2.Api.Features.Projects.CreateProject;

public static class CreateProjectEndpoint
{
    public static IEndpointRouteBuilder MapCreateProject(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v2/projects",
                async (
                    CreateProjectRequest request,
                    ICommandDispatcher dispatcher,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.SendAsync(
                        new CreateProjectCommand(request.Name, request.Description),
                        cancellationToken);

                    if (!result.IsSuccess)
                    {
                        return Results.ValidationProblem(result.Errors);
                    }

                    var project = result.Project!;
                    return Results.Created($"/api/v2/projects/{project.Id}", project);
                })
            .WithName("CreateV2Project")
            .Produces<ProjectView>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        return endpoints;
    }

    public sealed record CreateProjectRequest(string? Name, string? Description);
}