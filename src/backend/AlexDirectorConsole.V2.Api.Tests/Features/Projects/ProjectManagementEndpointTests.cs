using System.Net;
using System.Net.Http.Json;
using AlexDirectorConsole.V2.Api.Tests.Infrastructure;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AlexDirectorConsole.V2.Api.Tests.Features.Projects;

public sealed class ProjectManagementEndpointTests(V2ApiFactory factory)
    : IClassFixture<V2ApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Put_updates_project_and_returns_200()
    {
        var project = await CreateProjectAsync();
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/v2/projects/{project.Id}",
            new { name = "  新项目名称  ", description = "  新描述  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(updated);
        Assert.Equal("新项目名称", updated.Name);
        Assert.Equal("新描述", updated.Description);
        Assert.True(updated.UpdatedAtUtc >= project.UpdatedAtUtc);
    }

    [Fact]
    public async Task Put_with_blank_name_returns_400()
    {
        var project = await CreateProjectAsync();
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/v2/projects/{project.Id}",
            new { name = "  ", description = "描述" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_for_missing_project_returns_404()
    {
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/v2/projects/{Guid.NewGuid()}",
            new { name = "项目", description = "描述" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_removes_empty_project_and_returns_204()
    {
        var project = await CreateProjectAsync();
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/api/v2/projects/{project.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        Assert.False(await dbContext.Projects.AnyAsync(item => item.Id == project.Id));
    }

    [Fact]
    public async Task Delete_for_missing_project_returns_404()
    {
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/api/v2/projects/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_with_project_data_returns_409()
    {
        var project = await CreateProjectAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
            dbContext.ProductionEpisodes.Add(new ProductionEpisode
            {
                ProjectId = project.Id,
                EpisodeNumber = 1,
                Title = "第一集",
                Status = "draft",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        var response = await client.DeleteAsync($"/api/v2/projects/{project.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private async Task<ProjectResponse> CreateProjectAsync()
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v2/projects",
            new { name = "测试项目", description = "测试描述" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectResponse>())!;
    }

    private sealed record ProjectResponse(
        Guid Id,
        string Name,
        string? Description,
        Guid? CurrentCreativeSettingsId,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc);
}
