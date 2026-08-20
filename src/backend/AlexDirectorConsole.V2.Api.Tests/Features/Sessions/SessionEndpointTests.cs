using System.Net;
using System.Net.Http.Json;
using AlexDirectorConsole.V2.Api.Tests.Infrastructure;
using AlexDirectorConsole.V2.Database.Models;

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

    private static async Task<SessionResponse> SendAsync(
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
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<SessionResponse>();
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
