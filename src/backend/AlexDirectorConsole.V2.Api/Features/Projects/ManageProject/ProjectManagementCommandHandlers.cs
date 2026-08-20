using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Database.Data;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Projects.ManageProject;

public sealed class UpdateProjectCommandHandler(
    V2DbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<UpdateProjectCommand, UpdateProjectResult>
{
    public async Task<UpdateProjectResult> HandleAsync(
        UpdateProjectCommand command,
        CancellationToken cancellationToken)
    {
        var name = command.Name?.Trim();
        var description = command.Description?.Trim();
        var errors = Validate(name, description);
        if (errors.Count > 0)
        {
            return UpdateProjectResult.Invalid(errors);
        }

        var project = await dbContext.Projects
            .SingleOrDefaultAsync(item => item.Id == command.ProjectId, cancellationToken);
        if (project is null)
        {
            return UpdateProjectResult.Missing();
        }

        project.Name = name!;
        project.Description = string.IsNullOrEmpty(description) ? null : description;
        project.UpdatedAtUtc = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);

        return UpdateProjectResult.Success(ProjectView.FromProject(project));
    }

    private static Dictionary<string, string[]> Validate(string? name, string? description)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["项目名称不能为空。"];
        }
        else if (name.Length > 200)
        {
            errors["name"] = ["项目名称不能超过 200 个字符。"];
        }

        if (description?.Length > 4000)
        {
            errors["description"] = ["项目描述不能超过 4000 个字符。"];
        }

        return errors;
    }
}

public sealed class DeleteProjectCommandHandler(V2DbContext dbContext)
    : ICommandHandler<DeleteProjectCommand, DeleteProjectResult>
{
    public async Task<DeleteProjectResult> HandleAsync(
        DeleteProjectCommand command,
        CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects
            .SingleOrDefaultAsync(item => item.Id == command.ProjectId, cancellationToken);
        if (project is null)
        {
            return DeleteProjectResult.NotFound;
        }

        dbContext.Projects.Remove(project);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return DeleteProjectResult.Deleted;
        }
        catch (DbUpdateException)
        {
            return DeleteProjectResult.HasDependencies;
        }
    }
}