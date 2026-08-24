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

        if (!command.Force && await HasProjectDataAsync(command.ProjectId, cancellationToken))
        {
            return DeleteProjectResult.HasDependencies;
        }

        if (command.Force)
        {
            await DeleteProjectDataAsync(project, cancellationToken);
            return DeleteProjectResult.Deleted;
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

    private async Task<bool> HasProjectDataAsync(Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.ProductionEpisodes.AnyAsync(item => item.ProjectId == projectId, cancellationToken)
        || await dbContext.Assets.AnyAsync(item => item.ProjectId == projectId, cancellationToken)
        || await dbContext.ResourceStates.AnyAsync(item => item.ProjectId == projectId, cancellationToken)
        || await dbContext.AssetDependencies.AnyAsync(item => item.ProjectId == projectId, cancellationToken)
        || await dbContext.VisualReferences.AnyAsync(item => item.ProjectId == projectId, cancellationToken)
        || await dbContext.ShotDefinitions.AnyAsync(item => item.ProjectId == projectId, cancellationToken)
        || await dbContext.ShotBeatClaims.AnyAsync(item => item.ProjectId == projectId, cancellationToken)
        || await dbContext.ShotAssetLinks.AnyAsync(item => item.ProjectId == projectId, cancellationToken)
        || await dbContext.DirectorDecisions.AnyAsync(item => item.ProjectId == projectId, cancellationToken)
        || await dbContext.ValidationRuns.AnyAsync(item => item.ProjectId == projectId, cancellationToken)
        || await dbContext.AgentTasks.AnyAsync(item => item.ProjectId == projectId, cancellationToken)
        || await dbContext.AgentTaskItems.AnyAsync(item => item.ProjectId == projectId, cancellationToken)
        || await dbContext.ProductionRuns.AnyAsync(item => item.ProjectId == projectId, cancellationToken)
        || await dbContext.ProductionRunItems.AnyAsync(item => item.ProjectId == projectId, cancellationToken)
        || await dbContext.Sessions.AnyAsync(item => item.ProjectId == projectId, cancellationToken)
        || await dbContext.CopilotConversations.AnyAsync(item => item.ProjectId == projectId, cancellationToken);

    private async Task DeleteProjectDataAsync(
        Database.Models.Project project,
        CancellationToken cancellationToken)
    {
        var projectId = project.Id;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        project.CurrentCreativeSettingsId = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        await dbContext.ProductionRunItems
            .Where(item => item.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ProductionRuns
            .Where(item => item.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.AgentTaskOutputs
            .Where(output =>
                dbContext.AgentTasks.Any(task => task.Id == output.TaskId && task.ProjectId == projectId)
                || dbContext.Assets.Any(asset => asset.Id == output.AssetId && asset.ProjectId == projectId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.AgentTaskItems
            .Where(item => item.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.VisualReferences
            .Where(item => item.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ShotBeatClaims
            .Where(item => item.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ShotAssetLinks
            .Where(item => item.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ShotDefinitions
            .Where(item => item.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.AssetDependencies
            .Where(item => item.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ResourceStates
            .Where(item => item.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.ValidationRuns
            .Where(item => item.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.DirectorDecisions
            .Where(item => item.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.Assets
            .Where(item => item.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.AgentTasks
            .Where(item => item.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.ProductionEpisodes
            .Where(item => item.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.Sessions
            .Where(item => item.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.CopilotConversations
            .Where(item => item.ProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.Projects.Remove(project);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}