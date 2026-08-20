using System.Text.Json;
using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Agents;
using AlexDirectorConsole.V2.Api.Features.Projects.CreateProject;
using AlexDirectorConsole.V2.Api.Features.Projects.ManageProject;
using AlexDirectorConsole.V2.Api.Features.Projects.Queries;
using AlexDirectorConsole.V2.Api.Features.Skills;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;
using AlexDirectorConsole.V2.Database.Data;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace AlexDirectorConsole.V2.Api.Features.Sessions;

#pragma warning disable MAAI001
public sealed class MafSessionAgent(
    V2DbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    IWebHostEnvironment environment,
    ISkillCatalog skillCatalog,
    ICommandDispatcher commandDispatcher,
    IQueryDispatcher queryDispatcher,
    ILoggerFactory loggerFactory) : ISessionAgent
{
    public async Task<SessionAgentReply> ReplyAsync(
        AgentView agentDefinition,
        SessionAgentContext context,
        IReadOnlyList<SessionHistoryMessage> history,
        string message,
        CancellationToken cancellationToken)
    {
        var configuration = await dbContext.FoundryConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (!LlmChatClientFactory.IsConfigured(configuration))
        {
            throw new SessionsConfigurationException("请先在系统设置中配置语言模型。");
        }

        var enabledSkills = await (
            from link in dbContext.AgentSkills.AsNoTracking()
            join skill in dbContext.SkillDefinitions.AsNoTracking() on link.SkillId equals skill.Id
            where link.AgentId == agentDefinition.Id && skill.IsEnabled
            select new { skill.Id, skill.SourcePath })
            .ToArrayAsync(cancellationToken);
        var skillsRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "Skills"));
        var skillPaths = enabledSkills
            .Select(skill => Path.GetDirectoryName(skill.SourcePath.Replace('/', Path.DirectorySeparatorChar)))
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

        var allowedToolNames = enabledSkills
            .Select(skill => skillCatalog.Get(skill.Id))
            .Where(metadata => metadata is not null)
            .SelectMany(metadata => metadata!.AllowedTools)
            .ToHashSet(StringComparer.Ordinal);
        var tools = CreateProjectTools(context)
            .Where(item => allowedToolNames.Contains(item.Name))
            .Select(item => item.Function)
            .ToArray();
        var projectContext = context.ProjectId is Guid projectId
            ? $"当前项目：{context.ProjectName}（{projectId:D}）。"
            : "当前没有固定项目；需要操作单个项目时，先查询并确认项目 ID。";
        var runtimeAgent = LlmChatClientFactory
            .Create(configuration!, dataProtectionProvider)
            .AsIChatClient()
            .AsHarnessAgent(
                new HarnessAgentOptions
                {
                    Name = "AlexAssistantDirector",
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
                            {{agentDefinition.SystemPrompt}}
                            {{projectContext}}
                            当前页面：{{context.Page}}。当前生产集：{{context.Episode}}。
                            用户是导演，拥有最终决定权。使用简洁中文回答。
                            专业任务与已绑定 Skill 匹配时，先加载对应 Skill，再严格遵循其工作流和工具边界。
                            只能根据工具的真实返回报告执行结果。没有注册项目删除工具；删除项目必须提示用户在项目中心手动操作。
                            """,
                        MaxOutputTokens = 8_192,
                        Tools = tools
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
        var response = await runtimeAgent.RunAsync(messages, cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(response.Text))
        {
            throw new InvalidOperationException("Agent 未返回可显示的回复。");
        }

        return new SessionAgentReply(
            response.Text.Trim(),
            LlmChatClientFactory.GetModel(configuration!),
            "MAF HarnessAgent");
    }

    private IReadOnlyList<ProjectTool> CreateProjectTools(SessionAgentContext context)
    {
        var listProjects = AIFunctionFactory.Create(
            (Func<CancellationToken, Task<string>>)(async toolCancellationToken =>
                Serialize(new
                {
                    status = "ok",
                    projects = await queryDispatcher.QueryAsync(new ListProjectsQuery(), toolCancellationToken)
                })),
            name: "list_projects",
            description: "列出全部项目及其 ID、名称、描述和更新时间。选择项目或回答项目列表问题时调用。");
        var readProject = AIFunctionFactory.Create(
            (Func<string?, CancellationToken, Task<string>>)(async (projectId, toolCancellationToken) =>
            {
                var resolved = ResolveProjectId(projectId, context.ProjectId);
                if (resolved is null) return InvalidProjectId();
                var project = await queryDispatcher.QueryAsync(
                    new GetProjectQuery(resolved.Value),
                    toolCancellationToken);
                return project is null
                    ? Serialize(new { status = "not-found", error = "项目不存在。" })
                    : Serialize(new { status = "ok", project });
            }),
            name: "read_project",
            description: "按项目 ID 读取项目。projectId 可省略，此时使用当前 Session 绑定的项目。更新项目前必须调用。");
        var createProject = AIFunctionFactory.Create(
            (Func<string, string?, CancellationToken, Task<string>>)(async (
                name,
                description,
                toolCancellationToken) =>
            {
                var result = await commandDispatcher.SendAsync(
                    new CreateProjectCommand(name, description),
                    toolCancellationToken);
                return result.IsSuccess
                    ? Serialize(new { status = "created", project = result.Project })
                    : Serialize(new { status = "invalid", errors = result.Errors });
            }),
            name: "create_project",
            description: "创建项目。name 必填，description 可选。只有工具返回 created 后才能声称创建成功。");
        var updateProject = AIFunctionFactory.Create(
            (Func<string?, string?, string?, CancellationToken, Task<string>>)(async (
                projectId,
                name,
                description,
                toolCancellationToken) =>
            {
                var resolved = ResolveProjectId(projectId, context.ProjectId);
                if (resolved is null) return InvalidProjectId();
                var current = await queryDispatcher.QueryAsync(
                    new GetProjectQuery(resolved.Value),
                    toolCancellationToken);
                if (current is null) return Serialize(new { status = "not-found", error = "项目不存在。" });
                if (name is null && description is null)
                {
                    return Serialize(new { status = "invalid", error = "至少提供 name 或 description。" });
                }

                var result = await commandDispatcher.SendAsync(
                    new UpdateProjectCommand(
                        resolved.Value,
                        name ?? current.Name,
                        description ?? current.Description),
                    toolCancellationToken);
                if (result.NotFound) return Serialize(new { status = "not-found", error = "项目不存在。" });
                return result.IsSuccess
                    ? Serialize(new { status = "updated", project = result.Project })
                    : Serialize(new { status = "invalid", errors = result.Errors });
            }),
            name: "update_project",
            description: "更新项目名称和/或描述。projectId 可省略以使用当前项目；省略的字段保持不变。更新前必须先调用 read_project。只有工具返回 updated 后才能声称更新成功。");

        return
        [
            new("list_projects", listProjects),
            new("read_project", readProject),
            new("create_project", createProject),
            new("update_project", updateProject)
        ];
    }

    private static Guid? ResolveProjectId(string? value, Guid? contextualProjectId)
    {
        if (string.IsNullOrWhiteSpace(value)) return contextualProjectId;
        return Guid.TryParse(value, out var projectId) ? projectId : null;
    }

    private static string InvalidProjectId() =>
        Serialize(new { status = "invalid", error = "必须提供有效的项目 ID。" });

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, JsonSerializerOptions.Web);

    private sealed record ProjectTool(string Name, AIFunction Function);
}
#pragma warning restore MAAI001
