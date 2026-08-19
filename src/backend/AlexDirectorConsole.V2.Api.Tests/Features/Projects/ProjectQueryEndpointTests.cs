using System.Net;
using System.Net.Http.Json;
using AlexDirectorConsole.V2.Api.Tests.Infrastructure;

namespace AlexDirectorConsole.V2.Api.Tests.Features.Projects;

public sealed class ProjectQueryEndpointTests(V2ApiFactory factory)
    : IClassFixture<V2ApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_returns_created_projects()
    {
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/v2/projects", new { name = "项目 A", description = "第一项" });
        await client.PostAsJsonAsync("/api/v2/projects", new { name = "项目 B", description = "第二项" });

        var response = await client.GetAsync("/api/v2/projects");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var projects = await response.Content.ReadFromJsonAsync<ProjectResponse[]>();
        Assert.NotNull(projects);
        Assert.Equal(2, projects.Length);
        Assert.Contains(projects, project => project.Name == "项目 A");
        Assert.Contains(projects, project => project.Name == "项目 B");
    }

    [Fact]
    public async Task Get_returns_project_by_id()
    {
        using var client = factory.CreateClient();
        var createdResponse = await client.PostAsJsonAsync(
            "/api/v2/projects",
            new { name = "可刷新项目", description = "刷新后仍可读取" });
        var created = await createdResponse.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(created);

        var response = await client.GetAsync($"/api/v2/projects/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var project = await response.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(project);
        Assert.Equal(created, project);
    }

    [Fact]
    public async Task Get_unknown_project_returns_404()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/v2/projects/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record ProjectResponse(
        Guid Id,
        string Name,
        string? Description,
        Guid? CurrentCreativeSettingsId,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);
}