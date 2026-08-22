using System.Net;
using System.Net.Http.Json;
using AlexDirectorConsole.V2.Api.Features.Sessions;
using AlexDirectorConsole.V2.Api.Tests.Infrastructure;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AlexDirectorConsole.V2.Api.Tests.Features.Sessions;

public sealed class SessionEndpointTests(V2ApiFactory factory)
    : IClassFixture<V2ApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Same_agent_keeps_history_separate_by_scope_and_exposes_management_views()
    {
        using var client = factory.CreateClient();
        const string globalScope = "global:project-center:assistant-director";

        await SendAsync(client, globalScope, null, "列出项目");
        var global = await SendAsync(client, globalScope, null, "继续");
        Assert.Equal(4, global.Messages.Length);
        Assert.Contains("历史 2 条", global.Messages[^1].Content, StringComparison.Ordinal);

        var projectId = await CreateProjectAsync(client);
        var projectScope = $"project:{projectId:D}:assistant-director";
        var project = await SendAsync(client, projectScope, projectId, "查看当前项目");
        Assert.Equal(2, project.Messages.Length);
        Assert.Contains("历史 0 条", project.Messages[^1].Content, StringComparison.Ordinal);

        var sessions = await client.GetFromJsonAsync<SessionSummaryResponse[]>("/api/v2/sessions");
        Assert.NotNull(sessions);
        Assert.Equal(2, sessions.Length);
        Assert.Equal([project.Id, global.Id], sessions.Select(session => session.Id));
        Assert.Equal([2, 4], sessions.Select(session => session.MessageCount));
        Assert.All(sessions, session => Assert.Equal(BuiltInAgents.AssistantDirectorId, session.AgentId));

        var detail = await client.GetFromJsonAsync<SessionResponse>($"/api/v2/sessions/{global.Id}");
        Assert.NotNull(detail);
        Assert.Equal(globalScope, detail.ScopeKey);
        Assert.Null(detail.ProjectId);
        Assert.Equal(global.Messages, detail.Messages);
    }

    [Fact]
    public async Task Supplied_session_id_must_match_agent_and_scope()
    {
        using var client = factory.CreateClient();
        var session = await SendAsync(client, "scope:one", null, "你好");

        var response = await client.PostAsJsonAsync(
            "/api/v2/sessions/messages",
            new
            {
                agentId = BuiltInAgents.AssistantDirectorId,
                scopeKey = "scope:two",
                sessionId = session.Id,
                content = "不应写入"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var unchanged = await client.GetFromJsonAsync<SessionResponse>($"/api/v2/sessions/{session.Id}");
        Assert.NotNull(unchanged);
        Assert.Equal(2, unchanged.Messages.Length);
    }

    [Fact]
    public async Task Clearing_messages_keeps_session_and_restarts_sequence()
    {
        using var client = factory.CreateClient();
        const string scopeKey = "scope:clear";
        var session = await SendAsync(client, scopeKey, null, "第一条");

        var clearResponse = await client.DeleteAsync($"/api/v2/sessions/{session.Id}/messages");

        Assert.Equal(HttpStatusCode.NoContent, clearResponse.StatusCode);
        var cleared = await client.GetFromJsonAsync<SessionResponse>($"/api/v2/sessions/{session.Id}");
        Assert.NotNull(cleared);
        Assert.Empty(cleared.Messages);

        var continued = await SendAsync(client, scopeKey, null, "重新开始");
        Assert.Equal(session.Id, continued.Id);
        Assert.Equal([1L, 2L], continued.Messages.Select(message => message.Sequence));
        Assert.Contains("历史 0 条", continued.Messages[^1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retrying_user_message_replaces_it_and_all_later_messages()
    {
        using var client = factory.CreateClient();
        const string scopeKey = "scope:retry";
        await SendAsync(client, scopeKey, null, "第一条");
        var session = await SendAsync(client, scopeKey, null, "第二条");
        var retriedMessage = session.Messages[2];
        await SendAsync(client, scopeKey, null, "第三条");

        var response = await client.PostAsJsonAsync(
            $"/api/v2/sessions/{session.Id}/messages/{retriedMessage.Id}/retry",
            new { page = "项目中心", episode = "未选择" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var retried = await response.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.NotNull(retried);
        Assert.Equal(4, retried.Messages.Length);
        Assert.Equal([1L, 2L, 3L, 4L], retried.Messages.Select(message => message.Sequence));
        Assert.Equal("第二条", retried.Messages[2].Content);
        Assert.NotEqual(retriedMessage.Id, retried.Messages[2].Id);
        Assert.Contains("历史 2 条", retried.Messages[^1].Content, StringComparison.Ordinal);

        var invalidResponse = await client.PostAsJsonAsync(
            $"/api/v2/sessions/{session.Id}/messages/{retried.Messages[^1].Id}/retry",
            new { page = "项目中心", episode = "未选择" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
    }

    [Fact]
    public void Continue_after_partial_first_frames_requires_the_next_frame_tool()
    {
        var history = new SessionHistoryMessage[]
        {
            new("assistant", "镜头首帧已生成 1 张，剩余未生成首帧：4 张。")
        };
        var allowedTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "generate_next_storyboard_first_frame"
        };

        var required = MafSessionAgent.GetRequiredToolName(history, "继续", allowedTools);

        Assert.Equal("generate_next_storyboard_first_frame", required);
    }

    [Fact]
    public async Task Async_message_task_persists_without_project_and_can_be_stopped()
    {
        using var client = factory.CreateClient();
        var enqueue = await client.PostAsJsonAsync(
            "/api/v2/sessions/messages/async",
            new
            {
                agentId = BuiltInAgents.AssistantDirectorId,
                scopeKey = "global:project-center:assistant-director",
                content = "创建项目测试",
                page = "项目中心"
            });

        Assert.Equal(HttpStatusCode.Accepted, enqueue.StatusCode);
        var queued = await enqueue.Content.ReadFromJsonAsync<AgentTaskResponse>();
        Assert.NotNull(queued);
        Assert.Equal("queued", queued.Status);
        Assert.Null(queued.SessionId);

        var restored = await client.GetFromJsonAsync<AgentTaskResponse>(
            $"/api/v2/sessions/agent-tasks/{queued.Id}");
        Assert.NotNull(restored);
        Assert.Equal("queued", restored.Status);

        var stop = await client.PostAsync(
            $"/api/v2/sessions/agent-tasks/{queued.Id}/stop",
            null);
        Assert.Equal(HttpStatusCode.OK, stop.StatusCode);
        var stopped = await stop.Content.ReadFromJsonAsync<AgentTaskResponse>();
        Assert.NotNull(stopped);
        Assert.Equal("cancelled", stopped.Status);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AlexDirectorConsole.V2.Database.Data.V2DbContext>();
        var events = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToArrayAsync(
            dbContext.AgentTaskEvents.Where(item => item.TaskId == queued.Id).OrderBy(item => item.Sequence));
        Assert.Equal([1L, 2L], events.Select(item => item.Sequence));
        Assert.Equal(["queued", "cancelled"], events.Select(item => item.Stage));
    }

    [Fact]
    public async Task Worker_completes_persistent_message_task_and_writes_session()
    {
        using var client = factory.CreateClient();
        var job = new SessionAgentTaskJob(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            factory.Services.GetRequiredService<SessionAgentTaskCancellation>(),
            factory.Services.GetRequiredService<SessionAgentExecutionContext>(),
            TimeProvider.System,
            factory.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SessionAgentTaskJob>>());
        var enqueue = await client.PostAsJsonAsync(
                "/api/v2/sessions/messages/async",
                new
                {
                    agentId = BuiltInAgents.AssistantDirectorId,
                    scopeKey = "global:worker-test:assistant-director",
                    content = "列出项目",
                    page = "项目中心"
                });
        var task = await enqueue.Content.ReadFromJsonAsync<AgentTaskResponse>();
        Assert.NotNull(task);

        Assert.True(await job.ExecuteAsync(task.Id, CancellationToken.None));
        var completed = await client.GetFromJsonAsync<AgentTaskResponse>(
            $"/api/v2/sessions/agent-tasks/{task.Id}");
        Assert.NotNull(completed);
        Assert.Equal("completed", completed.Status);
        Assert.NotNull(completed.SessionId);
        var session = await client.GetFromJsonAsync<SessionResponse>(
            $"/api/v2/sessions/{completed.SessionId}");
        Assert.NotNull(session);
        Assert.Equal(2, session.Messages.Length);
    }

    private async Task<SessionResponse> SendAsync(
        HttpClient client,
        string scopeKey,
        Guid? projectId,
        string content)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v2/sessions/messages",
            new
            {
                agentId = BuiltInAgents.AssistantDirectorId,
                scopeKey,
                projectId,
                content,
                page = "项目中心"
            });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var task = await response.Content.ReadFromJsonAsync<AgentTaskResponse>();
        Assert.NotNull(task);
        var job = factory.Services.GetRequiredService<SessionAgentTaskJob>();
        Assert.True(await job.ExecuteAsync(task.Id, CancellationToken.None));
        var completed = await client.GetFromJsonAsync<AgentTaskResponse>(
            $"/api/v2/sessions/agent-tasks/{task.Id}");
        Assert.NotNull(completed?.SessionId);
        var session = await client.GetFromJsonAsync<SessionResponse>(
            $"/api/v2/sessions/{completed.SessionId}");
        Assert.NotNull(session);
        return session;
    }

    private static async Task<Guid> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v2/projects",
            new { name = "Session 测试项目", description = "" });
        var project = await response.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(project);
        return project.Id;
    }

    private sealed record ProjectResponse(Guid Id);

    private sealed record AgentTaskResponse(Guid Id, Guid? SessionId, string Status);

    private sealed record SessionSummaryResponse(
        Guid Id,
        Guid AgentId,
        string ScopeKey,
        int MessageCount);

    private sealed record SessionResponse(
        Guid Id,
        Guid AgentId,
        string ScopeKey,
        Guid? ProjectId,
        MessageResponse[] Messages);

    private sealed record MessageResponse(
        Guid Id,
        long Sequence,
        string Role,
        string Content,
        string? Model,
        DateTimeOffset CreatedAtUtc);
}
