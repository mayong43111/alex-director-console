using System.Text.Json;
using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Projects.Assets;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.Projects.Sources;
using AlexDirectorConsole.V2.Api.Features.Projects.Storyboard;
using AlexDirectorConsole.V2.Api.Features.Projects.Voice;
using AlexDirectorConsole.V2.Api.Features.Sessions;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Generation;

public static class GenerationTaskTypes
{
    public const string ProjectCover = "project-cover";
    public const string ProjectCoverPreview = "project-cover-preview";
    public const string ProjectSettingsAssist = "project-settings-assist";
    public const string ProjectDescriptionAssist = "project-description-assist";
    public const string VisualReferencePrompt = "visual-reference-prompt";
    public const string VisualReferenceImage = "visual-reference-image";
    public const string VisualReferencePromptBatch = "visual-reference-prompt-batch";
    public const string VisualReferenceImageBatch = "visual-reference-image-batch";
    public const string StoryboardImagePrompt = "storyboard-image-prompt";
    public const string StoryboardImagePreview = "storyboard-image-preview";
    public const string StoryboardImage = "storyboard-image";
    public const string StoryboardVideoPrompt = "storyboard-video-prompt";
    public const string StoryboardVideo = "storyboard-video";
    public const string ShotVideoPreview = "shot-video-preview";
    public const string StoryboardImagePromptBatch = "storyboard-image-prompt-batch";
    public const string StoryboardImageBatch = "storyboard-image-batch";
    public const string StoryboardVideoPromptBatch = "storyboard-video-prompt-batch";
    public const string StoryboardVideoBatch = "storyboard-video-batch";
    public const string VoiceProfile = "voice-profile";
    public const string ProductionScript = "production-script";
    public const string StoryMaterialAssets = "story-material-assets";
    public const string StoryboardDesign = "storyboard-design";

    public static bool IsSupported(string taskType) => taskType is
        ProjectCover or ProjectCoverPreview or ProjectSettingsAssist or ProjectDescriptionAssist or
        VisualReferencePrompt or VisualReferenceImage or
        VisualReferencePromptBatch or VisualReferenceImageBatch or
        StoryboardImagePrompt or StoryboardImagePreview or StoryboardImage or StoryboardVideoPrompt or StoryboardVideo or ShotVideoPreview or
        StoryboardImagePromptBatch or StoryboardImageBatch or
        StoryboardVideoPromptBatch or StoryboardVideoBatch or VoiceProfile or ProductionScript or StoryMaterialAssets or StoryboardDesign;
}

public sealed record GenerationTaskPayload(
    Guid ProjectId,
    Guid? ProductionEpisodeId = null,
    Guid? ResourceId = null,
    string? Instruction = null,
    string? ConfirmedPrompt = null,
    string? PreviewHash = null,
    bool UseCurrentReference = false,
    string? Kind = null,
    string? RequestJson = null,
    Guid? SourceResourceId = null,
    int? EpisodeNumber = null,
    IReadOnlyList<Guid>? ResourceIds = null);

