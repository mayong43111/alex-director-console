using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Projects;

namespace AlexDirectorConsole.V2.Api.Features.Projects.CreateProject;

public sealed record CreateProjectCommand(string? Name, string? Description)
    : ICommand<CreateProjectResult>;

public sealed record CreateProjectResult(
    ProjectView? Project,
    Dictionary<string, string[]> Errors)
{
    public bool IsSuccess => Project is not null;

    public static CreateProjectResult Success(ProjectView project) =>
        new(project, new Dictionary<string, string[]>());

    public static CreateProjectResult Invalid(Dictionary<string, string[]> errors) =>
        new(null, errors);
}

