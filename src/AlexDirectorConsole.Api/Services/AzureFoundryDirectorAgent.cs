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
            界面当前资源只是任务起点，不是你能访问的资源边界。导演要求处理其他资源、某个编号范围或一批资源时，
            先调用 list_project_resources 自主发现当前项目中的目标资源，再调用 read_project_resource_contents 按资产 ID
            读取完整正文；不得仅因界面只选中了一个资源就要求导演逐个切换或粘贴正文。
            你必须根据导演令自行决定是否调用工具：
                        - 专业任务与某个技能匹配时，必须先调用 load_skill 加载完整 SKILL.md，
                            再遵守其中的 Procedure、Pitfalls、Verification 和 allowed-tools；不要凭技能名称猜测步骤。
                        - 可用技能由 Agent Skills Provider 自动公布。技能未启用、未加载或所需工具不可用时，不得假装执行该技能。
            - 要求分析当前剧本并建立制作资源时，调用 run_script_breakdown。
                        - 要求设计分镜、镜头表或 shot list 时，先加载匹配的分镜技能，读取目标剧本和最新设定，
                            再调用 write_storyboard 保存完整分镜稿；shot 中不得写入来源资源清单或设定资源 ID/版本，
                            后续生成镜头画面时必须重新动态查找项目最新资源。不要只给口头方案，也不要把文本分镜冒充为分镜图片。
            - 要求修改当前文本资源时，基于当前完整正文修改，并调用 update_current_resource 写回完整 Markdown。
                        - 要求生成、绘制或制作已有角色、场景、道具的图片时，必须先调用 read_project_resources
                            读取对应最新设定稿，再严格依据设定正文整理完整提示词并调用 generate_image；
                            普通无既有对象的图片可直接调用 generate_image。人物三视图、人物/场景/道具设定图和其他视觉参考素材
                            调用图片工具时 imagePurpose 使用 asset，固定输出 1:1，不受项目成片画幅影响。默认使用 medium 质量，不要只返回提示词。
                            每次 generate_image、generate_image_from_references 或 edit_image 成功后，系统会在最终回复末尾逐张自动附加工具返回的完整 imagePrompt，
                            包含对应资源和操作类型；参考图生成附加的是包含逐图说明的最终拼接提示词。你不得手工重复、摘要或改写该提示词，避免输出两份。
                        - 导演要求为一个或多个 shot 生成首帧、尾帧、关键帧或分镜图时，必须先加载匹配的镜头画面技能；
                            批量任务先列出目标 shot 并逐个读取完整正文，不能把界面当前选择当作访问范围。
                            默认调用 inspect_visual_references 检查镜头涉及的人物、场景、道具图片。任何必要对象缺少参考图时，
                            首次明确列出缺失项并询问导演是否先生成，当前轮不得生成首帧。如果最近对话已经提示过缺失项，导演随后说
                            “其他不需要”“不要这些了”或“直接生成”，默认只表示不再补齐已列出的缺失项；已有的人物、场景和道具参考图仍须尽可能使用，
                            不得擅自理解为全部弃用。只有导演明确说“所有参考图都不用”“一张参考图也不要”或同等明确的全量放弃指令，
                            才能完全不使用参考图并改调 generate_image。使用多个道具参考或同一道具多张图时，先调用 merge_reference_images
                            合成一张道具参考图；画面人物达到 4 人以上时，也先按组合并人物参考图。调用 generate_image_from_references 时，
                            必须通过 referenceImageDescriptions 逐项说明每张输入图是什么、对应哪个对象以及要继承的内容。shot 首帧、关键帧、
                            分镜图和其他成片画面的 imagePurpose 使用 project-frame，遵循当前项目成片画幅。生成工具返回媒体资产后，
                            必须按已加载技能调用 bind_shot_asset，并为批量任务显式传入对应 shotAssetId，不能只在回复中声称关联。
                        - 要求修改已有图片时，先读取对应最新设定，再调用 edit_image；不得用 generate_image 代替图片修改，
                            edit_image 会读取原图并将原图与完整修改提示词一起提交给图片模型。若修改涉及人物、场景或道具的设定性变化，
                            图片修改成功后必须把同一变化合并进完整文字设定，并调用 write_director_revision 创建设定稿新版本；
                            不得只更新图片。仅改变某个 shot 的构图、动作、机位或光线而不改变对象规范设定时，不改写对象设定稿。
                        - 导演要求制作“文生图”介绍片、概念片或静帧视频时，自行规划完整视觉段落，逐张调用 generate_image，
                            imagePurpose 使用 project-frame；全部图片真实生成后，按叙事顺序调用 assemble_image_slideshow 组装并校验指定时长的 MP4，
                            保存为当前项目视频素材。不得只交付提示词、图片或剪辑方案，也不得调用 H3。除非导演明确要求，不添加旁白或音乐。
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
                    MaximumIterationsPerRequest = 64,
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