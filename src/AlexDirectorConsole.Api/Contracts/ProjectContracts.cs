using AlexDirectorConsole.Api.Models;

namespace AlexDirectorConsole.Api.Contracts;

public sealed record UpsertProjectRequest(
    string Name,
    string Description,
    DateTimeOffset CreatedAt,
    string FormatPreset,
    int OutputWidth,
    int OutputHeight,
    string PreviewResolution,
    string LanguageModel,
    string ImageModel,
    string VideoModel);

public sealed record ProjectResponse(
    Guid Id,
    string Name,
    string Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string FormatPreset,
    int OutputWidth,
    int OutputHeight,
    string PreviewResolution,
    string LanguageModel,
    string ImageModel,
    string VideoModel)
{
    public static ProjectResponse FromProject(Project project) => new(
        project.Id,
        project.Name,
        project.Description,
        project.CreatedAtUtc,
        project.UpdatedAtUtc,
        project.FormatPreset,
        project.OutputWidth,
        project.OutputHeight,
        project.PreviewResolution,
        project.LanguageModel,
        project.ImageModel,
        project.VideoModel);
}