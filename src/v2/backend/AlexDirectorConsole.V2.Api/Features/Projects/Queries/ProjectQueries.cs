using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Database.Data;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Queries;

public sealed record ListProjectsQuery : IQuery<IReadOnlyList<ProjectView>>;

public sealed class ListProjectsQueryHandler(V2DbContext dbContext)
    : IQueryHandler<ListProjectsQuery, IReadOnlyList<ProjectView>>
{
    public async Task<IReadOnlyList<ProjectView>> HandleAsync(
        ListProjectsQuery query,
        CancellationToken cancellationToken)
    {
        var projects = await dbContext.Projects
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return projects
            .OrderByDescending(project => project.UpdatedAtUtc)
            .Select(ProjectView.FromProject)
            .ToArray();
    }
}

public sealed record GetProjectQuery(Guid ProjectId) : IQuery<ProjectView?>;

public sealed class GetProjectQueryHandler(V2DbContext dbContext)
    : IQueryHandler<GetProjectQuery, ProjectView?>
{
    public Task<ProjectView?> HandleAsync(
        GetProjectQuery query,
        CancellationToken cancellationToken) =>
        dbContext.Projects
            .AsNoTracking()
            .Where(project => project.Id == query.ProjectId)
            .Select(project => new ProjectView(
                project.Id,
                project.Name,
                project.Description,
                project.CurrentCreativeSettingsId,
                project.CreatedAtUtc,
                project.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
}