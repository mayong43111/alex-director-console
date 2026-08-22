using System.Collections.Concurrent;
using System.Text.Json;
using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Sessions;

public sealed record SessionAgentTaskView(
    Guid Id,
    Guid? SessionId,
    string Status,
    string? CurrentStep,
    string? LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record SessionAgentTaskEventView(
    long Sequence,
    string EventType,
    string? Stage,
    string Message,
    string? DataJson,
    DateTimeOffset CreatedAtUtc);

public sealed class SessionAgentTaskCancellation
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> active = new();

    public CancellationToken Register(Guid taskId, CancellationToken stoppingToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        if (!active.TryAdd(taskId, source))
        {
            source.Dispose();
            throw new InvalidOperationException("Agent 任务已在运行。");
        }
        return source.Token;
    }

    public void Complete(Guid taskId)
    {
        if (active.TryRemove(taskId, out var source)) source.Dispose();
    }

    public bool Cancel(Guid taskId)
    {
        if (!active.TryGetValue(taskId, out var source)) return false;
        source.Cancel();
        return true;
    }
}

public sealed class SessionAgentExecutionContext(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider)
{
    private readonly AsyncLocal<Guid?> currentTaskId = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> eventLocks = new();

    public IDisposable Begin(Guid taskId)
    {
        var previous = currentTaskId.Value;
        currentTaskId.Value = taskId;
        return new Scope(() => currentTaskId.Value = previous);
    }

