using System.Text.Json;
using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Projects.Assets;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
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

    public static bool IsSupported(string taskType) => taskType is
        ProjectCover or ProjectCoverPreview or ProjectSettingsAssist or ProjectDescriptionAssist or
        VisualReferencePrompt or VisualReferenceImage or
        VisualReferencePromptBatch or VisualReferenceImageBatch or
        StoryboardImagePrompt or StoryboardImagePreview or StoryboardImage or StoryboardVideoPrompt or StoryboardVideo or ShotVideoPreview or
        StoryboardImagePromptBatch or StoryboardImageBatch or
        StoryboardVideoPromptBatch or StoryboardVideoBatch or VoiceProfile;
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
    string? RequestJson = null);

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
        task.CurrentStep = "正在生成";
        task.LeaseOwner = workerId;
        task.LeaseExpiresAtUtc = now.AddMinutes(30);
        task.StartedAtUtc ??= now;
        task.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        try
        {
            var payload = JsonSerializer.Deserialize<GenerationTaskPayload>(task.ContextSnapshotJson, JsonOptions)
                ?? throw new InvalidOperationException("生成任务上下文无效。");
            var result = await ExecuteCoreAsync(scope.ServiceProvider, task.TaskType, payload, cancellationToken);
            var completedAt = timeProvider.GetUtcNow();
            task.Status = "completed";
            task.CurrentStep = "已完成";
            task.ProgressCompleted = 1;
            task.CompletedAtUtc = completedAt;
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
        string taskType,
        GenerationTaskPayload payload,
        CancellationToken cancellationToken)
    {
        switch (taskType)
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
                    payload.ProjectId, payload.Kind ?? string.Empty, cancellationToken);
            case GenerationTaskTypes.VisualReferenceImageBatch:
                return await services.GetRequiredService<IVisualReferenceService>().GenerateMissingImagesAsync(
                    payload.ProjectId, payload.Kind ?? string.Empty, cancellationToken);
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
                return await services.GetRequiredService<IShotVideoService>().StartAsync(
                    payload.ProjectId,
                    RequiredEpisodeId(payload),
                    RequiredResourceId(payload),
                    videoPrompt.Prompt,
                    videoPrompt.PreviewHash ?? throw new InvalidOperationException("当前视频提示词缺少预览校验值，请重新生成。"),
                    videoPrompt.Instruction,
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
                    payload.ProjectId, RequiredEpisodeId(payload), cancellationToken);
            case GenerationTaskTypes.StoryboardImageBatch:
                return await services.GetRequiredService<IStoryboardMediaBatchService>().GenerateMissingImagesAsync(
                    payload.ProjectId, RequiredEpisodeId(payload), cancellationToken);
            case GenerationTaskTypes.StoryboardVideoPromptBatch:
                return await services.GetRequiredService<IStoryboardMediaBatchService>().GenerateMissingVideoPromptsAsync(
                    payload.ProjectId, RequiredEpisodeId(payload), cancellationToken);
            case GenerationTaskTypes.StoryboardVideoBatch:
                return await services.GetRequiredService<IStoryboardMediaBatchService>().GenerateMissingVideosAsync(
                    payload.ProjectId, RequiredEpisodeId(payload), cancellationToken);
            case GenerationTaskTypes.VoiceProfile:
                return await services.GetRequiredService<IVoiceProfileService>().GenerateAsync(
                    payload.ProjectId, RequiredResourceId(payload), cancellationToken);
            default:
                throw new InvalidOperationException($"不支持的生成任务类型：{taskType}");
        }
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
                    || task.TaskType == "session-message"
                    || task.TaskType == ShotVideoService.RunType))
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
            else if (task.TaskType == ShotVideoService.RunType)
            {
                using var context = JsonDocument.Parse(task.ContextSnapshotJson);
                var runId = context.RootElement.GetProperty("runId").GetGuid();
                backgroundJobs.Enqueue<ShotVideoJob>(job => job.ExecuteAsync(runId, CancellationToken.None));
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
        return app;
    }
}