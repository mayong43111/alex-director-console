using System.Text.Json;
using AlexDirectorConsole.Api.Models;

namespace AlexDirectorConsole.Api.Contracts;

public sealed record SendMessageRequest(
    string Message,
    string? Model,
    string? SkillId = null,
    Guid? AssetId = null,
    string? ProjectAspectRatio = null,
    string? ProjectResolution = null,
    string? ImageSize = null,
    string? ProjectName = null,
    string? ProjectDescription = null,
    string? PreviewResolution = null,
    string? ImageModel = null,
    string? VideoModel = null);

public sealed record ConversationMessageResponse(
    Guid Id,
    Guid ProjectId,
    string Role,
    string Content,
    string Model,
    DateTime CreatedAtUtc,
    IReadOnlyList<AssetResponse> GeneratedAssets)
{
    public static ConversationMessageResponse FromMessage(
        ConversationMessage message,
        IReadOnlyList<AssetResponse>? generatedAssets = null) => new(
        message.Id,
        message.ProjectId,
        message.Role,
        message.Content,
        message.Model,
        message.CreatedAtUtc,
        generatedAssets ?? []);

    public static IReadOnlyList<Guid> GetGeneratedAssetIds(ConversationMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.GeneratedAssetIdsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Guid[]>(message.GeneratedAssetIdsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

public sealed record SendMessageResponse(
    ConversationMessageResponse UserMessage,
    ConversationMessageResponse AssistantMessage,
    SkillRunResponse? SkillRun = null,
    AssetResponse? OutputAsset = null);

public sealed record SkillDefinitionResponse(
    string Id,
    string Name,
    string Description,
    string Version,
    bool IsEnabled,
    bool IsSystem,
    string Title,
    IReadOnlyList<string> AllowedTools,
    string Content);

public sealed record UpdateSkillRequest(bool IsEnabled);

public sealed record SkillRunResponse(
    Guid Id,
    Guid ProjectId,
    string SkillId,
    Guid InputAssetId,
    Guid? OutputAssetId,
    string Status,
    string DirectorInstruction,
    string Model,
    string? ResultJson,
    string? Error,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc)
{
    public static SkillRunResponse FromRun(SkillRun run) => new(
        run.Id,
        run.ProjectId,
        run.SkillId,
        run.InputAssetId,
        run.OutputAssetId,
        run.Status,
        run.DirectorInstruction,
        run.Model,
        run.ResultJson,
        run.Error,
        run.StartedAtUtc,
        run.CompletedAtUtc);
}