    public async Task PublishToolAsync(
        string stage,
        string toolName,
        string message,
        CancellationToken cancellationToken)
    {
        if (currentTaskId.Value is not Guid taskId) return;
        var gate = eventLocks.GetOrAdd(taskId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
            await SessionAgentTaskJob.AppendEventAsync(
                dbContext,
                taskId,
                "tool",
                stage,
                message,
                JsonSerializer.Serialize(new { toolName }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                timeProvider.GetUtcNow(),
                cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

public sealed class SessionAgentTaskJob(
    IServiceScopeFactory scopeFactory,
    SessionAgentTaskCancellation cancellation,
    SessionAgentExecutionContext executionContext,
    TimeProvider timeProvider,
    ILogger<SessionAgentTaskJob> logger)
{
    private readonly string workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task<bool> ExecuteAsync(Guid taskId, CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        var now = timeProvider.GetUtcNow();
        var candidate = await dbContext.AgentTasks
            .AsNoTracking()
            .Where(task => task.Id == taskId
                && task.TaskType == "session-message"
                && (task.Status == "queued" || task.Status == "running"))
            .Select(task => new
            {
                task.Id,
                task.Status,
                task.LeaseOwner,
                task.LeaseExpiresAtUtc,
                task.CreatedAtUtc
            })
            .SingleOrDefaultAsync(stoppingToken);
        if (candidate is not null
            && candidate.Status == "running"
            && candidate.LeaseExpiresAtUtc >= now)
        {
            return false;
        }
        if (candidate is null) return false;

        var claimed = await dbContext.AgentTasks
            .Where(task => task.Id == candidate.Id
                && task.Status == candidate.Status
                && task.LeaseOwner == candidate.LeaseOwner)
            .ExecuteUpdateAsync(update => update
                .SetProperty(task => task.Status, "running")
                .SetProperty(task => task.CurrentStep, "正在调用 Agent")
                .SetProperty(task => task.LeaseOwner, workerId)
                .SetProperty(task => task.LeaseExpiresAtUtc, now.AddMinutes(30))
                .SetProperty(task => task.StartedAtUtc, task => task.StartedAtUtc ?? now)
                .SetProperty(task => task.UpdatedAtUtc, now), stoppingToken);
        if (claimed == 0) return true;

        var task = await dbContext.AgentTasks.SingleAsync(item => item.Id == candidate.Id, stoppingToken);
        await AppendEventAsync(dbContext, task.Id, "status", "running", "副导演已开始处理。", null, now, stoppingToken);
        var executionToken = cancellation.Register(task.Id, stoppingToken);
        using var monitorStop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var monitorTask = MonitorCancellationAsync(task.Id, monitorStop.Token);
        try
        {
            var request = JsonSerializer.Deserialize<SendSessionMessageRequest>(task.ContextSnapshotJson, JsonOptions)
                ?? throw new InvalidOperationException("Agent 任务上下文无效。");
            var dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            using var executionScope = executionContext.Begin(task.Id);
            var result = await dispatcher.SendAsync(
                new SendSessionMessageCommand(
                    request.AgentId,
                    request.ScopeKey,
                    request.SessionId,
                    request.ProjectId,
                    request.Title,
                    request.Content,
                    request.Page,
                    request.Episode),
                executionToken);
            var completedAt = timeProvider.GetUtcNow();
            if (result.Status != SendSessionMessageStatus.Success || result.Session is null)
            {
                task.Status = "failed";
                task.LastError = result.Error ?? "Agent 任务执行失败。";
                task.CurrentStep = "执行失败";
                task.CompletedAtUtc = completedAt;
                await AppendEventAsync(dbContext, task.Id, "failure", "failed", task.LastError, null, completedAt, stoppingToken);
            }
            else
            {
                task.Status = "completed";
                task.SessionId = result.Session.Id;
                task.Model = result.Session.Messages.LastOrDefault(message => message.Role == "assistant")?.Model;
                task.CurrentStep = "已完成";
                task.ProgressCompleted = 1;
                task.ProgressTotal = 1;
                task.CompletedAtUtc = completedAt;
                await AppendEventAsync(
                    dbContext,
                    task.Id,
                    "result",
                    "completed",
                    "副导演已完成本轮处理。",
                    JsonSerializer.Serialize(new { sessionId = result.Session.Id }, JsonOptions),
                    completedAt,
                    stoppingToken);
            }
            task.LeaseOwner = null;
            task.LeaseExpiresAtUtc = null;
            task.UpdatedAtUtc = completedAt;
            await dbContext.SaveChangesAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (executionToken.IsCancellationRequested)
        {
            var cancelledAt = timeProvider.GetUtcNow();
            task.Status = "cancelled";
            task.CurrentStep = "已停止";
            task.CompletedAtUtc = cancelledAt;
            task.LeaseOwner = null;
            task.LeaseExpiresAtUtc = null;
            task.UpdatedAtUtc = cancelledAt;
            await AppendEventAsync(dbContext, task.Id, "status", "cancelled", "任务已停止。", null, cancelledAt, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception error)
        {
            var failedAt = timeProvider.GetUtcNow();
            logger.LogError(error, "Session Agent task {TaskId} failed.", task.Id);
            task.Status = "failed";
            task.CurrentStep = "执行失败";
            task.LastError = error.Message[..Math.Min(error.Message.Length, 4000)];
            task.CompletedAtUtc = failedAt;
            task.LeaseOwner = null;
            task.LeaseExpiresAtUtc = null;
            task.UpdatedAtUtc = failedAt;
            await AppendEventAsync(dbContext, task.Id, "failure", "failed", task.LastError, null, failedAt, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            await monitorStop.CancelAsync();
            try
            {
                await monitorTask;
            }
            catch (OperationCanceledException) when (monitorStop.IsCancellationRequested)
            {
            }
            cancellation.Complete(task.Id);
        }
        return true;
    }

    private async Task MonitorCancellationAsync(Guid taskId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
            var status = await dbContext.AgentTasks.AsNoTracking()
                .Where(task => task.Id == taskId)
                .Select(task => task.Status)
                .SingleOrDefaultAsync(cancellationToken);
            if (status == "cancellation-requested")
            {
                cancellation.Cancel(taskId);
                return;
            }
            if (status != "running") return;
        }
    }

    internal static async Task AppendEventAsync(
        V2DbContext dbContext,
        Guid taskId,
        string eventType,
        string? stage,
        string message,
        string? dataJson,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var sequence = (await dbContext.AgentTaskEvents
            .Where(item => item.TaskId == taskId)
            .MaxAsync(item => (long?)item.Sequence, cancellationToken) ?? 0) + 1;
        dbContext.AgentTaskEvents.Add(new AgentTaskEvent
        {
            TaskId = taskId,
            Sequence = sequence,
            EventType = eventType,
            Stage = stage,
            Message = message,
            DataJson = dataJson,
            CreatedAtUtc = createdAt
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
