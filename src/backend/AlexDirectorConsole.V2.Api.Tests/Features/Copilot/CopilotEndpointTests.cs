using System.Net;
using System.Net.Http.Json;
using AlexDirectorConsole.V2.Api.Tests.Infrastructure;

namespace AlexDirectorConsole.V2.Api.Tests.Features.Copilot;

public sealed class CopilotEndpointTests(V2ApiFactory factory)
    : IClassFixture<V2ApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task New_project_has_an_empty_copilot_conversation()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);

        var response = await client.GetAsync($"/api/v2/projects/{projectId}/copilot/messages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var conversation = await response.Content.ReadFromJsonAsync<ConversationResponse>();
        Assert.NotNull(conversation);
        Assert.Null(conversation.ConversationId);
        Assert.Empty(conversation.Messages);
        Assert.Equal("MAF HarnessAgent", conversation.Runtime);
    }

    [Fact]
    public async Task Send_persists_user_and_agent_messages_with_history()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);

        var firstResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/copilot/messages",
            new { content = "先总结当前项目" });
        var secondResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/copilot/messages",
            new { content = "再给下一步建议" });

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var conversation = await secondResponse.Content.ReadFromJsonAsync<ConversationResponse>();
        Assert.NotNull(conversation);
        Assert.NotNull(conversation.ConversationId);
        Assert.Equal(4, conversation.Messages.Length);
        Assert.Equal([1, 2, 3, 4], conversation.Messages.Select(message => message.Sequence));
        Assert.Equal("user", conversation.Messages[0].Role);
        Assert.Equal("assistant", conversation.Messages[1].Role);
        Assert.Equal("gpt-5.4", conversation.Messages[1].Model);
        Assert.Contains("历史 2 条", conversation.Messages[3].Content, StringComparison.Ordinal);

        var loaded = await client.GetFromJsonAsync<ConversationResponse>(
            $"/api/v2/projects/{projectId}/copilot/messages");
        Assert.NotNull(loaded);
        Assert.Equal(conversation.Messages, loaded.Messages);
    }

    [Fact]
    public async Task Blank_message_returns_400_without_creating_conversation()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/copilot/messages",
            new { content = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var conversation = await client.GetFromJsonAsync<ConversationResponse>(
            $"/api/v2/projects/{projectId}/copilot/messages");
        Assert.NotNull(conversation);
        Assert.Empty(conversation.Messages);
    }

    [Fact]
    public async Task Unknown_project_returns_404()
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v2/projects/{Guid.NewGuid()}/copilot/messages",
            new { content = "你好" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reset_clears_history_but_keeps_session()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/copilot/messages",
            new { content = "你好" });
        var beforeReset = await client.GetFromJsonAsync<ConversationResponse>(
            $"/api/v2/projects/{projectId}/copilot/messages");
        Assert.NotNull(beforeReset);

        var response = await client.DeleteAsync($"/api/v2/projects/{projectId}/copilot/messages");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var conversation = await client.GetFromJsonAsync<ConversationResponse>(
            $"/api/v2/projects/{projectId}/copilot/messages");
        Assert.NotNull(conversation);
        Assert.Equal(beforeReset.ConversationId, conversation.ConversationId);
        Assert.Empty(conversation.Messages);
    }

    private static async Task<Guid> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v2/projects",
            new { name = "Agent 测试项目", description = "" });
        var project = await response.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(project);
        return project.Id;
    }

    private sealed record ProjectResponse(Guid Id);

    private sealed record ConversationResponse(
        Guid? ConversationId,
        Guid ProjectId,
        string Runtime,
        MessageResponse[] Messages);

    private sealed record MessageResponse(
        Guid Id,
        long Sequence,
        string Role,
        string Content,
        string? Model,
        DateTimeOffset CreatedAtUtc);
}