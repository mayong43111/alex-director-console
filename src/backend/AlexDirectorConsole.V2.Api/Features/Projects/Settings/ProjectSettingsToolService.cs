using System.Text.Json;
using AlexDirectorConsole.V2.Api.Application.Cqrs;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Settings;

public interface IProjectSettingsToolService
{
    Task<ProjectSettingsView> ReadAsync(Guid projectId, CancellationToken cancellationToken);

    Task<ProjectSettingsView> UpdateAsync(
        Guid projectId,
        string changesJson,
        CancellationToken cancellationToken);
}

public sealed class ProjectSettingsToolService(
    IQueryDispatcher queryDispatcher,
    ICommandDispatcher commandDispatcher) : IProjectSettingsToolService
{
    private static readonly HashSet<string> AllowedFields =
    [
        "projectName",
        "description",
        "contentType",
        "targetAudience",
        "plannedEpisodeCount",
        "targetEpisodeSeconds",
        "aspectRatio",
        "outputWidth",
        "outputHeight",
        "visualStyle",
        "artDirection",
        "characterDesign",
        "colorPalette",
        "cameraLanguage",
        "soundStrategy",
        "imagePromptPrefix",
        "videoPromptModel"
    ];

    public async Task<ProjectSettingsView> ReadAsync(
        Guid projectId,
        CancellationToken cancellationToken) =>
        await queryDispatcher.QueryAsync(
            new GetProjectSettingsQuery(projectId),
            cancellationToken)
        ?? throw new KeyNotFoundException("项目不存在。");

    public async Task<ProjectSettingsView> UpdateAsync(
        Guid projectId,
        string changesJson,
        CancellationToken cancellationToken)
    {
        Dictionary<string, JsonElement> changes;
        try
        {
            changes = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(changesJson)
                ?? [];
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException("项目设定补丁必须是有效的 JSON 对象。", error);
        }

        if (changes.Count == 0)
        {
            throw new InvalidOperationException("项目设定补丁不能为空。");
        }
        var unknownFields = changes.Keys.Where(field => !AllowedFields.Contains(field)).ToArray();
        if (unknownFields.Length > 0)
        {
            throw new InvalidOperationException($"不允许修改字段：{string.Join("、", unknownFields)}。");
        }

        var current = await ReadAsync(projectId, cancellationToken);
        var result = await commandDispatcher.SendAsync(
            new SaveProjectSettingsCommand(
                projectId,
                ReadString(changes, "projectName", current.ProjectName),
                ReadString(changes, "description", current.Description),
                ReadString(changes, "contentType", current.ContentType),
                ReadString(changes, "targetAudience", current.TargetAudience),
                ReadInt(changes, "plannedEpisodeCount", current.PlannedEpisodeCount),
                ReadInt(changes, "targetEpisodeSeconds", current.TargetEpisodeSeconds),
                ReadString(changes, "aspectRatio", current.AspectRatio),
                ReadInt(changes, "outputWidth", current.OutputWidth),
                ReadInt(changes, "outputHeight", current.OutputHeight),
                ReadString(changes, "visualStyle", current.VisualStyle),
                ReadString(changes, "artDirection", current.ArtDirection),
                ReadString(changes, "characterDesign", current.CharacterDesign),
                ReadString(changes, "colorPalette", current.ColorPalette),
                ReadString(changes, "cameraLanguage", current.CameraLanguage),
                ReadString(changes, "soundStrategy", current.SoundStrategy),
                ReadString(changes, "imagePromptPrefix", current.ImagePromptPrefix),
                ReadString(changes, "videoPromptModel", current.VideoPromptModel)),
            cancellationToken);

        return result.Status switch
        {
            SaveProjectSettingsStatus.Success => result.Settings!,
            SaveProjectSettingsStatus.NotFound => throw new KeyNotFoundException("项目不存在。"),
            _ => throw new InvalidOperationException(string.Join(
                " ",
                result.Errors.SelectMany(error => error.Value)))
        };
    }

    private static string? ReadString(
        IReadOnlyDictionary<string, JsonElement> changes,
        string field,
        string currentValue)
    {
        if (!changes.TryGetValue(field, out var value))
        {
            return currentValue;
        }
        if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            throw new InvalidOperationException($"字段 {field} 必须是字符串。");
        }
        return value.GetString();
    }

    private static int ReadInt(
        IReadOnlyDictionary<string, JsonElement> changes,
        string field,
        int currentValue)
    {
        if (!changes.TryGetValue(field, out var value))
        {
            return currentValue;
        }
        if (!value.TryGetInt32(out var result))
        {
            throw new InvalidOperationException($"字段 {field} 必须是整数。");
        }
        return result;
    }
}
