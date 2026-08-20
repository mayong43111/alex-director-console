using System.Text.Json;
using AlexDirectorConsole.V2.Api.Features.Projects.Generation;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;
using AlexDirectorConsole.V2.Database.Data;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.V2.Api.Features.Agents;

public sealed record AgentTextInvocation(
    AgentView Agent,
    string Input,
    JsonElement Context,
    int? MaxLength);

public sealed record AgentTextInvocationResult(
    string Value,
    string Model,
    string Runtime);

public interface IAgentTextInvoker
{
    Task<AgentTextInvocationResult> InvokeAsync(
        AgentTextInvocation invocation,
        CancellationToken cancellationToken);
}

#pragma warning disable MAAI001
public sealed class MafAgentTextInvoker(
    V2DbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    IWebHostEnvironment environment,
    ILoggerFactory loggerFactory) : IAgentTextInvoker
{
    public async Task<AgentTextInvocationResult> InvokeAsync(
        AgentTextInvocation invocation,
        CancellationToken cancellationToken)
    {
        var configuration = await dbContext.FoundryConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (!LlmChatClientFactory.IsConfigured(configuration))
        {
            throw new ProjectGenerationConfigurationException("请先在系统设置中配置语言模型。");
        }

        var sourcePaths = await (
            from link in dbContext.AgentSkills.AsNoTracking()
            join skill in dbContext.SkillDefinitions.AsNoTracking() on link.SkillId equals skill.Id
            where link.AgentId == invocation.Agent.Id && skill.IsEnabled
            select skill.SourcePath)
            .ToListAsync(cancellationToken);
        var skillsRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "Skills"));
        var skillPaths = sourcePaths
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
        var lengthInstruction = invocation.MaxLength is int maxLength
            ? $"候选正文不得超过 {maxLength} 个字符。"
            : string.Empty;
        var agent = LlmChatClientFactory
            .Create(configuration!, dataProtectionProvider)
            .AsIChatClient()
            .AsHarnessAgent(
                new HarnessAgentOptions
                {
                    Name = "AlexConfiguredTextAgent",
                    MaxContextWindowTokens = 1_050_000,
                    MaxOutputTokens = 4_096,
                    MaximumIterationsPerRequest = 8,
                    DisableFileMemory = true,
                    DisableWebSearch = true,
                    DisableTodoProvider = true,
                    DisableAgentModeProvider = true,
                    DisableAgentSkillsProvider = true,
                    AIContextProviders = [skillsProvider],
                    ChatOptions = new ChatOptions
                    {
                        Instructions = $$"""
                            {{invocation.Agent.SystemPrompt}}
                            输出将作为多行文本字段的候选内容。只返回候选正文，不要解释、标题、Markdown 围栏或 JSON。
                            {{lengthInstruction}}
                            """,
                        MaxOutputTokens = 4_096
                    }
                },
                loggerFactory);

        var contextJson = invocation.Context.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? "{}"
            : invocation.Context.GetRawText();
        var response = await agent.RunAsync(
            $$"""
            当前文本：
            {{invocation.Input}}

            附加上下文（JSON）：
            {{contextJson}}
            """,
            cancellationToken: cancellationToken);
        var value = response.Text?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            throw new InvalidOperationException("Agent 未返回可用内容。");
        }
        if (invocation.MaxLength is int limit && value.Length > limit)
        {
            throw new InvalidOperationException($"Agent 返回内容超过 {limit} 个字符。");
        }

        return new(value, LlmChatClientFactory.GetModel(configuration!), "MAF HarnessAgent");
    }
}
#pragma warning restore MAAI001