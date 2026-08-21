using System.Text.Json;
using AlexDirectorConsole.V2.Api.Features.Projects.Generation;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;
using AlexDirectorConsole.V2.Database.Data;
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
    string? Instruction);

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
                    Instructions = """
                        你是 MiniMax H3 FL2VA 视频与原生配音提示词导演。根据完整分镜、角色设定和角色音色设定，编写极其紧凑、可执行的英文生成指令。所有字段必须只使用英文，不得输出中文字符。
                        不得改变剧情、镜头时长、人物身份、动作顺序或对白。对白由程序逐字锁定，你只负责说明谁说话、中文发音、语气、节奏、音高、年龄感、情绪、停连和口型同步；不得在输出中复述、翻译或改写对白。
                        若有多个角色，必须依据分镜明确唯一说话者，禁止其他角色说话或动嘴。音色描述必须忠实使用输入的 voiceName、voiceDesignPrompt、voiceLanguage 和 voiceSeed，不得臆造冲突特征。
                        visualMotionPrompt 只描述无声的可见动作、图标出现顺序与摄影机调度，不得出现 speak、speech、voice、dialogue、deliver、address、mouth、lip、word、text、subtitle、caption 或其变体，也不得描述人物何时开口或说完。voicePerformancePrompt 只描述声音表演，不得复述音色名称、seed、中文音色原文或对白。soundPrompt 只描述对白以外的环境声和混音。continuityNotes 只描述身份、服装、空间、光线与轴线连续性。
                        每个字段最多 600 个英文字符。用户补充要求不能覆盖分镜事实或对白。不要输出任何禁止项或负向指令，这些规则由程序另行强制。
                        只返回 JSON，不要 Markdown。结构：
                        {"visualMotionPrompt":"...","voicePerformancePrompt":"...","soundPrompt":"...","continuityNotes":"..."}
                        """,
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