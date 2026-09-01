using System.Text.Json;
using AlexDirectorConsole.V2.Api.Features.Agents;
using AlexDirectorConsole.V2.Api.Features.Projects.Generation;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Storyboard;

public sealed record ShotVideoPromptCharacterContext(
    Guid AssetId,
    Guid ResourceId,
    string Name,
    string Summary,
    string VisualDescription,
    IReadOnlyList<string> MustKeep,
    IReadOnlyList<string> Avoid,
    Guid? VoiceProfileAssetId,
    string? VoiceName,
    string? VoiceDesignPrompt,
    string? VoiceLanguage,
    int? VoiceSeed);

public sealed record ShotVideoPromptAgentInput(
    string ProjectName,
    string? VideoPromptModel,
    string VisualStyle,
    string ArtDirection,
    string CameraLanguage,
    string SoundStrategy,
    double DurationSeconds,
    string ShotSize,
    string CameraAngle,
    string CameraMovement,
    string Composition,
    string VisualDescription,
    string Action,
    string Dialogue,
    string Sound,
    string FirstFrameDescription,
    string LastFrameDescription,
    string CutDescription,
    IReadOnlyList<ShotVideoPromptCharacterContext> Characters,
    string? Instruction,
    string DialogueCharacter = "");

public sealed record ShotVideoPromptDraft(
    string VisualMotionPrompt,
    string VoicePerformancePrompt,
    string SoundPrompt,
    string ContinuityNotes);

public interface IShotVideoPromptAgent
{
    Task<ShotVideoPromptDraft> GenerateAsync(
        ShotVideoPromptAgentInput input,
        CancellationToken cancellationToken);
}

#pragma warning disable MAAI001
public sealed class MafShotVideoPromptAgent(
    V2DbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    ILoggerFactory loggerFactory) : IShotVideoPromptAgent
{
    public async Task<ShotVideoPromptDraft> GenerateAsync(
        ShotVideoPromptAgentInput input,
        CancellationToken cancellationToken)
    {
        var configuration = await dbContext.FoundryConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (!LlmChatClientFactory.IsConfigured(configuration))
            throw new ProjectGenerationConfigurationException("请先在系统设置中配置语言模型。");
        var instructions = await BuiltInAgentPromptLoader.LoadAsync(
            dbContext,
            BuiltInAgents.VideoPromptDirectorId,
            cancellationToken);

        var agent = LlmChatClientFactory.Create(configuration!, dataProtectionProvider)
            .AsIChatClient()
            .AsHarnessAgent(new HarnessAgentOptions
            {
                Name = "AlexMiniMaxH3PromptDirector",
                MaxContextWindowTokens = 1_050_000,
                MaxOutputTokens = 4_096,
                MaximumIterationsPerRequest = 2,
                DisableFileMemory = true,
                DisableWebSearch = true,
                DisableTodoProvider = true,
                DisableAgentModeProvider = true,
                DisableAgentSkillsProvider = true,
                ChatOptions = new ChatOptions
                {
                    Instructions = instructions,
                    MaxOutputTokens = 4_096
                }
            }, loggerFactory);
        var response = await agent.RunAsync(
            $"为这个镜头生成 MiniMax H3 提示词草案：\n{JsonSerializer.Serialize(input, StoryboardDefaults.JsonOptions)}",
            cancellationToken: cancellationToken);
        var text = response.Text?.Trim() ?? string.Empty;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidOperationException("GPT-5.4 未返回 JSON 视频提示词。");
        var draft = JsonSerializer.Deserialize<ShotVideoPromptDraft>(
            text[start..(end + 1)],
            StoryboardDefaults.JsonOptions)
            ?? throw new InvalidOperationException("GPT-5.4 未返回有效视频提示词。");
        if (string.IsNullOrWhiteSpace(draft.VisualMotionPrompt)
            || string.IsNullOrWhiteSpace(draft.VoicePerformancePrompt)
            || string.IsNullOrWhiteSpace(draft.SoundPrompt)
            || string.IsNullOrWhiteSpace(draft.ContinuityNotes))
            throw new InvalidOperationException("Agent 返回的视频提示词缺少必填内容。");
        return draft;
    }
}
#pragma warning restore MAAI001

public static class ShotVideoPromptInstructions
{
    internal const string DefaultModel = "minimax-h3-fl2va";
    internal const string MiniMaxH3Model = "minimax-h3";

    public static bool UsesMiniMaxH3Format(string? model) => Normalize(model) == MiniMaxH3Model;

    private static string Normalize(string? model) => model?.Trim().ToLowerInvariant() switch
    {
        "minimax-h3" or "minimax h3" or "hailuo-h3" or "hailuo h3" => MiniMaxH3Model,
        _ => DefaultModel
    };
}