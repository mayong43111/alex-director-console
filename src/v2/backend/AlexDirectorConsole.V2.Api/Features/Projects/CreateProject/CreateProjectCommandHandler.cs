using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Projects;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;

namespace AlexDirectorConsole.V2.Api.Features.Projects.CreateProject;

public sealed class CreateProjectCommandHandler(
    V2DbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<CreateProjectCommand, CreateProjectResult>
{
    public async Task<CreateProjectResult> HandleAsync(
        CreateProjectCommand command,
        CancellationToken cancellationToken)
    {
        var name = command.Name?.Trim();
        var description = command.Description?.Trim();
        var errors = Validate(name, description);
        if (errors.Count > 0)
        {
            return CreateProjectResult.Invalid(errors);
        }

        var now = timeProvider.GetUtcNow();
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = name!,
            Description = string.IsNullOrEmpty(description) ? null : description,
            CurrentCreativeSettingsId = null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreateProjectResult.Success(ProjectView.FromProject(project));
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