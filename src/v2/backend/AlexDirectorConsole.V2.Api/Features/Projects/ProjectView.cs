using AlexDirectorConsole.V2.Database.Models;

namespace AlexDirectorConsole.V2.Api.Features.Projects;

public sealed record ProjectView(
    Guid Id,
    string Name,
    string? Description,
    Guid? CurrentCreativeSettingsId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static ProjectView FromProject(Project project) => new(
        project.Id,
        project.Name,
        project.Description,
        project.CurrentCreativeSettingsId,
        project.CreatedAtUtc,
        project.UpdatedAtUtc);
}