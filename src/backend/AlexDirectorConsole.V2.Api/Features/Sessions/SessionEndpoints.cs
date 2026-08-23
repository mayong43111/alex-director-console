using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AlexDirectorConsole.V2.Api.Features.Sessions;

public sealed record SendSessionMessageRequest(
    Guid AgentId,
    string? ScopeKey,
    Guid? SessionId,
    Guid? ProjectId,
    string? Title,
    string? Content,
    string? Page,
    string? Episode);

public sealed record RetrySessionMessageRequest(string? Page, string? Episode);

public static class SessionEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapSessions(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/sessions");

        group.MapGet("/", async (
            IQueryDispatcher queryDispatcher,
            CancellationToken cancellationToken) => Results.Ok(
                await queryDispatcher.QueryAsync(new ListSessionsQuery(), cancellationToken)));

        group.MapGet("/scoped", async (
            Guid agentId,
            string scopeKey,
            IQueryDispatcher queryDispatcher,
            CancellationToken cancellationToken) =>
        {
            var session = await queryDispatcher.QueryAsync(
                new GetScopedSessionQuery(agentId, scopeKey),
                cancellationToken);
            return session is null ? Results.NotFound() : Results.Ok(session);
        });

        group.MapGet("/{sessionId:guid}", async (
            Guid sessionId,
            IQueryDispatcher queryDispatcher,
            CancellationToken cancellationToken) =>
        {
            var session = await queryDispatcher.QueryAsync(
                new GetSessionQuery(sessionId),
                cancellationToken);
            return session is null ? Results.NotFound() : Results.Ok(session);
        });

        group.MapPost("/messages", async (
            SendSessionMessageRequest request,
            V2DbContext dbContext,
            IBackgroundJobClient backgroundJobs,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
            await EnqueueMessageAsync(request, dbContext, backgroundJobs, timeProvider, cancellationToken));

        group.MapPost("/messages/async", async (
            SendSessionMessageRequest request,
            V2DbContext dbContext,
            IBackgroundJobClient backgroundJobs,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
            await EnqueueMessageAsync(request, dbContext, backgroundJobs, timeProvider, cancellationToken));

        group.MapGet("/agent-tasks/{taskId:guid}", async (
            Guid taskId,
            V2DbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var task = await dbContext.AgentTasks.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == taskId && item.TaskType == "session-message", cancellationToken);
            return task is null ? Results.NotFound() : Results.Ok(ToTaskView(task));
        });

        group.MapGet("/agent-tasks/{taskId:guid}/events", async (
            Guid taskId,
            long? after,
            HttpContext context,
            V2DbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            if (!await dbContext.AgentTasks.AsNoTracking()
                .AnyAsync(item => item.Id == taskId && item.TaskType == "session-message", cancellationToken))
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
                    await context.Response.WriteAsync($"id: {item.Sequence}\nevent: {item.EventType}\ndata: {payload}\n\n", cancellationToken);
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

        group.MapPost("/agent-tasks/{taskId:guid}/stop", async (
            Guid taskId,
            V2DbContext dbContext,
            SessionAgentTaskCancellation taskCancellation,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            var task = await dbContext.AgentTasks
                .SingleOrDefaultAsync(item => item.Id == taskId && item.TaskType == "session-message", cancellationToken);
            if (task is null) return Results.NotFound();
            if (task.Status is "completed" or "failed" or "cancelled") return Results.Ok(ToTaskView(task));

            var now = timeProvider.GetUtcNow();
            task.CancellationRequestedAtUtc = now;
            task.UpdatedAtUtc = now;
            if (task.Status == "queued")
            {
                task.Status = "cancelled";
                task.CurrentStep = "已停止";
                task.CompletedAtUtc = now;
            }
            else
            {
                task.Status = "cancellation-requested";
                task.CurrentStep = "正在停止";
                taskCancellation.Cancel(task.Id);
            }
            await SessionAgentTaskJob.AppendEventAsync(
                dbContext, task.Id, "status", task.Status, "已请求停止任务。", null, now, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToTaskView(task));
        });

        group.MapPost("/{sessionId:guid}/messages/{messageId:guid}/retry", async (
            Guid sessionId,
            Guid messageId,
            RetrySessionMessageRequest request,
            ICommandDispatcher commandDispatcher,
            CancellationToken cancellationToken) =>
        {
            var result = await commandDispatcher.SendAsync(
                new RetrySessionMessageCommand(
                    sessionId,
                    messageId,
                    request.Page,
                    request.Episode),
                cancellationToken);
            return result.Status switch
            {
                SendSessionMessageStatus.Success => Results.Ok(result.Session),
                SendSessionMessageStatus.AgentNotFound or
                    SendSessionMessageStatus.ProjectNotFound or
                    SendSessionMessageStatus.SessionNotFound => Results.NotFound(),
                SendSessionMessageStatus.Invalid => Results.BadRequest(new { error = result.Error }),
                SendSessionMessageStatus.NotConfigured => Results.Conflict(new { error = result.Error }),
                _ => Results.Problem(result.Error, statusCode: StatusCodes.Status502BadGateway)
            };
        });

        group.MapDelete("/{sessionId:guid}/messages", async (
            Guid sessionId,
            ICommandDispatcher commandDispatcher,
            CancellationToken cancellationToken) =>
        {
            var deleted = await commandDispatcher.SendAsync(
                new ClearSessionMessagesCommand(sessionId),
                cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        return app;
    }

    private static async Task<IResult> EnqueueMessageAsync(
        SendSessionMessageRequest request,
        V2DbContext dbContext,
        IBackgroundJobClient backgroundJobs,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var content = request.Content?.Trim() ?? string.Empty;
        var scopeKey = request.ScopeKey?.Trim() ?? string.Empty;
        if (content.Length is 0 or > 10_000 || scopeKey.Length is 0 or > 500)
        {
            return Results.BadRequest(new { error = "消息或 Session scopeKey 无效。" });
        }
        if (!await dbContext.AgentDefinitions.AsNoTracking()
            .AnyAsync(agent => agent.Id == request.AgentId, cancellationToken))
        {
            return Results.NotFound();
        }
        if (request.ProjectId is Guid projectId
            && !await dbContext.Projects.AsNoTracking()
                .AnyAsync(project => project.Id == projectId, cancellationToken))
        {
            return Results.NotFound();
        }
        if (request.SessionId is Guid sessionId
            && !await dbContext.Sessions.AsNoTracking().AnyAsync(
                session => session.Id == sessionId
                    && session.AgentId == request.AgentId
                    && session.ScopeKey == scopeKey,
                cancellationToken))
        {
            return Results.NotFound();
        }

        var now = timeProvider.GetUtcNow();
        var task = new AgentTask
        {
            ProjectId = request.ProjectId,
            AgentId = request.AgentId,
            SessionId = request.SessionId,
            Intent = content,
            TaskType = "session-message",
            ContextSnapshotJson = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Status = "queued",
            CurrentStep = "等待 Hangfire 执行",
            ProgressCompleted = 0,
            ProgressTotal = 1,
            RequestedBy = "session-api",
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
            Message = "消息已进入 Hangfire 队列。",
            CreatedAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        var jobId = backgroundJobs.Enqueue<SessionAgentTaskJob>(
            job => job.ExecuteAsync(task.Id, CancellationToken.None));
        task.PlanJson = JsonSerializer.Serialize(new { hangfireJobId = jobId });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Accepted($"/api/v2/sessions/agent-tasks/{task.Id}", ToTaskView(task));
    }

    private static SessionAgentTaskView ToTaskView(AgentTask task) => new(
        task.Id,
        task.SessionId,
        task.Status,
        task.CurrentStep,
        task.LastError,
        task.CreatedAtUtc,
        task.UpdatedAtUtc,
        task.CompletedAtUtc);
}
