using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace AlexDirectorConsole.V2.Api.Features.Copilot;

#pragma warning disable MAAI001

public sealed record CopilotHistoryMessage(string Role, string Content);

public sealed record CopilotAgentReply(string Content, string Model, string Runtime);

public interface IProjectCopilotAgent
{
    Task<CopilotAgentReply> ReplyAsync(
        Guid projectId,
        string projectName,
        string page,
        string episode,
        IReadOnlyList<CopilotHistoryMessage> history,
        string message,
        CancellationToken cancellationToken);
}

public sealed class CopilotConfigurationException(string message) : InvalidOperationException(message);

public sealed class MafProjectCopilotAgent(
    V2DbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    IWebHostEnvironment environment,
    IProjectCoverService coverService,
    IProjectSettingsToolService settingsToolService,
    ILoggerFactory loggerFactory) : IProjectCopilotAgent
{
    public async Task<CopilotAgentReply> ReplyAsync(
        Guid projectId,
        string projectName,
        string page,
        string episode,
        IReadOnlyList<CopilotHistoryMessage> history,
        string message,
        CancellationToken cancellationToken)
    {
        var configuration = await dbContext.FoundryConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (configuration is null
            || string.IsNullOrWhiteSpace(configuration.Endpoint)
            || string.IsNullOrWhiteSpace(configuration.ProtectedApiKey))
        {
            throw new CopilotConfigurationException("请先在系统设置中配置 Azure AI Foundry。");
        }

        var protector = dataProtectionProvider.CreateProtector("FoundryApiKeys.v1");
        var apiKey = protector.Unprotect(configuration.ProtectedApiKey);
        var enabledSkills = await dbContext.SkillDefinitions
            .AsNoTracking()
            .Where(skill => skill.IsEnabled)
            .Select(skill => skill.SourcePath)
            .ToListAsync(cancellationToken);
        var skillsRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "Skills"));
        var skillPaths = enabledSkills
            .Select(sourcePath => Path.GetDirectoryName(sourcePath.Replace('/', Path.DirectorySeparatorChar)))
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => Path.GetFullPath(Path.Combine(skillsRoot, directory!)))
            .Where(path => path.StartsWith(skillsRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var skillsProvider = new AgentSkillsProvider(
            skillPaths,
            scriptRunner: null,
            fileOptions: null,
            options: new AgentSkillsProviderOptions
            {
                DisableLoadSkillApproval = true,
                DisableReadSkillResourceApproval = true
            },
            loggerFactory);
        var generateProjectCover = AIFunctionFactory.Create(
            (Func<string?, CancellationToken, Task<string>>)(async (instruction, toolCancellationToken) =>
                JsonSerializer.Serialize(
                    await coverService.GenerateAsync(projectId, instruction, toolCancellationToken),
                    JsonSerializerOptions.Web)),
            name: "generate_project_cover",
            description: "根据当前项目已保存的创作设定调用 gpt-image-2 生成或重新生成项目概念封面，并保存为版本化资产。instruction 是导演对本次生成的可选意见。仅在导演明确要求生成封面时调用。");
        var readProjectSettings = AIFunctionFactory.Create(
            (Func<CancellationToken, Task<string>>)(async toolCancellationToken =>
                JsonSerializer.Serialize(
                    await settingsToolService.ReadAsync(projectId, toolCancellationToken),
                    JsonSerializerOptions.Web)),
            name: "read_project_settings",
            description: "读取当前项目完整且已保存的项目设定，包括当前版本。回答或修改设定前必须先调用。");
        var updateProjectSettings = AIFunctionFactory.Create(
            (Func<string, CancellationToken, Task<string>>)(async (changesJson, toolCancellationToken) =>
                JsonSerializer.Serialize(
                    await settingsToolService.UpdateAsync(projectId, changesJson, toolCancellationToken),
                    JsonSerializerOptions.Web)),
            name: "update_project_settings",
            description: "用 JSON 对象补丁更新项目设定并保存为新版本。只传需要修改的 camelCase 字段；未传字段保持不变。修改前必须先调用 read_project_settings。可修改字段包括 projectName、description、contentType、targetAudience、plannedEpisodeCount、targetEpisodeSeconds、aspectRatio、outputWidth、outputHeight、visualStyle、artDirection、protagonistSpecies、characterDesign、colorPalette、cameraLanguage、soundStrategy、imagePromptPrefix。");
        var agent = AzureFoundryChatClientFactory
            .Create(configuration.Endpoint, configuration.Deployment, apiKey)
            .AsIChatClient()
            .AsHarnessAgent(
                new HarnessAgentOptions
                {
                    Name = "AlexProjectCopilot",
                    MaxContextWindowTokens = 1_050_000,
                    MaxOutputTokens = 8_192,
                    MaximumIterationsPerRequest = 32,
                    DisableFileMemory = true,
                    DisableWebSearch = true,
                    DisableTodoProvider = true,
                    DisableAgentModeProvider = true,
                    DisableAgentSkillsProvider = true,
                    AIContextProviders = [skillsProvider],
                    ChatOptions = new ChatOptions
                    {
                        Instructions = $$"""
                            你是 Alex 导演台中项目“{{projectName}}”的右侧副驾驶。
                            当前页面是“{{page}}”，当前生产集是“{{episode}}”。
                            用户是导演，拥有最终决定权。请使用简洁中文回答，优先给出可执行建议。
                            专业任务与已公布 Skill 匹配时，先加载对应 Skill，再遵循其约束。
                            已注册 read_project_settings、update_project_settings 和 generate_project_cover 工具。
                            讨论或修改项目设定时，必须先读取当前设定。导演明确要求修改并保存时，直接调用更新工具创建新版本，保持未要求修改的字段不变。
                            导演明确要求生成或重新生成项目封面时，必须调用封面工具。只有工具成功返回后才能报告操作完成。
                            """,
                        MaxOutputTokens = 8_192
                        ,Tools = [readProjectSettings, updateProjectSettings, generateProjectCover]
                    }
                },
                loggerFactory);

        var messages = history
            .TakeLast(40)
            .Select(item => new AIChatMessage(
                item.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                    ? ChatRole.Assistant
                    : ChatRole.User,
                item.Content))
            .Append(new AIChatMessage(ChatRole.User, message));
        var response = await agent.RunAsync(messages, cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(response.Text))
        {
            throw new InvalidOperationException("GPT-5.4 未返回可显示的回复。");
        }

        return new CopilotAgentReply(
            response.Text.Trim(),
            configuration.Deployment,
            "MAF HarnessAgent");
    }
}
#pragma warning restore MAAI001