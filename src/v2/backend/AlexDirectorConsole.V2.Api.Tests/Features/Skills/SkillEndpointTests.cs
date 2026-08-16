using System.Net;
using System.Net.Http.Json;
using AlexDirectorConsole.V2.Api.Tests.Infrastructure;

namespace AlexDirectorConsole.V2.Api.Tests.Features.Skills;

public sealed class SkillEndpointTests(V2ApiFactory factory)
    : IClassFixture<V2ApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_returns_synchronized_system_skills_with_tools_and_content()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v2/skills");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var skills = await response.Content.ReadFromJsonAsync<SkillResponse[]>();
        Assert.NotNull(skills);
        Assert.Equal(5, skills.Length);
        var storyboard = Assert.Single(skills, skill => skill.Id == "storyboard-design");
        Assert.Equal("分镜设计", storyboard.Name);
        Assert.Equal("2.1.0", storyboard.Version);
        Assert.True(storyboard.IsEnabled);
        Assert.Contains("write_storyboard", storyboard.AllowedTools);
        Assert.Contains("# 分镜设计", storyboard.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Patch_persists_enabled_state()
    {
        using var client = factory.CreateClient();

        var patchResponse = await client.PatchAsJsonAsync(
            "/api/v2/skills/storyboard-design",
            new { isEnabled = false });

        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var updated = await patchResponse.Content.ReadFromJsonAsync<SkillResponse>();
        Assert.NotNull(updated);
        Assert.False(updated.IsEnabled);

        var getResponse = await client.GetAsync("/api/v2/skills/storyboard-design");
        var loaded = await getResponse.Content.ReadFromJsonAsync<SkillResponse>();
        Assert.NotNull(loaded);
        Assert.False(loaded.IsEnabled);
    }

    [Fact]
    public async Task Unknown_skill_returns_404()
    {
        using var client = factory.CreateClient();

        var response = await client.PatchAsJsonAsync(
            "/api/v2/skills/not-found",
            new { isEnabled = false });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record SkillResponse(
        string Id,
        string Name,
        string Description,
        string Version,
        bool IsEnabled,
        bool IsSystem,
        string[] AllowedTools,
        string Content,
        string SourcePath);
}