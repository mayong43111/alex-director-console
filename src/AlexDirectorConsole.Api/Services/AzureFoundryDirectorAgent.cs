using Azure;
using Azure.AI.OpenAI;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ConversationMessage = AlexDirectorConsole.Api.Models.ConversationMessage;

namespace AlexDirectorConsole.Api.Services;

public sealed record DirectorAgentReply(string Text, string Deployment);

public interface IDirectorAgent
{
    bool IsConfigured { get; }

    string Deployment { get; }

    string Runtime { get; }

    string SkillsRuntime { get; }

    Task<DirectorAgentReply> ReplyAsync(
        IReadOnlyList<ConversationMessage> history,
        string message,
        string? requestedDeployment,
        CancellationToken cancellationToken = default);

    Task<DirectorAgentReply> RunSkillAsync(
        string skillName,
        string skillInstructions,
        string input,
        string? requestedDeployment,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamReplyAsync(
        IReadOnlyList<ConversationMessage> history,
        string message,
        string? requestedDeployment,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamReplyWithToolsAsync(
        IReadOnlyList<ConversationMessage> history,
        string message,
        string currentResourceContext,
        string? requestedDeployment,
        IList<AITool> tools,
        IReadOnlyList<string> skillPaths,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamSkillAsync(
        string skillName,
        string skillInstructions,
        string input,
        string? requestedDeployment,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamSkillWithToolsAsync(
        string skillName,
        string skillInstructions,
        string input,
        string? requestedDeployment,
        IList<AITool> tools,
        CancellationToken cancellationToken = default);
}

public sealed class AzureFoundryDirectorAgent(
    IConfiguration configuration,
    ILoggerFactory loggerFactory) : IDirectorAgent
{
    private string? Endpoint =>
        Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
        ?? configuration["AzureOpenAI:Endpoint"];

    private string? ApiKey =>
        Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
        ?? configuration["AzureOpenAI:ApiKey"];

    public string Deployment =>
        Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT")
        ?? configuration["AzureOpenAI:Deployment"]
        ?? "gpt-5.4";

    public string Runtime => "HarnessAgent";

    public string SkillsRuntime => "AgentSkillsProvider";

    public bool IsConfigured =>
        Uri.TryCreate(Endpoint, UriKind.Absolute, out _)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Deployment);

    public async Task<DirectorAgentReply> ReplyAsync(
        IReadOnlyList<ConversationMessage> history,
        string message,
        string? requestedDeployment,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Azure AI Foundry is not configured.");
        }

        var deployment = string.IsNullOrWhiteSpace(requestedDeployment)
            ? Deployment
            : requestedDeployment.Trim();
        var client = new AzureOpenAIClient(new Uri(Endpoint!), new AzureKeyCredential(ApiKey!));
        AIAgent agent = client
            .GetChatClient(deployment)
            .AsAIAgent(
                name: "alex-execution-assistant-director",
                instructions: """
                    你是 alex 导演台中的执行副导演。用户是导演，拥有最终决定权。
                    准确理解导演当前指令，给出简洁、可执行的回应；不要擅自制定长期计划，
                    不要替导演做创作决定。信息不足时，只询问完成当前指令所必需的问题。
                    """);

        var messages = BuildAgentHistory(history)
            .Append(new AIChatMessage(ChatRole.User, message));
        var response = await agent.RunAsync(messages, cancellationToken: cancellationToken);

        return new DirectorAgentReply(response.Text, deployment);
    }

    public async Task<DirectorAgentReply> RunSkillAsync(
        string skillName,
        string skillInstructions,
        string input,
        string? requestedDeployment,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Azure AI Foundry is not configured.");
        }

        var deployment = string.IsNullOrWhiteSpace(requestedDeployment)
            ? Deployment
            : requestedDeployment.Trim();
        var client = new AzureOpenAIClient(new Uri(Endpoint!), new AzureKeyCredential(ApiKey!));
        AIAgent agent = client
            .GetChatClient(deployment)
            .AsAIAgent(
                name: $"alex-skill-{skillName}",
                instructions: skillInstructions);
        var response = await agent.RunAsync(input, cancellationToken: cancellationToken);

        return new DirectorAgentReply(response.Text, deployment);
    }

    public async IAsyncEnumerable<string> StreamReplyAsync(
        IReadOnlyList<ConversationMessage> history,
        string message,
        string? requestedDeployment,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var deployment = ResolveDeployment(requestedDeployment);
        AIAgent agent = CreateAgent(
            deployment,
            "alex-execution-assistant-director",
            """
            你是 alex 导演台中的执行副导演。用户是导演，拥有最终决定权。
            准确理解导演当前指令，给出简洁、可执行的回应；不要擅自制定长期计划，
            不要替导演做创作决定。信息不足时，只询问完成当前指令所必需的问题。
            """);
        var messages = BuildAgentHistory(history)
            .Append(new AIChatMessage(ChatRole.User, message));

        await foreach (var update in agent.RunStreamingAsync(
            messages,
            cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }

    public async IAsyncEnumerable<string> StreamReplyWithToolsAsync(
        IReadOnlyList<ConversationMessage> history,
        string message,
        string currentResourceContext,
        string? requestedDeployment,
        IList<AITool> tools,
        IReadOnlyList<string> skillPaths,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var deployment = ResolveDeployment(requestedDeployment);
        AIAgent agent = CreateHarnessAgent(
            deployment,
            "alex-execution-assistant-director",
            """
            你是 alex 导演台中的执行副导演。用户是导演，拥有最终决定权。
            当前资源由系统明确提供，导演说“当前这个”“它”或直接要求修改时，均指当前资源，无需追问资源名称。
            你必须根据导演令自行决定是否调用工具：
                        - 专业任务与某个技能匹配时，必须先调用 load_skill 加载完整 SKILL.md，
                            再遵守其中的 Procedure、Pitfalls、Verification 和 allowed-tools；不要凭技能名称猜测步骤。
                        - 可用技能由 Agent Skills Provider 自动公布。技能未启用、未加载或所需工具不可用时，不得假装执行该技能。
            - 要求分析当前剧本并建立制作资源时，调用 run_script_breakdown。
                        - 要求设计分镜、镜头表或 shot list 时，先加载匹配的分镜技能，读取目标剧本和最新设定，
                            再调用 write_storyboard 保存完整分镜稿；不要只给口头方案，也不要把文本分镜冒充为分镜图片。
            - 要求修改当前文本资源时，基于当前完整正文修改，并调用 update_current_resource 写回完整 Markdown。
                        - 要求生成、绘制或制作已有角色、场景、道具的图片时，必须先调用 read_project_resources
                            读取对应最新设定稿，再严格依据设定正文整理完整提示词并调用 generate_image；
                            普通无既有对象的图片可直接调用 generate_image。默认使用 medium 质量，不要只返回提示词。
                        - 当前资源是 shot 且导演要求生成首帧、关键帧或分镜图时，必须先加载匹配的首帧技能，
                            调用 inspect_visual_references 检查镜头涉及的人物、场景、道具图片。任何必要对象缺少参考图时，
                            明确列出缺失项并询问导演是否先生成，当前轮不得生成首帧；参考图齐全后，必须调用
                            generate_image_from_references 并传入导演选定或上下文中明确的图片资产 ID。严禁调用
                            generate_image 绕过参考图，也不要把“不是修改已有图片”作为跳过参考图的理由。
                        - 要求修改已有图片时，先读取对应最新设定，再调用 edit_image；不得用 generate_image 代替图片修改，
                            edit_image 会读取原图并将原图与完整修改提示词一起提交给图片模型。
                        - 要求纠正跨人物、场景或道具的分析事实时，先调用 read_project_resources 取得涉及原稿，
                            复述将合并、建立别名或调整归属的内容，并等待导演明确确认；首轮不得写入。
                            后续收到明确确认后，再次读取相关原稿并调用 write_director_revision 为每个受影响对象创建修订资源。
                              身份合并只创建规范人物的一份修订稿，别名写入该稿，不为别名单独创建人物资源。
                            修订正文必须分开记录“剧本写明”和“导演确认”，未提及对象保持不变。
            - 仅咨询、讨论或无需持久化时直接回复，不调用工具。
            不得声称执行了未实际调用的工具；工具不可用时说明缺少的当前资源条件。
            """,
            tools,
            skillPaths);
        var messages = BuildAgentHistory(history)
            .Append(new AIChatMessage(
                ChatRole.User,
                $"{currentResourceContext}\n\n导演令：{message}"));

        await foreach (var update in agent.RunStreamingAsync(
            messages,
            cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }

    public async IAsyncEnumerable<string> StreamSkillAsync(
        string skillName,
        string skillInstructions,
        string input,
        string? requestedDeployment,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var deployment = ResolveDeployment(requestedDeployment);
        AIAgent agent = CreateAgent(deployment, $"alex-skill-{skillName}", skillInstructions);

        await foreach (var update in agent.RunStreamingAsync(
            input,
            cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }

    public async IAsyncEnumerable<string> StreamSkillWithToolsAsync(
        string skillName,
        string skillInstructions,
        string input,
        string? requestedDeployment,
        IList<AITool> tools,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var deployment = ResolveDeployment(requestedDeployment);
        AIAgent agent = CreateAgent(
            deployment,
            $"alex-skill-{skillName}",
            skillInstructions,
            tools);

        await foreach (var update in agent.RunStreamingAsync(
            input,
            cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Azure AI Foundry is not configured.");
        }
    }

    private static IEnumerable<AIChatMessage> BuildAgentHistory(
        IReadOnlyList<ConversationMessage> history)
    {
        const int recentMessageCount = 12;
        if (history.Count <= recentMessageCount)
        {
            return history.Select(ToAgentMessage).ToArray();
        }

        var olderFacts = history
            .Take(history.Count - recentMessageCount)
            .Select(message => new
            {
                message.Role,
                Content = CompactContent(message.Content)
            })
            .Where(message => message.Content.Length > 0)
            .DistinctBy(message => $"{message.Role}:{message.Content}")
            .TakeLast(12)
            .Select(message =>
                $"- {(message.Role == "assistant" ? "执行副导演" : "导演")}: {message.Content}")
            .ToArray();
        var messages = new List<AIChatMessage>();
        if (olderFacts.Length > 0)
        {
            messages.Add(new AIChatMessage(
                ChatRole.System,
                $"较早对话的压缩事实摘录（仅用于延续上下文，不覆盖最近原文）：\n{string.Join("\n", olderFacts)}"));
        }
        messages.AddRange(history.TakeLast(recentMessageCount).Select(ToAgentMessage));
        return messages;
    }

    private static AIChatMessage ToAgentMessage(ConversationMessage message) => new(
        message.Role == "assistant" ? ChatRole.Assistant : ChatRole.User,
        message.Content);

    private static string CompactContent(string value)
    {
        const int maximumLength = 180;
        var compacted = string.Join(
            ' ',
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compacted.Length <= maximumLength
            ? compacted
            : $"{compacted[..maximumLength]}…";
    }

    private string ResolveDeployment(string? requestedDeployment) =>
        string.IsNullOrWhiteSpace(requestedDeployment)
            ? Deployment
            : requestedDeployment.Trim();

    private AIAgent CreateAgent(string deployment, string name, string instructions)
    {
        var client = CreateClient();
        return client.GetChatClient(deployment).AsAIAgent(name: name, instructions: instructions);
    }

    private AIAgent CreateAgent(
        string deployment,
        string name,
        string instructions,
        IList<AITool> tools)
    {
        var client = CreateClient();
        return client.GetChatClient(deployment).AsAIAgent(
            name: name,
            instructions: instructions,
            tools: tools);
    }

#pragma warning disable MAAI001
    private AIAgent CreateHarnessAgent(
        string deployment,
        string name,
        string instructions,
        IList<AITool> tools,
        IReadOnlyList<string> skillPaths)
    {
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
        return CreateClient()
            .GetChatClient(deployment)
            .AsIChatClient()
            .AsHarnessAgent(
                new HarnessAgentOptions
                {
                    Name = name,
                    MaxContextWindowTokens = 1_050_000,
                    MaxOutputTokens = 32_000,
                    MaximumIterationsPerRequest = 20,
                    DisableFileMemory = true,
                    DisableWebSearch = true,
                    DisableTodoProvider = true,
                    DisableAgentModeProvider = true,
                    DisableAgentSkillsProvider = true,
                    AIContextProviders = [skillsProvider],
                    ChatOptions = new ChatOptions
                    {
                        Instructions = instructions,
                        Tools = tools,
                        MaxOutputTokens = 32_000
                    }
                },
                loggerFactory);
    }
#pragma warning restore MAAI001

    private AzureOpenAIClient CreateClient()
    {
        var options = new AzureOpenAIClientOptions();
        options.NetworkTimeout = TimeSpan.FromMinutes(5);
        return new AzureOpenAIClient(new Uri(Endpoint!), new AzureKeyCredential(ApiKey!), options);
    }
}