public sealed record GenerationTaskView(
    Guid Id,
    string TaskType,
    string Status,
    string? CurrentStep,
    string? LastError,
    int ProgressCompleted,
    int? ProgressTotal,
    string? ResultJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public interface IGenerationTaskScheduler
{
    Task<GenerationTaskView> EnqueueAsync(
        string taskType,
        string intent,
        GenerationTaskPayload payload,
        CancellationToken cancellationToken);
}

public sealed class GenerationTaskScheduler(
    V2DbContext dbContext,
    IBackgroundJobClient backgroundJobs,
    TimeProvider timeProvider) : IGenerationTaskScheduler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GenerationTaskView> EnqueueAsync(
        string taskType,
        string intent,
        GenerationTaskPayload payload,
        CancellationToken cancellationToken)
    {
        if (!GenerationTaskTypes.IsSupported(taskType))
        {
            throw new ArgumentOutOfRangeException(nameof(taskType), taskType, "不支持的生成任务类型。");
        }

        var now = timeProvider.GetUtcNow();
        var task = new AgentTask
        {
            ProjectId = payload.ProjectId == Guid.Empty ? null : payload.ProjectId,
            ProductionEpisodeId = payload.ProductionEpisodeId,
            Intent = intent,
            TaskType = taskType,
            ContextSnapshotJson = JsonSerializer.Serialize(payload, JsonOptions),
            Status = "queued",
            CurrentStep = "等待 Hangfire 执行",
            ProgressTotal = 1,
            RequestedBy = "generation-api",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.AgentTasks.Add(task);
        dbContext.AgentTaskEvents.Add(new AgentTaskEvent
        {
            TaskId = task.Id,
            Sequence = 1,
            EventType = "status",
            Stage = "queued",
            Message = "生成任务已进入 Hangfire 队列。",
            CreatedAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        var jobId = backgroundJobs.Enqueue<GenerationTaskJob>(
            job => job.ExecuteAsync(task.Id, CancellationToken.None));
        task.PlanJson = JsonSerializer.Serialize(new { hangfireJobId = jobId }, JsonOptions);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToView(task);
    }

    internal static GenerationTaskView ToView(AgentTask task, string? resultJson = null) => new(
        task.Id,
        task.TaskType,
        task.Status,
        task.CurrentStep,
        task.LastError,
        task.ProgressCompleted,
        task.ProgressTotal,
        resultJson,
        task.CreatedAtUtc,
        task.UpdatedAtUtc,
        task.CompletedAtUtc);
}

public sealed class GenerationTaskJob(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<GenerationTaskJob> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(Guid taskId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        var now = timeProvider.GetUtcNow();
        var task = await dbContext.AgentTasks.SingleOrDefaultAsync(item => item.Id == taskId, cancellationToken);
        if (task is null
            || !GenerationTaskTypes.IsSupported(task.TaskType)
            || task.Status is "completed" or "cancelled"
            || task.LeaseExpiresAtUtc >= now)
        {
            return;
        }
        task.Status = "running";
        task.CurrentStep = task.TaskType switch
        {
            GenerationTaskTypes.ProductionScript => "正在生成正式剧本",
            GenerationTaskTypes.StoryMaterialAssets => "正在分析剧本中的人物、场景与道具",
            GenerationTaskTypes.StoryboardDesign => "正在调用分镜设计模型",
            GenerationTaskTypes.VisualReferencePrompt => "正在调用提示词模型",
            GenerationTaskTypes.VisualReferenceImage => "正在调用图片模型",
            GenerationTaskTypes.VisualReferencePromptBatch => "正在准备批量生成提示词",
            GenerationTaskTypes.VisualReferenceImageBatch => "正在准备批量生成图片",
            _ => "正在生成"
        };
        task.LeaseOwner = workerId;
        task.LeaseExpiresAtUtc = now.AddMinutes(30);
        task.StartedAtUtc ??= now;
        task.LastError = null;
        task.CompletedAtUtc = null;
        task.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        await SessionAgentTaskJob.AppendEventAsync(
            dbContext,
            task.Id,
            "status",
            "running",
            task.CurrentStep,
            null,
            now,
            cancellationToken);
        try
        {
            var payload = JsonSerializer.Deserialize<GenerationTaskPayload>(task.ContextSnapshotJson, JsonOptions)
                ?? throw new InvalidOperationException("生成任务上下文无效。");
            var result = await ExecuteCoreAsync(scope.ServiceProvider, dbContext, task, payload, cancellationToken);
            await dbContext.Entry(task).ReloadAsync(cancellationToken);
            if (task.Status == "cancelled") return;
            var completedAt = timeProvider.GetUtcNow();
            task.Status = "completed";
            task.CurrentStep = "已完成";
            task.ProgressCompleted = task.ProgressTotal ?? 1;
            task.CompletedAtUtc = completedAt;
            task.LastError = null;
            task.UpdatedAtUtc = completedAt;
            task.LeaseOwner = null;
            task.LeaseExpiresAtUtc = null;
            await SessionAgentTaskJob.AppendEventAsync(
                dbContext,
                task.Id,
                "result",
                "completed",
                "生成任务已完成。",
                result is null ? null : JsonSerializer.Serialize(result, JsonOptions),
                completedAt,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            await dbContext.Entry(task).ReloadAsync(CancellationToken.None);
            if (task.Status == "cancelled") return;
            var failedAt = timeProvider.GetUtcNow();
            logger.LogError(error, "Generation task {TaskId} failed.", task.Id);
            task.Status = "failed";
            task.CurrentStep = "执行失败";
            task.LastError = error.Message[..Math.Min(error.Message.Length, 4000)];
            task.CompletedAtUtc = failedAt;
            task.UpdatedAtUtc = failedAt;
            task.LeaseOwner = null;
            task.LeaseExpiresAtUtc = null;
            await SessionAgentTaskJob.AppendEventAsync(
                dbContext, task.Id, "failure", "failed", task.LastError, null, failedAt, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<object?> ExecuteCoreAsync(
        IServiceProvider services,
        V2DbContext dbContext,
        AgentTask task,
        GenerationTaskPayload payload,
        CancellationToken cancellationToken)
    {
        switch (task.TaskType)
        {
            case GenerationTaskTypes.ProjectCover:
                return await services.GetRequiredService<IProjectCoverService>().GenerateConfirmedAsync(
                    payload.ProjectId,
                    payload.Instruction,
                    payload.ConfirmedPrompt ?? throw new InvalidOperationException("缺少已确认的封面提示词。"),
                    payload.PreviewHash ?? throw new InvalidOperationException("缺少封面提示词预览凭据。"),
                    cancellationToken);
            case GenerationTaskTypes.ProjectCoverPreview:
                return await services.GetRequiredService<IProjectCoverService>().PreviewAsync(
                    payload.ProjectId, payload.Instruction, cancellationToken);
            case GenerationTaskTypes.ProjectSettingsAssist:
            case GenerationTaskTypes.ProjectDescriptionAssist:
                var assistRequest = JsonSerializer.Deserialize<ProjectSettingsAssistRequest>(
                    payload.RequestJson ?? throw new InvalidOperationException("缺少 AI 帮写请求。"),
                    JsonOptions) ?? throw new InvalidOperationException("AI 帮写请求无效。");
                return await services.GetRequiredService<IProjectSettingsAssistant>().WriteAsync(assistRequest, cancellationToken);
            case GenerationTaskTypes.VisualReferencePrompt:
                return await services.GetRequiredService<IVisualReferenceService>().GeneratePromptAsync(
                    payload.ProjectId,
                    RequiredResourceId(payload),
                    payload.Instruction,
                    payload.UseCurrentReference,
                    cancellationToken);
            case GenerationTaskTypes.VisualReferenceImage:
                return await services.GetRequiredService<IVisualReferenceService>().GenerateImageAsync(
                    payload.ProjectId, RequiredResourceId(payload), cancellationToken);
            case GenerationTaskTypes.VisualReferencePromptBatch:
                return await services.GetRequiredService<IVisualReferenceService>().GenerateMissingPromptsAsync(
                    payload.ProjectId,
                    payload.Kind ?? string.Empty,
                    cancellationToken,
                    progress => ReportBatchProgressAsync(dbContext, task, progress, "提示词", cancellationToken));
            case GenerationTaskTypes.VisualReferenceImageBatch:
                return await services.GetRequiredService<IVisualReferenceService>().GenerateMissingImagesAsync(
                    payload.ProjectId,
                    payload.Kind ?? string.Empty,
                    cancellationToken,
                    progress => ReportBatchProgressAsync(dbContext, task, progress, "图片", cancellationToken));
            case GenerationTaskTypes.StoryboardDesign:
                return await services.GetRequiredService<ICommandDispatcher>().SendAsync(
                    new GenerateStoryboardCommand(
                        payload.ProjectId,
                        payload.ProductionEpisodeId ?? throw new InvalidOperationException("缺少生产集标识。"),
                        progress => ReportStoryboardDesignProgressAsync(
                            dbContext,
                            task,
                            progress,
                            cancellationToken)),
                    cancellationToken);
            case GenerationTaskTypes.StoryboardImagePrompt:
                return await services.GetRequiredService<IStoryboardMediaPromptService>().GenerateImagePromptAsync(
                    payload.ProjectId, RequiredEpisodeId(payload), RequiredResourceId(payload), payload.Instruction, cancellationToken);
            case GenerationTaskTypes.StoryboardImagePreview:
                return await services.GetRequiredService<IShotFrameService>().PreviewFirstFrameAsync(
                    payload.ProjectId, RequiredEpisodeId(payload), RequiredResourceId(payload), payload.Instruction, cancellationToken);
            case GenerationTaskTypes.StoryboardVideoPrompt:
                return await services.GetRequiredService<IStoryboardMediaPromptService>().GenerateVideoPromptAsync(
                    payload.ProjectId, RequiredEpisodeId(payload), RequiredResourceId(payload), payload.Instruction, cancellationToken);
            case GenerationTaskTypes.StoryboardVideo:
                var videoPromptService = services.GetRequiredService<IStoryboardMediaPromptService>();
                var videoPrompt = await videoPromptService.GetCurrentAsync(
                    payload.ProjectId, RequiredResourceId(payload), StoryboardMediaPromptService.VideoKind, cancellationToken)
                    ?? throw new InvalidOperationException("请先生成视频提示词。");
                var startedVideo = await services.GetRequiredService<IShotVideoService>().StartAsync(
                    payload.ProjectId,
                    RequiredEpisodeId(payload),
                    RequiredResourceId(payload),
                    videoPrompt.Prompt,
                    videoPrompt.PreviewHash ?? throw new InvalidOperationException("当前视频提示词缺少预览校验值，请重新生成。"),
                    videoPrompt.Instruction,
                    cancellationToken)
                    ?? throw new InvalidOperationException("镜头不存在。");
                return await services.GetRequiredService<IShotVideoService>().WaitForCompletionAsync(
                    payload.ProjectId,
                    RequiredEpisodeId(payload),
                    RequiredResourceId(payload),
                    startedVideo.RunId,
                    progress => ReportShotVideoProgressAsync(dbContext, task, progress, cancellationToken),
                    cancellationToken);
            case GenerationTaskTypes.ShotVideoPreview:
                return await services.GetRequiredService<IShotVideoService>().PreviewAsync(
                    payload.ProjectId, RequiredEpisodeId(payload), RequiredResourceId(payload), payload.Instruction, cancellationToken);
            case GenerationTaskTypes.StoryboardImage:
                var promptService = services.GetRequiredService<IStoryboardMediaPromptService>();
                var prompt = await promptService.GetCurrentAsync(
                    payload.ProjectId, RequiredResourceId(payload), StoryboardMediaPromptService.ImageKind, cancellationToken)
                    ?? throw new InvalidOperationException("请先生成图片提示词。");
                return await services.GetRequiredService<ICommandDispatcher>().SendAsync(
                    new StartShotProductionCommand(
                        payload.ProjectId,
                        RequiredEpisodeId(payload),
                        RequiredResourceId(payload),
                        prompt.Prompt,
                        prompt.Instruction),
                    cancellationToken);
            case GenerationTaskTypes.StoryboardImagePromptBatch:
                return await services.GetRequiredService<IStoryboardMediaBatchService>().GenerateMissingImagePromptsAsync(
                    payload.ProjectId, RequiredEpisodeId(payload), payload.ResourceIds, cancellationToken,
                    progress => ReportStoryboardBatchProgressAsync(dbContext, task, progress, cancellationToken));
            case GenerationTaskTypes.StoryboardImageBatch:
                return await services.GetRequiredService<IStoryboardMediaBatchService>().GenerateMissingImagesAsync(
                    payload.ProjectId, RequiredEpisodeId(payload), payload.ResourceIds, cancellationToken,
                    progress => ReportStoryboardBatchProgressAsync(dbContext, task, progress, cancellationToken));
            case GenerationTaskTypes.StoryboardVideoPromptBatch:
                return await services.GetRequiredService<IStoryboardMediaBatchService>().GenerateMissingVideoPromptsAsync(
                    payload.ProjectId, RequiredEpisodeId(payload), payload.ResourceIds, cancellationToken,
                    progress => ReportStoryboardBatchProgressAsync(dbContext, task, progress, cancellationToken));
            case GenerationTaskTypes.StoryboardVideoBatch:
                return await services.GetRequiredService<IStoryboardMediaBatchService>().GenerateMissingVideosAsync(
                    payload.ProjectId, RequiredEpisodeId(payload), payload.ResourceIds, cancellationToken,
                    progress => ReportStoryboardBatchProgressAsync(dbContext, task, progress, cancellationToken));
            case GenerationTaskTypes.VoiceProfile:
                return await services.GetRequiredService<IVoiceProfileService>().GenerateAsync(
                    payload.ProjectId, RequiredResourceId(payload), cancellationToken);
            case GenerationTaskTypes.ProductionScript:
                return await services.GetRequiredService<ICommandDispatcher>().SendAsync(
                    new ConfirmAdaptationScriptCommand(
                        payload.ProjectId,
                        payload.SourceResourceId ?? throw new InvalidOperationException("生成任务缺少原文资料 ID。"),
                        payload.EpisodeNumber ?? throw new InvalidOperationException("生成任务缺少集号。")),
                    cancellationToken);
            case GenerationTaskTypes.StoryMaterialAssets:
                return await services.GetRequiredService<ICommandDispatcher>().SendAsync(
                    new ImportStoryMaterialAssetsCommand(payload.ProjectId),
                    cancellationToken);
            default:
                throw new InvalidOperationException($"不支持的生成任务类型：{task.TaskType}");
        }
    }

    private static async Task ReportBatchProgressAsync(
        V2DbContext dbContext,
        AgentTask task,
        BatchVisualReferenceProgress progress,
        string target,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        task.ProgressCompleted = progress.Completed;
        task.ProgressTotal = progress.Total;
        task.CurrentStep = $"{progress.SubjectName}：{target}{progress.Outcome}";
        task.UpdatedAtUtc = now;
        await SessionAgentTaskJob.AppendEventAsync(
            dbContext,
            task.Id,
            "progress",
            "running",
            $"[{progress.Completed}/{progress.Total}] {task.CurrentStep}",
            JsonSerializer.Serialize(progress, JsonOptions),
            now,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task ReportStoryboardBatchProgressAsync(
        V2DbContext dbContext,
        AgentTask task,
        StoryboardMediaBatchProgress progress,
        CancellationToken cancellationToken)
    {
        await dbContext.Entry(task).ReloadAsync(cancellationToken);
        if (task.Status == "cancelled") throw new OperationCanceledException("批量任务已停止。", cancellationToken);
        var now = DateTimeOffset.UtcNow;
        task.ProgressCompleted = progress.ProgressCompleted ?? progress.Completed;
        task.ProgressTotal = progress.Total;
        task.CurrentStep = $"{progress.ShotCode}：{progress.Outcome}";
        task.UpdatedAtUtc = now;
        await SessionAgentTaskJob.AppendEventAsync(
            dbContext,
            task.Id,
            "progress",
            "running",
            progress.Phase == "submitting"
                ? $"[提交 {progress.Completed}/{progress.Total}] {task.CurrentStep}"
                : $"[{progress.Completed}/{progress.Total}] {task.CurrentStep}",
            JsonSerializer.Serialize(progress, JsonOptions),
            now,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task ReportStoryboardDesignProgressAsync(
        V2DbContext dbContext,
        AgentTask task,
        StoryboardDesignProgress progress,
        CancellationToken cancellationToken)
    {
        await dbContext.Entry(task).ReloadAsync(cancellationToken);
        if (task.Status == "cancelled") throw new OperationCanceledException("分镜任务已停止。", cancellationToken);
        var now = DateTimeOffset.UtcNow;
        task.ProgressCompleted = progress.Completed;
        task.ProgressTotal = progress.Total;
        task.CurrentStep = $"S{progress.SceneNumber:00} · {progress.Heading}：已生成 {progress.ShotCount} 镜";
        task.UpdatedAtUtc = now;
        task.LeaseExpiresAtUtc = now.AddMinutes(30);
        await SessionAgentTaskJob.AppendEventAsync(
            dbContext,
            task.Id,
            "progress",
            "running",
            $"[{progress.Completed}/{progress.Total}] {task.CurrentStep}",
            JsonSerializer.Serialize(progress, JsonOptions),
            now,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task ReportShotVideoProgressAsync(
        V2DbContext dbContext,
        AgentTask task,
        ShotVideoProductionView progress,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        task.CurrentStep = progress.CurrentStage switch
        {
            "queued" => "已加入 Hangfire 队列",
            "validating-capabilities" => "正在检查 ComfyUI 节点与模型",
            "uploading-inputs" => "正在上传首尾帧",
            "polling" => "ComfyUI 正在生成视频",
            "downloading" => "正在下载生成视频",
            "persisting" => "正在保存视频资产",
            "completed" => "视频已生成",
            _ => progress.CurrentStage
        };
        task.UpdatedAtUtc = now;
        await SessionAgentTaskJob.AppendEventAsync(
            dbContext,
            task.Id,
            "progress",
            "running",
            task.CurrentStep,
            JsonSerializer.Serialize(new { progress.RunId, progress.Status, progress.CurrentStage }, JsonOptions),
            now,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static Guid RequiredResourceId(GenerationTaskPayload payload) =>
        payload.ResourceId ?? throw new InvalidOperationException("生成任务缺少资源 ID。");

    private static Guid RequiredEpisodeId(GenerationTaskPayload payload) =>
        payload.ProductionEpisodeId ?? throw new InvalidOperationException("生成任务缺少剧集 ID。");
}

public sealed class GenerationTaskRecoveryJob(
    V2DbContext dbContext,
    IBackgroundJobClient backgroundJobs,
    TimeProvider timeProvider)
{
    [DisableConcurrentExecution(60)]
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var candidates = await dbContext.AgentTasks.AsNoTracking()
            .Where(task => (task.Status == "queued" || task.Status == "running")
                && (task.TaskType == GenerationTaskTypes.ProjectCover
                    || task.TaskType == GenerationTaskTypes.ProjectCoverPreview
                    || task.TaskType == GenerationTaskTypes.ProjectSettingsAssist
                    || task.TaskType == GenerationTaskTypes.ProjectDescriptionAssist
                    || task.TaskType == GenerationTaskTypes.VisualReferencePrompt
                    || task.TaskType == GenerationTaskTypes.VisualReferenceImage
                    || task.TaskType == GenerationTaskTypes.VisualReferencePromptBatch
                    || task.TaskType == GenerationTaskTypes.VisualReferenceImageBatch
                    || task.TaskType == GenerationTaskTypes.StoryboardDesign
                    || task.TaskType == GenerationTaskTypes.StoryboardImagePrompt
                    || task.TaskType == GenerationTaskTypes.StoryboardImagePreview
                    || task.TaskType == GenerationTaskTypes.StoryboardImage
                    || task.TaskType == GenerationTaskTypes.StoryboardVideoPrompt
                    || task.TaskType == GenerationTaskTypes.StoryboardVideo
                    || task.TaskType == GenerationTaskTypes.ShotVideoPreview
                    || task.TaskType == GenerationTaskTypes.StoryboardImagePromptBatch
                    || task.TaskType == GenerationTaskTypes.StoryboardImageBatch
                    || task.TaskType == GenerationTaskTypes.StoryboardVideoPromptBatch
                    || task.TaskType == GenerationTaskTypes.StoryboardVideoBatch
                    || task.TaskType == GenerationTaskTypes.VoiceProfile
                    || task.TaskType == GenerationTaskTypes.ProductionScript
                    || task.TaskType == GenerationTaskTypes.StoryMaterialAssets
                    || task.TaskType == "session-message"))
            .Select(task => new { task.Id, task.TaskType, task.ContextSnapshotJson, task.Status, task.LeaseExpiresAtUtc })
            .Take(500)
            .ToArrayAsync(cancellationToken);
        var tasks = candidates
            .Where(task => task.Status == "queued"
                || task.LeaseExpiresAtUtc is null
                || task.LeaseExpiresAtUtc < now)
            .Take(100);
        foreach (var task in tasks)
        {
            if (task.TaskType == "session-message")
            {
                backgroundJobs.Enqueue<SessionAgentTaskJob>(job => job.ExecuteAsync(task.Id, CancellationToken.None));
            }
            else
            {
                backgroundJobs.Enqueue<GenerationTaskJob>(job => job.ExecuteAsync(task.Id, CancellationToken.None));
            }
        }
    }
}

public static class GenerationTaskEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapGenerationTasks(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/tasks");
        group.MapGet("/{taskId:guid}", async (Guid taskId, V2DbContext dbContext, CancellationToken cancellationToken) =>
        {
            var task = await dbContext.AgentTasks.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == taskId, cancellationToken);
            if (task is null) return Results.NotFound();
            var resultJson = await dbContext.AgentTaskEvents.AsNoTracking()
                .Where(item => item.TaskId == taskId && item.EventType == "result")
                .OrderByDescending(item => item.Sequence)
                .Select(item => item.DataJson)
                .FirstOrDefaultAsync(cancellationToken);
            return Results.Ok(GenerationTaskScheduler.ToView(task, resultJson));
        });
        group.MapPost("/{taskId:guid}/cancel", async (
            Guid taskId,
            V2DbContext dbContext,
            IHttpClientFactory httpClientFactory,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            var task = await dbContext.AgentTasks.SingleOrDefaultAsync(item => item.Id == taskId, cancellationToken);
            if (task is null) return Results.NotFound();
            if (task.Status is "completed" or "failed" or "cancelled")
                return Results.Ok(GenerationTaskScheduler.ToView(task));

            var cancelledAt = timeProvider.GetUtcNow();
            CancelTask(task, cancelledAt);
            var relatedTasks = await dbContext.AgentTasks
                .Where(item => item.ProjectId == task.ProjectId
                    && item.ProductionEpisodeId == task.ProductionEpisodeId
                    && item.TaskType == "shot-video"
                    && (item.Status == "queued" || item.Status == "running"))
                .ToListAsync(cancellationToken);
            foreach (var relatedTask in relatedTasks) CancelTask(relatedTask, cancelledAt);

            var activeRuns = await dbContext.ProductionRuns
                .Where(item => item.ProjectId == task.ProjectId
                    && item.ProductionEpisodeId == task.ProductionEpisodeId
                    && item.RunType == ShotVideoService.RunType
                    && (item.Status == "queued" || item.Status == "running"))
                .ToListAsync(cancellationToken);
            foreach (var run in activeRuns)
            {
                run.Status = "cancelled";
                run.CurrentStage = "cancelled";
                run.LastError = "用户已停止生成";
                run.CompletedAtUtc = cancelledAt;
                run.UpdatedAtUtc = cancelledAt;
                run.LeaseOwner = null;
                run.LeaseExpiresAtUtc = null;
            }
            await SessionAgentTaskJob.AppendEventAsync(
                dbContext, task.Id, "status", "cancelled", "用户已停止生成任务。", null, cancelledAt, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            var configuration = await dbContext.ComfyUiConfigurations.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
            if (configuration is not null && configuration.IsEnabled)
            {
                var client = httpClientFactory.CreateClient("ComfyUiVideo");
                try
                {
                    await client.PostAsync($"{configuration.BaseUrl.TrimEnd('/')}/interrupt", null, cancellationToken);
                    await client.PostAsJsonAsync(
                        $"{configuration.BaseUrl.TrimEnd('/')}/queue",
                        new { clear = true },
                        cancellationToken);
                }
                catch (HttpRequestException)
                {
                }
            }
            return Results.Ok(GenerationTaskScheduler.ToView(task));
        });
        group.MapGet("/{taskId:guid}/events", async (
            Guid taskId,
            long? after,
            HttpContext context,
            V2DbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (!await dbContext.AgentTasks.AsNoTracking()
                .AnyAsync(item => item.Id == taskId, cancellationToken))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";
            context.Response.ContentType = "text/event-stream";
            var sequence = after ?? 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                var events = await dbContext.AgentTaskEvents.AsNoTracking()
                    .Where(item => item.TaskId == taskId && item.Sequence > sequence)
                    .OrderBy(item => item.Sequence)
                    .ToArrayAsync(cancellationToken);
                foreach (var item in events)
                {
                    var payload = JsonSerializer.Serialize(new SessionAgentTaskEventView(
                        item.Sequence,
                        item.EventType,
                        item.Stage,
                        item.Message,
                        item.DataJson,
                        item.CreatedAtUtc), JsonOptions);
                    await context.Response.WriteAsync(
                        $"id: {item.Sequence}\nevent: {item.EventType}\ndata: {payload}\n\n",
                        cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                    sequence = item.Sequence;
                }

                var status = await dbContext.AgentTasks.AsNoTracking()
                    .Where(item => item.Id == taskId)
                    .Select(item => item.Status)
                    .SingleAsync(cancellationToken);
                if (status is "completed" or "failed" or "cancelled") break;
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
        });
        return app;
    }

    private static void CancelTask(AgentTask task, DateTimeOffset cancelledAt)
    {
        task.Status = "cancelled";
        task.CurrentStep = "已停止";
        task.LastError = "用户已停止生成";
        task.CancellationRequestedAtUtc = cancelledAt;
        task.CompletedAtUtc = cancelledAt;
        task.UpdatedAtUtc = cancelledAt;
        task.LeaseOwner = null;
        task.LeaseExpiresAtUtc = null;
    }
}