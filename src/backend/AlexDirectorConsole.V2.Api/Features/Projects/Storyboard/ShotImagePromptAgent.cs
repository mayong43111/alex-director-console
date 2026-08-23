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

public sealed record ShotImagePromptReferenceContext(string Kind, string Name);

public sealed record ShotImagePromptAgentInput(
    string ImageProvider,
    string ImageModel,
    string FrameStage,
    string ProjectName,
    string VisualStyle,
    string ArtDirection,
    string CharacterDesign,
    string ColorPalette,
    string CameraLanguage,
    string ImageConstraints,
    int OutputWidth,
    int OutputHeight,
    int SceneNumber,
    int ShotNumber,
    double DurationSeconds,
    string ShotSize,
    string CameraAngle,
    string CameraMovement,
    string Composition,
    string VisualDescription,
    string Action,
    string FirstFrameDescription,
    string LastFrameDescription,
    string CutDescription,
    IReadOnlyList<string> NarrativeHooks,
    IReadOnlyList<ShotImagePromptReferenceContext> References,
    IReadOnlyList<string> ImportantProps,
    string? Instruction);

public sealed record ShotImagePromptDraft(string Prompt);

public interface IShotImagePromptAgent
{
    Task<ShotImagePromptDraft> GenerateAsync(
        ShotImagePromptAgentInput input,
        CancellationToken cancellationToken);
}

#pragma warning disable MAAI001
public sealed class MafShotImagePromptAgent(
    V2DbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    ILoggerFactory loggerFactory) : IShotImagePromptAgent
{
    public async Task<ShotImagePromptDraft> GenerateAsync(
        ShotImagePromptAgentInput input,
        CancellationToken cancellationToken)
    {
        var configuration = await dbContext.FoundryConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (!LlmChatClientFactory.IsConfigured(configuration))
            throw new ProjectGenerationConfigurationException("请先在系统设置中配置语言模型。");
        var instructions = await BuiltInAgentPromptLoader.LoadAsync(
            dbContext,
            BuiltInAgents.ImagePromptDirectorId,
            cancellationToken);

        var agent = LlmChatClientFactory.Create(configuration!, dataProtectionProvider)
            .AsIChatClient()
            .AsHarnessAgent(new HarnessAgentOptions
            {
                Name = "AlexImagePromptDirector",
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
            $"为目标图片模型生成这个分镜帧的提示词：\n{JsonSerializer.Serialize(input, StoryboardDefaults.JsonOptions)}",
            cancellationToken: cancellationToken);
        var text = response.Text?.Trim() ?? string.Empty;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidOperationException("图片提示词 Agent 未返回 JSON。");
        var draft = JsonSerializer.Deserialize<ShotImagePromptDraft>(
            text[start..(end + 1)],
            StoryboardDefaults.JsonOptions)
            ?? throw new InvalidOperationException("图片提示词 Agent 未返回有效提示词。");
        if (string.IsNullOrWhiteSpace(draft.Prompt))
            throw new InvalidOperationException("图片提示词 Agent 返回的提示词为空。");
        return draft with { Prompt = draft.Prompt.Trim() };
    }
}
#pragma warning restore MAAI001