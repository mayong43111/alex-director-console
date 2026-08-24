using AlexDirectorConsole.V2.Api.Application.Cqrs;

namespace AlexDirectorConsole.V2.Api.Features.Projects.ManageProject;

public sealed record UpdateProjectCommand(Guid ProjectId, string? Name, string? Description)
    : ICommand<UpdateProjectResult>;

public sealed record UpdateProjectResult(
    ProjectView? Project,
    Dictionary<string, string[]> Errors,
    bool NotFound)
{
    public bool IsSuccess => Project is not null;

    public static UpdateProjectResult Success(ProjectView project) =>
        new(project, new Dictionary<string, string[]>(), false);

    public static UpdateProjectResult Invalid(Dictionary<string, string[]> errors) =>
        new(null, errors, false);

    public static UpdateProjectResult Missing() =>
        new(null, new Dictionary<string, string[]>(), true);
}

public sealed record DeleteProjectCommand(Guid ProjectId, bool Force)
    : ICommand<DeleteProjectResult>;

public enum DeleteProjectResult
{
    Deleted,
    NotFound,
    HasDependencies
}