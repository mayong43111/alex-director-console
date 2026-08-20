using System.Net;
using System.Net.Http.Json;
using AlexDirectorConsole.V2.Api.Tests.Infrastructure;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AlexDirectorConsole.V2.Api.Tests.Features.Agents;

public sealed class AgentEndpointTests(V2ApiFactory factory)
    : IClassFixture<V2ApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_and_update_persist_agent_and_replace_skill_links()
    {
        using var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/v2/agents", new
        {
            name = "分镜副导演",
            systemPrompt = "负责将剧本转为可执行分镜。",
            skillIds = new[] { "storyboard-design", "script-writing" }
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.NotNull(created);
        Assert.Equal("分镜副导演", created.Name);
        Assert.Equal(2, created.SkillIds.Length);

        var updateResponse = await client.PutAsJsonAsync($"/api/v2/agents/{created.Id}", new
        {
            name = "分镜导演",
            systemPrompt = "只输出结构化、可生产的分镜结果。",
            skillIds = new[] { "storyboard-design", "storyboard-design" }
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.NotNull(updated);
        Assert.Equal("分镜导演", updated.Name);
        Assert.Equal("只输出结构化、可生产的分镜结果。", updated.SystemPrompt);
        Assert.Equal(["storyboard-design"], updated.SkillIds);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        Assert.Equal(3, await dbContext.AgentDefinitions.CountAsync());
        var link = Assert.Single(await dbContext.AgentSkills
            .Where(item => item.AgentId == created.Id)
            .ToListAsync());
        Assert.Equal(created.Id, link.AgentId);
        Assert.Equal("storyboard-design", link.SkillId);
    }

    [Fact]
    public async Task Create_rejects_unknown_skill_and_duplicate_name()
    {
        using var client = factory.CreateClient();

        var invalidResponse = await client.PostAsJsonAsync("/api/v2/agents", new
        {
            name = "未知技能 Agent",
            systemPrompt = "测试。",
            skillIds = new[] { "not-found" }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        var request = new
        {
            name = "唯一 Agent",
            systemPrompt = "测试。",
            skillIds = Array.Empty<string>()
        };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v2/agents", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v2/agents", request)).StatusCode);
    }

    [Fact]
    public async Task Delete_removes_agent_and_skill_links()
    {
        using var client = factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync("/api/v2/agents", new
        {
            name = "临时 Agent",
            systemPrompt = "测试删除。",
            skillIds = new[] { "storyboard-design" }
        });
        var created = await createResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.NotNull(created);

        var deleteResponse = await client.DeleteAsync($"/api/v2/agents/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v2/agents/{created.Id}")).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        Assert.Equal(2, await dbContext.AgentDefinitions.CountAsync());
        Assert.DoesNotContain(
            await dbContext.AgentSkills.ToListAsync(),
            link => link.AgentId == created.Id);
    }

    [Fact]
    public async Task Invoke_uses_requested_agent_and_returns_candidate_text()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v2/agents/{BuiltInAgents.ProjectDescriptionWriterId}/invoke",
            new
            {
                input = "原始项目描述",
                context = new { projectName = "三个火枪手" },
                maxLength = 4000
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<InvocationResponse>();
        Assert.NotNull(result);
        Assert.Equal("Agent 候选：原始项目描述", result.Value);
        var invocation = factory.LastAgentTextInvocation;
        Assert.NotNull(invocation);
        Assert.Equal(BuiltInAgents.ProjectDescriptionWriterId, invocation.Agent.Id);
        Assert.Contains("影视项目介绍编辑", invocation.Agent.SystemPrompt, StringComparison.Ordinal);
        Assert.Equal("原始项目描述", invocation.Input);
        Assert.Equal("三个火枪手", invocation.Context.GetProperty("projectName").GetString());
        Assert.Equal(4000, invocation.MaxLength);
    }

    [Fact]
    public async Task Invoke_returns_404_for_unknown_agent()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/v2/agents/{Guid.NewGuid()}/invoke", new
        {
            input = "文本",
            context = new { },
            maxLength = 100
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(factory.LastAgentTextInvocation);
    }

    private sealed record AgentResponse(
        Guid Id,
        string Name,
        string SystemPrompt,
        string[] SkillIds,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    private sealed record InvocationResponse(string Value, string Model, string Runtime);
}