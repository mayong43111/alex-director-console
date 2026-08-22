using System.Net;
using System.Net.Http.Json;
using AlexDirectorConsole.V2.Api.Tests.Infrastructure;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AlexDirectorConsole.V2.Api.Tests.Features.Projects;

public sealed class CreateProjectEndpointTests(V2ApiFactory factory)
    : IClassFixture<V2ApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Valid_request_creates_an_empty_project_and_returns_201()
    {
        var startedAt = DateTimeOffset.UtcNow;
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v2/projects",
            new { name = "  天桥食堂  ", description = "  都市悬疑短片  " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var project = await response.Content.ReadFromJsonAsync<CreatedProjectResponse>();
        Assert.NotNull(project);
        Assert.NotEqual(Guid.Empty, project.Id);
        Assert.Equal("天桥食堂", project.Name);
        Assert.Equal("都市悬疑短片", project.Description);
        Assert.Null(project.CurrentCreativeSettingsId);
        Assert.Equal(project.CreatedAtUtc, project.UpdatedAtUtc);
        Assert.InRange(project.CreatedAtUtc, startedAt, DateTimeOffset.UtcNow);
        Assert.Equal($"/api/v2/projects/{project.Id}", response.Headers.Location?.OriginalString);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        Assert.Equal(1, dbContext.Projects.Count());
        Assert.Equal(0, dbContext.ProductionEpisodes.Count());
        Assert.Equal(0, dbContext.Assets.Count());
        Assert.Equal(0, dbContext.ResourceStates.Count());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_name_returns_400_without_writing(string name)
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v2/projects",
            new { name, description = "任意描述" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(0, await ProjectCountAsync());
    }

    [Fact]
    public async Task Name_longer_than_200_characters_returns_400_without_writing()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v2/projects",
            new { name = new string('项', 201), description = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await ProjectCountAsync());
    }

    [Fact]
    public async Task Description_longer_than_4000_characters_returns_400_without_writing()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v2/projects",
            new { name = "合法项目", description = new string('描', 4001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await ProjectCountAsync());
    }

    [Fact]
    public async Task Assist_description_returns_agent_text_without_creating_project()
    {
        using var client = factory.CreateClient();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
            var agent = await dbContext.AgentDefinitions.SingleAsync(
                item => item.Id == BuiltInAgents.ProjectDescriptionWriterId);
            agent.SystemPrompt = "只写一句清晰的项目介绍。";
            await dbContext.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync(
            "/api/v2/projects/assist-description",
            new { name = "天桥食堂", description = "都市悬疑短片" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = await factory.CompleteGenerationTaskAsync<AssistDescriptionResponse>(response);
        Assert.NotNull(result);
        Assert.Equal("description", result.Field);
        Assert.Equal("AI 优化：都市悬疑短片", result.Value);
        Assert.Equal("MAF HarnessAgent", result.Runtime);
        Assert.Equal(
            "只写一句清晰的项目介绍。",
            factory.LastProjectSettingsAssistRequest?.SystemInstructions);
        Assert.Equal(0, await ProjectCountAsync());
    }

    [Theory]
    [InlineData("", "已有描述")]
    [InlineData("项目", "")]
    public async Task Assist_description_requires_name_and_description(string name, string description)
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v2/projects/assist-description",
            new { name, description });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await ProjectCountAsync());
    }

    private async Task<int> ProjectCountAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        return dbContext.Projects.Count();
    }

    private sealed record CreatedProjectResponse(
        Guid Id,
        string Name,
        string? Description,
        Guid? CurrentCreativeSettingsId,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    private sealed record AssistDescriptionResponse(
        string Field,
        string Value,
        string Model,
        string Runtime);
}