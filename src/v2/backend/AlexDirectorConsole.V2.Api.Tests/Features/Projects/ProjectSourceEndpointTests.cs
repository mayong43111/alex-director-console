using System.Net;
using System.Net.Http.Json;
using AlexDirectorConsole.V2.Api.Features.Projects.Queries;
using AlexDirectorConsole.V2.Api.Features.Projects.Sources;
using AlexDirectorConsole.V2.Api.Tests.Infrastructure;
using AlexDirectorConsole.V2.Database.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AlexDirectorConsole.V2.Api.Tests.Features.Projects;

public sealed class ProjectSourceEndpointTests(V2ApiFactory factory)
    : IClassFixture<V2ApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task New_project_has_no_sources_or_production_episodes()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);

        var sources = await client.GetFromJsonAsync<ProjectSourceView[]>(
            $"/api/v2/projects/{projectId}/sources");
        var episodes = await client.GetFromJsonAsync<ProductionEpisodeView[]>(
            $"/api/v2/projects/{projectId}/production-episodes");

        Assert.NotNull(sources);
        Assert.Empty(sources);
        Assert.NotNull(episodes);
        Assert.Empty(episodes);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        Assert.False(await dbContext.ProductionEpisodes.AnyAsync());
    }

    [Fact]
    public async Task Import_creates_project_source_chapters_without_creating_production_episodes()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources",
            new
            {
                title = "三个火枪手原著",
                description = "作为后续改编和剧集生成的参考",
                fileName = "三个火枪手.md",
                content = "# 第一章 离开故乡\n达达尼昂前往巴黎。\n\n# 第二章 初遇火枪手\n一次误会引出三场决斗。"
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var source = await response.Content.ReadFromJsonAsync<ProjectSourceView>();
        Assert.NotNull(source);
        Assert.Equal("三个火枪手原著", source.Title);
        Assert.Equal(2, source.ChapterCount);
        Assert.Equal(["第一章 离开故乡", "第二章 初遇火枪手"], source.Chapters.Select(item => item.Title));
        Assert.All(source.Chapters, chapter => Assert.NotEqual(Guid.Empty, chapter.Id));

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        var asset = await dbContext.Assets.SingleAsync(item => item.ProjectId == projectId);
        Assert.Equal("source-document", asset.Type);
        Assert.Null(asset.ProductionEpisodeId);
        Assert.Equal(source.Id, asset.ResourceId);
        Assert.Single(await dbContext.ResourceStates.ToListAsync());
        Assert.False(await dbContext.ProductionEpisodes.AnyAsync());
    }

    [Fact]
    public async Task Blank_content_returns_validation_problem_without_writing_assets()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources",
            new { title = "空资料", description = "", content = "   ", fileName = "empty.txt" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        Assert.False(await dbContext.Assets.AnyAsync());
    }

    [Fact]
    public async Task Append_chapter_creates_a_new_source_version_without_changing_production_episodes()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources",
            new
            {
                title = "三个火枪手原著",
                description = "改编参考",
                content = "# 第一章\n第一章正文。\n\n# 第二章\n第二章正文。",
                fileName = "chapters-1-2.md"
            });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ProjectSourceView>();
        Assert.NotNull(created);

        var appendResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources/{created.Id}/chapters",
            new { content = "# 第三章\n第三章正文。", fileName = "chapter-3.md" });

        Assert.Equal(HttpStatusCode.OK, appendResponse.StatusCode);
        var updated = await appendResponse.Content.ReadFromJsonAsync<ProjectSourceView>();
        Assert.NotNull(updated);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(2, updated.Version);
        Assert.Equal(3, updated.ChapterCount);
        Assert.Equal("第三章", updated.Chapters[2].Title);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        var versions = await dbContext.Assets
            .Where(item => item.ProjectId == projectId && item.Type == "source-document")
            .OrderBy(item => item.Version)
            .ToListAsync();
        Assert.Equal([1, 2], versions.Select(item => item.Version));
        Assert.Single(versions.Select(item => item.ResourceId).Distinct());
        Assert.Single(versions.Select(item => item.Number).Distinct());
        Assert.Equal(updated.AssetId, (await dbContext.ResourceStates.SingleAsync()).CurrentAssetId);
        Assert.False(await dbContext.ProductionEpisodes.AnyAsync());
    }

    [Fact]
    public async Task Source_update_marks_analysis_stale_without_changing_episodes()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources",
            new
            {
                title = "三个火枪手原著",
                description = "改编参考",
                content = "# 第一章\n达达尼昂离开故乡。\n\n# 第二章\n达达尼昂抵达巴黎。",
                fileName = "chapters-1-2.md"
            });
        createResponse.EnsureSuccessStatusCode();
        var sourceV1 = await createResponse.Content.ReadFromJsonAsync<ProjectSourceView>();
        Assert.NotNull(sourceV1);

        var analysisResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/sources/{sourceV1.Id}/analysis",
            null);
        analysisResponse.EnsureSuccessStatusCode();
        var analysis = await analysisResponse.Content.ReadFromJsonAsync<StoryMaterialAnalysisView>();
        Assert.NotNull(analysis);
        Assert.Equal(sourceV1.AssetId, analysis.SourceAssetId);
        Assert.Equal(1, analysis.SourceVersion);
        Assert.False(analysis.IsStale);
        Assert.Single(analysis.Characters);

        var appendResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources/{sourceV1.Id}/chapters",
            new { content = "# 第三章\n达达尼昂得到接见。", fileName = "chapter-3.md" });
        appendResponse.EnsureSuccessStatusCode();
        var sourceV2 = await appendResponse.Content.ReadFromJsonAsync<ProjectSourceView>();
        Assert.NotNull(sourceV2);
        Assert.Equal(2, sourceV2.Version);

        var staleAnalysis = await client.GetFromJsonAsync<StoryMaterialAnalysisView>(
            $"/api/v2/projects/{projectId}/sources/{sourceV1.Id}/analysis");
        Assert.NotNull(staleAnalysis);
        Assert.True(staleAnalysis.IsStale);
        Assert.Equal(1, staleAnalysis.SourceVersion);
        Assert.Contains("v2", staleAnalysis.StaleReason);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        Assert.Single(await dbContext.AssetDependencies.ToListAsync());
        Assert.False(await dbContext.ProductionEpisodes.AnyAsync());
    }

    [Fact]
    public async Task Script_draft_creates_episodes_only_after_confirmation_and_ignores_later_source_updates()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources",
            new
            {
                title = "三个火枪手原著",
                description = "改编参考",
                content = "# 第一章\n达达尼昂离开故乡。\n\n# 第二章\n达达尼昂抵达巴黎。",
                fileName = "chapters-1-2.md"
            });
        createResponse.EnsureSuccessStatusCode();
        var source = await createResponse.Content.ReadFromJsonAsync<ProjectSourceView>();
        Assert.NotNull(source);
        (await client.PostAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/analysis",
            null)).EnsureSuccessStatusCode();

        var draftResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft",
            new { desiredEpisodeCount = 1, instruction = "突出达达尼昂初入巴黎的冲突" });
        draftResponse.EnsureSuccessStatusCode();
        var draft = await draftResponse.Content.ReadFromJsonAsync<AdaptationScriptView>();
        Assert.NotNull(draft);
        Assert.Equal("draft", draft.Status);
        Assert.Empty(draft.ProductionEpisodeIds);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
            Assert.False(await dbContext.ProductionEpisodes.AnyAsync());
            Assert.False(await dbContext.Assets.AnyAsync(item => item.Type == "script-package"));
        }

        var confirmResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft/confirm",
            null);
        confirmResponse.EnsureSuccessStatusCode();
        var confirmed = await confirmResponse.Content.ReadFromJsonAsync<AdaptationScriptView>();
        Assert.NotNull(confirmed);
        Assert.Equal("confirmed", confirmed.Status);
        Assert.Single(confirmed.ProductionEpisodeIds);

        var package = await client.GetFromJsonAsync<ProductionScriptPackageView>(
            $"/api/v2/projects/{projectId}/production-episodes/{confirmed.ProductionEpisodeIds[0]}/script-package");
        Assert.NotNull(package);
        Assert.Equal(confirmed.ProductionEpisodeIds[0], package.ProductionEpisodeId);
        Assert.Equal(confirmed.AssetId, package.AdaptationScriptAssetId);
        Assert.Equal(draft.Episodes[0].Title, package.Episode.Title);
        Assert.NotEmpty(package.Episode.Scenes);

        var appendResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/chapters",
            new { content = "# 第三章\n达达尼昂得到接见。", fileName = "chapter-3.md" });
        appendResponse.EnsureSuccessStatusCode();
        var unchangedScript = await client.GetFromJsonAsync<AdaptationScriptView>(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft");
        Assert.NotNull(unchangedScript);
        Assert.Equal(confirmed.AssetId, unchangedScript.AssetId);
        Assert.Equal("confirmed", unchangedScript.Status);
        Assert.True(unchangedScript.HasNewerSourceVersion);

        await using var finalScope = factory.Services.CreateAsyncScope();
        var finalDbContext = finalScope.ServiceProvider.GetRequiredService<V2DbContext>();
        Assert.Equal(1, await finalDbContext.ProductionEpisodes.CountAsync());
        Assert.Equal(1, await finalDbContext.Assets.CountAsync(item => item.Type == "script-package"));
    }

    [Fact]
    public async Task Script_draft_uses_project_episode_count_and_appends_without_rewriting_existing_episodes()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources",
            new
            {
                title = "三个火枪手原著",
                description = "改编参考",
                content = "# 第一章\n达达尼昂离开故乡。\n\n# 第二章\n达达尼昂抵达巴黎。",
                fileName = "chapters-1-2.md"
            });
        createResponse.EnsureSuccessStatusCode();
        var source = await createResponse.Content.ReadFromJsonAsync<ProjectSourceView>();
        Assert.NotNull(source);
        (await client.PostAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/analysis",
            null)).EnsureSuccessStatusCode();

        var generateResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft",
            new { instruction = "遵循项目设定规划完整分集" });
        generateResponse.EnsureSuccessStatusCode();
        var draft = await generateResponse.Content.ReadFromJsonAsync<AdaptationScriptView>();
        Assert.NotNull(draft);
        Assert.Equal(3, draft.Episodes.Count);
        Assert.NotEmpty(draft.OverallSmallHooks);
        Assert.NotEmpty(draft.OverallBigHooks);
        Assert.All(draft.Episodes, episode =>
        {
            Assert.NotEmpty(episode.SmallHooks!);
            Assert.NotEmpty(episode.BigHooks!);
        });
        var originalTitles = draft.Episodes.Select(item => item.Title).ToArray();

        var appendResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft/episodes",
            new { instruction = "增加一集承上启下的追逐" });
        appendResponse.EnsureSuccessStatusCode();
        var appended = await appendResponse.Content.ReadFromJsonAsync<AdaptationScriptView>();
        Assert.NotNull(appended);
        Assert.Equal(2, appended.Version);
        Assert.Equal(4, appended.Episodes.Count);
        Assert.Equal(originalTitles, appended.Episodes.Take(3).Select(item => item.Title));
        Assert.Equal(4, appended.Episodes[3].ProposalNumber);
        Assert.Empty(appended.ProductionEpisodeIds);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        Assert.False(await dbContext.ProductionEpisodes.AnyAsync());
        Assert.Equal(2, await dbContext.Assets.CountAsync(item => item.Type == "adaptation-script-draft"));
    }

    private static async Task<Guid> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v2/projects",
            new { name = "三个火枪手", description = "经典文学改编" });
        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<CreatedProjectResponse>();
        return Assert.IsType<CreatedProjectResponse>(project).Id;
    }

    private sealed record CreatedProjectResponse(Guid Id);
}