using System.Text.Json;
using System.Reflection;
using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Agents;
using AlexDirectorConsole.V2.Api.Features.Projects.CreateProject;
using AlexDirectorConsole.V2.Api.Features.Projects.Assets;
using AlexDirectorConsole.V2.Api.Features.Projects.ManageProject;
using AlexDirectorConsole.V2.Api.Features.Projects.Queries;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.Projects.Sources;
using AlexDirectorConsole.V2.Api.Features.Projects.Storyboard;
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
    ISkillCatalog skillCatalog,
    ICommandDispatcher commandDispatcher,
    IQueryDispatcher queryDispatcher,
    IProjectSettingsToolService projectSettingsToolService,
    IStoryProductionToolService storyProductionToolService,
    IVisualAssetProductionToolService visualAssetProductionToolService,
    IStoryboardMediaBatchService storyboardMediaBatchService,
    SessionAgentExecutionContext executionContext,
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
        var skillInstructions = enabledSkills
            .Select(skill => skillCatalog.Get(skill.Id))
            .Where(skill => skill is not null)
            .Select(skill => $"## Skill: {skill!.Title}\n{skill.Content}")
            .ToArray();

        var allowedToolNames = enabledSkills
            .Select(skill => skillCatalog.Get(skill.Id))
            .Where(metadata => metadata is not null)
            .SelectMany(metadata => metadata!.AllowedTools)
            .ToHashSet(StringComparer.Ordinal);
        var tools = CreateProjectTools(context)
            .Where(item => allowedToolNames.Contains(item.Name))
            .Select(item => (AIFunction)new ReportingAIFunction(item.Function, executionContext))
            .ToArray();
        var requiredToolName = GetRequiredToolName(history, message, allowedToolNames);
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
                    ChatOptions = new ChatOptions
                    {
                        Instructions = $$"""
                            {{agentDefinition.SystemPrompt}}
                            {{string.Join("\n\n", skillInstructions)}}
                            {{projectContext}}
                            当前页面：{{context.Page}}。当前生产集：{{context.Episode}}。
                            用户是导演，拥有最终决定权。使用简洁中文回答。
                            专业任务与已绑定 Skill 匹配时，先加载对应 Skill，再严格遵循其工作流和工具边界。
                            只能根据工具的真实返回报告执行结果。没有注册项目删除工具；删除项目必须提示用户在项目中心手动操作。
                            """,
                        MaxOutputTokens = 8_192,
                            Tools = tools,
                        ToolMode = requiredToolName is not null
                            ? ChatToolMode.RequireSpecific(requiredToolName)
                                : ChatToolMode.Auto
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

    internal static string? GetRequiredToolName(
        IReadOnlyList<SessionHistoryMessage> history,
        string message,
        IReadOnlySet<string> allowedToolNames)
    {
        var previous = history.LastOrDefault();
        if (previous?.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) != true)
        {
            return null;
        }

        if (allowedToolNames.Contains("create_project")
            && previous.Content.Contains("请直接回复我一个项目名称", StringComparison.Ordinal)
            && message.Trim().Length is > 0 and <= 100)
        {
            return "create_project";
        }

        var isContinuation = message.Trim() is "继续" or "继续生成首帧";
        if (isContinuation
            && allowedToolNames.Contains("generate_next_storyboard_first_frame")
            && previous.Content.Contains("剩余未生成首帧", StringComparison.Ordinal))
        {
            return "generate_next_storyboard_first_frame";
        }

        return null;
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
        var readProjectSettings = AIFunctionFactory.Create(
            (Func<string?, CancellationToken, Task<string>>)(async (projectId, toolCancellationToken) =>
            {
                var resolved = ResolveProjectId(projectId, context.ProjectId);
                if (resolved is null) return InvalidProjectId();
                try
                {
                    var settings = await projectSettingsToolService.ReadAsync(
                        resolved.Value,
                        toolCancellationToken);
                    return Serialize(new { status = "ok", settings });
                }
                catch (KeyNotFoundException error)
                {
                    return Serialize(new { status = "not-found", error = error.Message });
                }
            }),
            name: "read_project_settings",
            description: "读取项目当前完整设定及版本。projectId 可省略，此时使用当前 Session 绑定的项目。更新项目设定前必须调用。");
        var updateProjectSettings = AIFunctionFactory.Create(
            (Func<string?, string, CancellationToken, Task<string>>)(async (
                projectId,
                changesJson,
                toolCancellationToken) =>
            {
                var resolved = ResolveProjectId(projectId, context.ProjectId);
                if (resolved is null) return InvalidProjectId();
                try
                {
                    var settings = await projectSettingsToolService.UpdateAsync(
                        resolved.Value,
                        changesJson,
                        toolCancellationToken);
                    return Serialize(new { status = "updated", settings });
                }
                catch (KeyNotFoundException error)
                {
                    return Serialize(new { status = "not-found", error = error.Message });
                }
                catch (InvalidOperationException error)
                {
                    return Serialize(new { status = "invalid", error = error.Message });
                }
            }),
            name: "update_project_settings",
            description: "用 JSON 对象补丁更新当前项目设定并创建新版本。projectId 可省略；changesJson 只填写需修改字段。只有工具返回 updated 后才能声称更新成功。");
        var createStorySource = AIFunctionFactory.Create(
            (Func<string?, string, string?, string, CancellationToken, Task<string>>)(async (
                projectId,
                title,
                description,
                content,
                toolCancellationToken) =>
            {
                var resolved = ResolveProjectId(projectId, context.ProjectId);
                if (resolved is null) return InvalidProjectId();
                try
                {
                    var source = await storyProductionToolService.CreateStorySourceAsync(
                        resolved.Value,
                        title,
                        description,
                        content,
                        toolCancellationToken);
                    return Serialize(new { status = "created", source });
                }
                catch (KeyNotFoundException error)
                {
                    return Serialize(new { status = "not-found", error = error.Message });
                }
                catch (InvalidOperationException error)
                {
                    return Serialize(new { status = "invalid", error = error.Message });
                }
            }),
            name: "create_story_source",
            description: "把已写好的故事保存为项目原文来源。content 应使用 Markdown 标题划分原分集，例如 '# 第一集 标题'。只有返回 created 后才能声称故事已保存。");
        var generateSourceEpisodeScript = AIFunctionFactory.Create(
            (Func<string?, string, int, CancellationToken, Task<string>>)(async (
                projectId,
                sourceResourceId,
                episodeNumber,
                toolCancellationToken) =>
            {
                var resolvedProject = ResolveProjectId(projectId, context.ProjectId);
                if (resolvedProject is null) return InvalidProjectId();
                if (!Guid.TryParse(sourceResourceId, out var resolvedSource))
                {
                    return Serialize(new { status = "invalid", error = "必须提供有效的故事来源 ID。" });
                }
                try
                {
                    var result = await storyProductionToolService.GenerateSourceEpisodeScriptAsync(
                        resolvedProject.Value,
                        resolvedSource,
                        episodeNumber,
                        toolCancellationToken);
                    return Serialize(new { status = "generated", result });
                }
                catch (KeyNotFoundException error)
                {
                    return Serialize(new { status = "not-found", error = error.Message });
                }
                catch (InvalidOperationException error)
                {
                    return Serialize(new { status = "invalid", error = error.Message });
                }
            }),
            name: "generate_source_episode_script",
            description: "按故事来源的原分集直接生成指定集正式剧本。固定使用 source-chapters，不做素材分析，不重新编排或生成新大纲。只有返回 generated 后才能声称正式剧本已生成。");
        var buildVisualAssets = AIFunctionFactory.Create(
            (Func<string?, CancellationToken, Task<string>>)(async (projectId, toolCancellationToken) =>
            {
                var resolved = ResolveProjectId(projectId, context.ProjectId);
                if (resolved is null) return InvalidProjectId();
                try
                {
                    var result = await visualAssetProductionToolService.BuildFromCurrentScriptsAsync(
                        resolved.Value,
                        toolCancellationToken);
                    return Serialize(new { status = "completed", result });
                }
                catch (KeyNotFoundException error)
                {
                    return Serialize(new { status = "not-found", error = error.Message });
                }
                catch (InvalidOperationException error)
                {
                    return Serialize(new { status = "invalid", error = error.Message });
                }
            }),
            name: "build_visual_assets",
            description: "从当前正式剧本建立人物、场景和道具资产；同名同类资产会跳过。只有返回 completed 后才能报告建立成功。");
        var generateMissingVisualPrompts = AIFunctionFactory.Create(
            (Func<string?, string, CancellationToken, Task<string>>)(async (
                projectId,
                kind,
                toolCancellationToken) =>
            {
                var resolved = ResolveProjectId(projectId, context.ProjectId);
                if (resolved is null) return InvalidProjectId();
                try
                {
                    var result = await visualAssetProductionToolService.GenerateMissingPromptsAsync(
                        resolved.Value,
                        kind,
                        toolCancellationToken);
                    return Serialize(new { status = result.Failed == 0 ? "completed" : "partial", result });
                }
                catch (InvalidOperationException error)
                {
                    return Serialize(new { status = "invalid", error = error.Message });
                }
            }),
            name: "generate_missing_visual_prompts",
            description: "为指定资产类型批量生成缺失的参考图提示词。kind 只能是 character、scene 或 prop。返回真实生成、跳过和失败数量。");
        var generateMissingVisualImages = AIFunctionFactory.Create(
            (Func<string?, string, int?, CancellationToken, Task<string>>)(async (
                projectId,
                kind,
                maxItems,
                toolCancellationToken) =>
            {
                var resolved = ResolveProjectId(projectId, context.ProjectId);
                if (resolved is null) return InvalidProjectId();
                try
                {
                    var result = await visualAssetProductionToolService.GenerateMissingImagesAsync(
                        resolved.Value,
                        kind,
                        maxItems ?? 1,
                        toolCancellationToken);
                    return Serialize(new { status = result.Failed == 0 ? "completed" : "partial", result });
                }
                catch (InvalidOperationException error)
                {
                    return Serialize(new { status = "invalid", error = error.Message });
                }
            }),
            name: "generate_missing_visual_images",
            description: "为指定资产类型分步生成缺失参考图。kind 只能是 character、scene 或 prop；maxItems 默认为 1、最多 3。返回 generated、alreadyPresent、failed、remaining 和 errors；remaining 大于 0 时在同一后台任务中继续调用。失败或收到停止信号时终止。");
        var generateStoryboard = AIFunctionFactory.Create(
            (Func<string?, string, CancellationToken, Task<string>>)(async (
                projectId,
                productionEpisodeId,
                toolCancellationToken) =>
            {
                var resolvedProject = ResolveProjectId(projectId, context.ProjectId);
                if (resolvedProject is null) return InvalidProjectId();
                if (!Guid.TryParse(productionEpisodeId, out var resolvedEpisode))
                    return Serialize(new { status = "invalid", error = "必须提供有效的生产集 ID。" });
                try
                {
                    var result = await commandDispatcher.SendAsync(
                        new GenerateStoryboardCommand(resolvedProject.Value, resolvedEpisode),
                        toolCancellationToken);
                    return result is null
                        ? Serialize(new { status = "not-found" })
                        : Serialize(new
                        {
                            status = "generated",
                            result.ProductionEpisodeId,
                            result.Revision,
                            result.TotalDurationSeconds,
                            shotCount = result.Shots.Count
                        });
                }
                catch (InvalidOperationException error)
                {
                    return Serialize(new { status = "invalid", error = error.Message });
                }
            }),
            name: "generate_storyboard",
            description: "从指定生产集的当前正式剧本生成并保存完整分镜。只有返回 generated 后才能报告分镜已完成。");
        var generateMissingStoryboardImagePrompts = AIFunctionFactory.Create(
            (Func<string?, string, CancellationToken, Task<string>>)(async (
                projectId,
                productionEpisodeId,
                toolCancellationToken) =>
            {
                var resolvedProject = ResolveProjectId(projectId, context.ProjectId);
                if (resolvedProject is null) return InvalidProjectId();
                if (!Guid.TryParse(productionEpisodeId, out var resolvedEpisode))
                    return Serialize(new { status = "invalid", error = "必须提供有效的生产集 ID。" });
                var result = await storyboardMediaBatchService.GenerateMissingImagePromptsAsync(
                    resolvedProject.Value,
                    resolvedEpisode,
                    toolCancellationToken);
                return Serialize(new { status = result.Failed == 0 ? "completed" : "partial", result });
            }),
            name: "generate_missing_storyboard_image_prompts",
            description: "为生产集全部分镜补齐缺失的首帧图片提示词，不生成图片或视频。返回 generated、skipped、failed 和 errors。");
        var generateNextStoryboardFirstFrame = AIFunctionFactory.Create(
            (Func<string?, string, CancellationToken, Task<string>>)(async (
                projectId,
                productionEpisodeId,
                toolCancellationToken) =>
            {
                var resolvedProject = ResolveProjectId(projectId, context.ProjectId);
                if (resolvedProject is null) return InvalidProjectId();
                if (!Guid.TryParse(productionEpisodeId, out var resolvedEpisode))
                    return Serialize(new { status = "invalid", error = "必须提供有效的生产集 ID。" });
                try
                {
                    var storyboard = await queryDispatcher.QueryAsync(
                        new GetStoryboardQuery(resolvedProject.Value, resolvedEpisode),
                        toolCancellationToken);
                    if (storyboard is null) return Serialize(new { status = "not-found" });
                    var pending = storyboard.Shots
                        .Where(shot => shot.Production?.OutputAssetId is null)
                        .OrderBy(shot => shot.SceneNumber)
                        .ThenBy(shot => shot.ShotNumber)
                        .ToArray();
                    if (pending.Length == 0)
                    {
                        return Serialize(new
                        {
                            status = "completed",
                            generated = 0,
                            alreadyPresent = storyboard.Shots.Count,
                            remaining = 0
                        });
                    }
                    var shot = pending[0];
                    if (shot.ImagePrompt is null)
                        return Serialize(new { status = "invalid", error = "下一镜缺少首帧图片提示词，请先补齐提示词。" });
                    var production = await commandDispatcher.SendAsync(
                        new StartShotProductionCommand(
                            resolvedProject.Value,
                            resolvedEpisode,
                            shot.ResourceId,
                            shot.ImagePrompt.Prompt,
                            shot.ImagePrompt.Instruction,
                            FirstFrameOnly: true),
                        toolCancellationToken);
                    return production?.OutputAssetId is null
                        ? Serialize(new { status = "partial", generated = 0, alreadyPresent = storyboard.Shots.Count - pending.Length, remaining = pending.Length, error = "首帧未生成。" })
                        : Serialize(new
                        {
                            status = "completed",
                            generated = 1,
                            alreadyPresent = storyboard.Shots.Count - pending.Length,
                            remaining = pending.Length - 1,
                            sceneNumber = shot.SceneNumber,
                            shotNumber = shot.ShotNumber,
                            firstFrameUrl = production.OutputUrl
                        });
                }
                catch (InvalidOperationException error)
                {
                    return Serialize(new { status = "invalid", error = error.Message });
                }
            }),
            name: "generate_next_storyboard_first_frame",
            description: "每次只为下一条缺失分镜生成首帧图片，绝不创建视频任务。返回 generated、alreadyPresent、remaining 和错误；remaining 大于 0 时继续自动调用。");
        var refreshFrontend = AIFunctionFactory.Create(
            (Func<string>)(() => Serialize(new { status = "ok", action = "refresh-frontend" })),
            name: "refresh_frontend",
            description: "完成项目数据写入后调用，通知当前浏览器刷新并显示最新数据。只读操作不调用。");

        return
        [
            new("list_projects", listProjects),
            new("read_project", readProject),
            new("create_project", createProject),
            new("update_project", updateProject),
            new("read_project_settings", readProjectSettings),
            new("update_project_settings", updateProjectSettings),
            new("create_story_source", createStorySource),
            new("generate_source_episode_script", generateSourceEpisodeScript),
            new("build_visual_assets", buildVisualAssets),
            new("generate_missing_visual_prompts", generateMissingVisualPrompts),
            new("generate_missing_visual_images", generateMissingVisualImages),
            new("generate_storyboard", generateStoryboard),
            new("generate_missing_storyboard_image_prompts", generateMissingStoryboardImagePrompts),
            new("generate_next_storyboard_first_frame", generateNextStoryboardFirstFrame),
            new("refresh_frontend", refreshFrontend)
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

    private sealed class ReportingAIFunction(
        AIFunction inner,
        SessionAgentExecutionContext executionContext) : AIFunction
    {
        public override string Name => inner.Name;
        public override string Description => inner.Description;
        public override JsonElement JsonSchema => inner.JsonSchema;
        public override JsonSerializerOptions JsonSerializerOptions => inner.JsonSerializerOptions;
        public override MethodInfo? UnderlyingMethod => inner.UnderlyingMethod;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            await executionContext.PublishToolAsync(
                "tool-started",
                Name,
                $"正在执行工具：{Name}",
                cancellationToken);
            try
            {
                var result = await inner.InvokeAsync(arguments, cancellationToken);
                await executionContext.PublishToolAsync(
                    "tool-completed",
                    Name,
                    $"工具已完成：{Name}",
                    cancellationToken);
                return result;
            }
            catch
            {
                await executionContext.PublishToolAsync(
                    "tool-failed",
                    Name,
                    $"工具执行失败：{Name}",
                    CancellationToken.None);
                throw;
            }
        }
    }
}
#pragma warning restore MAAI001
