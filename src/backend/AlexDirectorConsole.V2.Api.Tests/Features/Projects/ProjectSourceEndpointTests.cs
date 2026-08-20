using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    public async Task Chapter_analysis_replaces_its_partition_and_uses_the_highest_historical_version()
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

        var first = await AnalyzeChapterAsync(client, projectId, source, source.Chapters[0].Id);
        Assert.Equal(1, first.Version);
        Assert.Equal([source.Chapters[0].Id], first.AnalyzedChapterIds);
        Assert.Equal([source.Chapters[0].Id], first.Characters.Single().ChapterIds);

        var second = await AnalyzeChapterAsync(client, projectId, source, source.Chapters[1].Id);
        Assert.Equal(2, second.Version);
        Assert.Equal(source.Chapters.Select(item => item.Id), second.AnalyzedChapterIds);
        Assert.Equal(source.Chapters.Select(item => item.Id), second.Characters.Single().ChapterIds);
        Assert.Equal(2, second.PlotBeats.Count);

        var replaced = await AnalyzeChapterAsync(client, projectId, source, source.Chapters[1].Id);
        Assert.Equal(3, replaced.Version);
        Assert.Equal(2, replaced.PlotBeats.Count);
        Assert.Equal(source.Chapters.Select(item => item.Id), replaced.AnalyzedChapterIds);

        var restoreResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/assets/{first.AssetId}/versions/current",
            new { assetId = first.AssetId });
        restoreResponse.EnsureSuccessStatusCode();
        var afterRestore = await AnalyzeChapterAsync(client, projectId, source, source.Chapters[1].Id);
        Assert.Equal(4, afterRestore.Version);
        Assert.Equal(2, afterRestore.PlotBeats.Count);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        var versions = await dbContext.Assets
            .Where(item => item.ProjectId == projectId && item.Type == "story-material-analysis")
            .OrderBy(item => item.Version)
            .Select(item => item.Version)
            .ToArrayAsync();
        Assert.Equal([1, 2, 3, 4], versions);
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

        var confirmRoute = $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft/confirm";
        var confirmResponses = await Task.WhenAll(
            client.PostAsync(confirmRoute, null),
            client.PostAsync(confirmRoute, null));
        Assert.All(confirmResponses, response => response.EnsureSuccessStatusCode());
        var confirmations = await Task.WhenAll(confirmResponses.Select(
            response => response.Content.ReadFromJsonAsync<AdaptationScriptView>()));
        var confirmed = confirmations[0];
        Assert.NotNull(confirmed);
        Assert.All(confirmations, confirmation => Assert.Equal(confirmed.AssetId, confirmation?.AssetId));
        Assert.Equal("draft", confirmed.Status);
        Assert.Single(confirmed.ProductionEpisodeIds);

        var package = await client.GetFromJsonAsync<ProductionScriptPackageView>(
            $"/api/v2/projects/{projectId}/production-episodes/{confirmed.ProductionEpisodeIds[0]}/script-package");
        Assert.NotNull(package);
        Assert.Equal(source.Id, package.SourceResourceId);
        Assert.Equal(confirmed.ProductionEpisodeIds[0], package.ProductionEpisodeId);
        Assert.Equal(confirmed.AssetId, package.AdaptationScriptAssetId);
        Assert.Equal(draft.Episodes[0].Title, package.Episode.Title);
        Assert.NotEmpty(package.Episode.Scenes);
        Assert.All(package.Episode.Scenes, scene =>
        {
            Assert.False(string.IsNullOrWhiteSpace(scene.Action));
            Assert.NotEmpty(scene.Dialogues);
            Assert.NotEmpty(scene.ShotPlan);
            var dialogueLines = scene.Dialogues.SelectMany(dialogue => dialogue.Lines).ToArray();
            Assert.All(dialogueLines, line => Assert.True(
                line.Count(character => !char.IsWhiteSpace(character)) <= 32));
            Assert.True(
                dialogueLines.Sum(line => line.Count(character => !char.IsWhiteSpace(character)))
                    <= Math.Floor(scene.TargetSeconds * 3.2));
        });
        Assert.Equal(
            package.Episode.TargetSeconds,
            package.Episode.Scenes.SelectMany(scene => scene.ShotPlan).Sum(shot => shot.DurationSeconds));

        await using (var legacyScope = factory.Services.CreateAsyncScope())
        {
            var legacyDbContext = legacyScope.ServiceProvider.GetRequiredService<V2DbContext>();
            var legacyAsset = await legacyDbContext.Assets.SingleAsync(item => item.Id == package.AssetId);
            legacyAsset.DocumentJson = JsonSerializer.Serialize(new
            {
                AdaptationScriptAssetId = confirmed.AssetId,
                ProductionEpisodeId = confirmed.ProductionEpisodeIds[0],
                Episode = draft.Episodes[0]
            });
            await legacyDbContext.SaveChangesAsync();
        }
        var legacyPackage = await client.GetFromJsonAsync<ProductionScriptPackageView>(
            $"/api/v2/projects/{projectId}/production-episodes/{confirmed.ProductionEpisodeIds[0]}/script-package");
        Assert.NotNull(legacyPackage);
        Assert.Equal(source.Id, legacyPackage.SourceResourceId);
        Assert.True(legacyPackage.IsLegacyOutline);
        Assert.All(legacyPackage.Episode.Scenes, scene => Assert.Empty(scene.Dialogues));
        Assert.Equal(draft.Episodes[0].Scenes[0].DialogueNotes, legacyPackage.Episode.Scenes[0].DialogueIntent);

        var regenerateScriptResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{confirmed.ProductionEpisodeIds[0]}/script-package/regenerate",
            null);
        Assert.True(
            regenerateScriptResponse.IsSuccessStatusCode,
            await regenerateScriptResponse.Content.ReadAsStringAsync());
        var regeneratedPackage = await regenerateScriptResponse.Content
            .ReadFromJsonAsync<ProductionScriptPackageView>();
        Assert.NotNull(regeneratedPackage);
        Assert.Equal(package.ResourceId, regeneratedPackage.ResourceId);
        Assert.Equal(package.Version + 1, regeneratedPackage.Version);
        Assert.Equal(source.Id, regeneratedPackage.SourceResourceId);
        Assert.Equal(package.AdaptationScriptAssetId, regeneratedPackage.AdaptationScriptAssetId);
        Assert.False(regeneratedPackage.IsLegacyOutline);
        var currentPackage = await client.GetFromJsonAsync<ProductionScriptPackageView>(
            $"/api/v2/projects/{projectId}/production-episodes/{confirmed.ProductionEpisodeIds[0]}/script-package");
        Assert.Equal(regeneratedPackage.AssetId, currentPackage?.AssetId);

        var regenerateResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft/episodes/1/regenerate",
            new { instruction = "保留人物身份，改成更适合短剧节奏的主动冲突，不照搬原著。" });
        regenerateResponse.EnsureSuccessStatusCode();
        var regeneratedDraft = await regenerateResponse.Content.ReadFromJsonAsync<AdaptationScriptView>();
        Assert.NotNull(regeneratedDraft);
        Assert.Equal("draft", regeneratedDraft.Status);
        Assert.NotEqual(confirmed.AssetId, regeneratedDraft.AssetId);
        Assert.Equal(confirmed.ProductionEpisodeIds, regeneratedDraft.ProductionEpisodeIds);
        Assert.Equal(
            confirmed.ProductionEpisodeIds[0],
            Assert.IsAssignableFrom<IReadOnlyDictionary<int, Guid>>(
                regeneratedDraft.ProductionEpisodeMap)[1]);
        var unchangedPackage = await client.GetFromJsonAsync<ProductionScriptPackageView>(
            $"/api/v2/projects/{projectId}/production-episodes/{confirmed.ProductionEpisodeIds[0]}/script-package");
        Assert.Equal(confirmed.AssetId, unchangedPackage?.AdaptationScriptAssetId);

        var appendResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/chapters",
            new { content = "# 第三章\n达达尼昂得到接见。", fileName = "chapter-3.md" });
        appendResponse.EnsureSuccessStatusCode();
        var unchangedScript = await client.GetFromJsonAsync<AdaptationScriptView>(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft");
        Assert.NotNull(unchangedScript);
        Assert.Equal(regeneratedDraft.AssetId, unchangedScript.AssetId);
        Assert.Equal("draft", unchangedScript.Status);
        Assert.True(unchangedScript.HasNewerSourceVersion);

        await using var finalScope = factory.Services.CreateAsyncScope();
        var finalDbContext = finalScope.ServiceProvider.GetRequiredService<V2DbContext>();
        Assert.Equal(1, await finalDbContext.ProductionEpisodes.CountAsync());
        Assert.Equal(2, await finalDbContext.Assets.CountAsync(item => item.Type == "script-package"));
    }

    [Fact]
    public async Task Script_draft_automatically_plans_episode_count_and_appends_without_rewriting_existing_episodes()
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
        Assert.Equal(AdaptationModes.Rearranged, draft.Mode);
        Assert.Equal(2, draft.Episodes.Count);
        Assert.Empty(draft.OverallSmallHooks);
        Assert.Empty(draft.OverallBigHooks);
        Assert.All(draft.Episodes, episode =>
        {
            Assert.NotEmpty(episode.SmallHooks!);
            Assert.NotEmpty(episode.BigHooks!);
            var scene = Assert.Single(episode.Scenes);
            Assert.Null(scene.TargetSeconds);
            Assert.Null(scene.Rhythm);
            Assert.Null(scene.VisualContrast);
            Assert.Null(scene.ShotPlan);
        });
        var originalTitles = draft.Episodes.Select(item => item.Title).ToArray();

        var appendResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft/episodes",
            new { instruction = "增加一集承上启下的追逐" });
        appendResponse.EnsureSuccessStatusCode();
        var appended = await appendResponse.Content.ReadFromJsonAsync<AdaptationScriptView>();
        Assert.NotNull(appended);
        Assert.Equal(2, appended.Version);
        Assert.Equal(3, appended.Episodes.Count);
        Assert.Equal(originalTitles, appended.Episodes.Take(2).Select(item => item.Title));
        Assert.Equal(3, appended.Episodes[2].ProposalNumber);
        Assert.Empty(appended.ProductionEpisodeIds);

        var regenerateResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft/episodes/2/regenerate",
            new { instruction = "把本集改编成以误会冲突为主、集尾反转的短剧，不要照搬原著。" });
        regenerateResponse.EnsureSuccessStatusCode();
        var regenerated = await regenerateResponse.Content.ReadFromJsonAsync<AdaptationScriptView>();
        Assert.NotNull(regenerated);
        Assert.Equal(3, regenerated.Version);
        Assert.Equal(3, regenerated.Episodes.Count);
        Assert.Equal(originalTitles[0], regenerated.Episodes[0].Title);
        Assert.NotEqual(originalTitles[1], regenerated.Episodes[1].Title);
        Assert.Equal(2, regenerated.Episodes[1].ProposalNumber);
        Assert.Equal(appended.Episodes[2].Title, regenerated.Episodes[2].Title);

        var restoreResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/assets/{draft.AssetId}/versions/current",
            new { assetId = draft.AssetId });
        restoreResponse.EnsureSuccessStatusCode();
        var regenerateHistoricalResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft/episodes/1/regenerate",
            new { instruction = "从这个历史方案重新改编第一集。" });
        regenerateHistoricalResponse.EnsureSuccessStatusCode();
        var regeneratedFromHistory = await regenerateHistoricalResponse.Content.ReadFromJsonAsync<AdaptationScriptView>();
        Assert.Equal(4, regeneratedFromHistory?.Version);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        Assert.False(await dbContext.ProductionEpisodes.AnyAsync());
        Assert.Equal(4, await dbContext.Assets.CountAsync(item => item.Type == "adaptation-script-draft"));
    }

    [Fact]
    public async Task Confirmed_adaptation_can_append_delete_all_and_generate_again()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources",
            new
            {
                title = "三个火枪手原著",
                description = "持续调整改编方案",
                content = "# 第一章\n达达尼昂离开故乡。\n\n# 第二章\n达达尼昂抵达巴黎。",
                fileName = "chapters-1-2.md"
            });
        createResponse.EnsureSuccessStatusCode();
        var source = Assert.IsType<ProjectSourceView>(
            await createResponse.Content.ReadFromJsonAsync<ProjectSourceView>());
        (await client.PostAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/analysis",
            null)).EnsureSuccessStatusCode();

        var generateResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft",
            new { mode = AdaptationModes.Rearranged, desiredEpisodeCount = 2 });
        generateResponse.EnsureSuccessStatusCode();
        var draft = Assert.IsType<AdaptationScriptView>(
            await generateResponse.Content.ReadFromJsonAsync<AdaptationScriptView>());
        var formalResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft/episodes/2/production-script",
            null);
        formalResponse.EnsureSuccessStatusCode();
        var withFormalScript = Assert.IsType<AdaptationScriptView>(
            await formalResponse.Content.ReadFromJsonAsync<AdaptationScriptView>());
        var productionEpisodeMap = Assert.IsAssignableFrom<IReadOnlyDictionary<int, Guid>>(
            withFormalScript.ProductionEpisodeMap);
        Assert.Equal(2, Assert.Single(productionEpisodeMap).Key);
        var productionEpisodeId = productionEpisodeMap[2];
        var package = await client.GetFromJsonAsync<ProductionScriptPackageView>(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/script-package");
        Assert.Equal(draft.Episodes[1].Title, package?.Episode.Title);

        var repeatedFormalResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft/episodes/2/production-script",
            null);
        repeatedFormalResponse.EnsureSuccessStatusCode();
        var repeatedFormal = Assert.IsType<AdaptationScriptView>(
            await repeatedFormalResponse.Content.ReadFromJsonAsync<AdaptationScriptView>());
        Assert.Equal(withFormalScript.AssetId, repeatedFormal.AssetId);

        var regenerateFormalResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/script-package/regenerate",
            null);
        regenerateFormalResponse.EnsureSuccessStatusCode();
        var regeneratedFormal = Assert.IsType<ProductionScriptPackageView>(
            await regenerateFormalResponse.Content.ReadFromJsonAsync<ProductionScriptPackageView>());
        Assert.Equal(draft.Episodes[1].Title, regeneratedFormal.Episode.Title);

        var appendResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft/episodes",
            new { count = 1, instruction = "继续生成一个新章节" });
        appendResponse.EnsureSuccessStatusCode();
        var appended = Assert.IsType<AdaptationScriptView>(
            await appendResponse.Content.ReadFromJsonAsync<AdaptationScriptView>());
        Assert.Equal(3, appended.Episodes.Count);
        Assert.Equal("draft", appended.Status);
        Assert.Equal(
            productionEpisodeId,
            Assert.IsAssignableFrom<IReadOnlyDictionary<int, Guid>>(
                appended.ProductionEpisodeMap)[2]);

        var episode = appended.Episodes[1];
        var updateResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft/episodes/2",
            new
            {
                title = "手工修改的第二章",
                logline = "手工调整后的章节概要",
                sceneSummaries = episode.Scenes.Select((_, index) => $"手工修改节点 {index + 1}")
            });
        updateResponse.EnsureSuccessStatusCode();
        var updated = Assert.IsType<AdaptationScriptView>(
            await updateResponse.Content.ReadFromJsonAsync<AdaptationScriptView>());
        Assert.Equal("手工修改的第二章", updated.Episodes[1].Title);
        Assert.Equal("手工调整后的章节概要", updated.Episodes[1].Logline);
        Assert.All(updated.Episodes[1].Scenes, scene => Assert.StartsWith("手工修改节点", scene.Summary));
        Assert.Equal(
            productionEpisodeId,
            Assert.IsAssignableFrom<IReadOnlyDictionary<int, Guid>>(
                updated.ProductionEpisodeMap)[2]);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
            var packageAsset = await dbContext.Assets.SingleAsync(
                item => item.Id == regeneratedFormal.AssetId);
            packageAsset.DocumentJson = JsonSerializer.Serialize(new
            {
                AdaptationScriptAssetId = draft.AssetId,
                ProductionEpisodeId = productionEpisodeId,
                Episode = draft.Episodes[1]
            });
            await dbContext.SaveChangesAsync();
        }

        var regenerateUpdatedFormalResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/script-package/regenerate",
            null);
        regenerateUpdatedFormalResponse.EnsureSuccessStatusCode();
        var regeneratedUpdatedFormal = Assert.IsType<ProductionScriptPackageView>(
            await regenerateUpdatedFormalResponse.Content.ReadFromJsonAsync<ProductionScriptPackageView>());
        Assert.Equal(updated.AssetId, regeneratedUpdatedFormal.AdaptationScriptAssetId);
        Assert.Equal("手工修改的第二章", regeneratedUpdatedFormal.Episode.Title);

        var deleteResponse = await client.DeleteAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft/episodes/1");
        deleteResponse.EnsureSuccessStatusCode();
        var afterDelete = Assert.IsType<AdaptationScriptView>(
            await deleteResponse.Content.ReadFromJsonAsync<AdaptationScriptView>());
        Assert.Equal(2, afterDelete.Episodes.Count);
        Assert.Equal(
            productionEpisodeId,
            Assert.IsAssignableFrom<IReadOnlyDictionary<int, Guid>>(
                afterDelete.ProductionEpisodeMap)[1]);

        var clearResponse = await client.DeleteAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft/episodes");
        clearResponse.EnsureSuccessStatusCode();
        var empty = Assert.IsType<AdaptationScriptView>(
            await clearResponse.Content.ReadFromJsonAsync<AdaptationScriptView>());
        Assert.Empty(empty.Episodes);

        var regenerateResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft/episodes",
            new { count = 2, instruction = "从空方案重新生成两个章节" });
        regenerateResponse.EnsureSuccessStatusCode();
        var regenerated = Assert.IsType<AdaptationScriptView>(
            await regenerateResponse.Content.ReadFromJsonAsync<AdaptationScriptView>());
        Assert.Equal(2, regenerated.Episodes.Count);
        Assert.Equal([1, 2], regenerated.Episodes.Select(item => item.ProposalNumber));
    }

    [Fact]
    public async Task Script_draft_limits_each_batch_to_six_and_can_continue_generation()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        var settingsResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings",
            new
            {
                projectName = "三个火枪手",
                description = "经典文学改编",
                contentType = "系列短剧",
                targetAudience = "全年龄冒险故事观众",
                plannedEpisodeCount = 7,
                targetEpisodeSeconds = 100,
                aspectRatio = "16:9",
                outputWidth = 1920,
                outputHeight = 1080,
                visualStyle = "法式彩色冒险漫画",
                artDirection = "17 世纪法国质感与清晰墨线",
                characterDesign = "角色年龄、外貌和服装必须保持连续。",
                colorPalette = "宝石红、法国蓝、羊皮纸金",
                cameraLanguage = "动态漫画构图与低机位英雄镜头",
                soundStrategy = "管弦乐冒险主题与轻快喜剧节奏",
                imagePromptPrefix = "法式彩色冒险漫画，清晰墨线"
            });
        settingsResponse.EnsureSuccessStatusCode();
        var createResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources",
            new
            {
                title = "三个火枪手原著",
                description = "长篇系列改编参考",
                content = "# 第一章\n达达尼昂离开故乡。\n\n# 第二章\n达达尼昂抵达巴黎。",
                fileName = "chapters-1-2.md"
            });
        createResponse.EnsureSuccessStatusCode();
        var source = Assert.IsType<ProjectSourceView>(
            await createResponse.Content.ReadFromJsonAsync<ProjectSourceView>());
        (await client.PostAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/analysis",
            null)).EnsureSuccessStatusCode();

        var generateResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft",
            new { instruction = "按项目设定生成完整系列草案" });

        generateResponse.EnsureSuccessStatusCode();
        var draft = Assert.IsType<AdaptationScriptView>(
            await generateResponse.Content.ReadFromJsonAsync<AdaptationScriptView>());
        Assert.Equal(6, draft.Episodes.Count);
        Assert.Equal(Enumerable.Range(1, 6), draft.Episodes.Select(item => item.ProposalNumber));

        var continueResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft/episodes",
            new { count = 1, instruction = "继续生成最后一集" });
        continueResponse.EnsureSuccessStatusCode();
        var continued = Assert.IsType<AdaptationScriptView>(
            await continueResponse.Content.ReadFromJsonAsync<AdaptationScriptView>());
        Assert.Equal(7, continued.Episodes.Count);
        Assert.Equal(Enumerable.Range(1, 7), continued.Episodes.Select(item => item.ProposalNumber));
    }

    [Fact]
    public async Task Source_chapter_mode_uses_original_chapters_without_hooks_and_supports_delete()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources",
            new
            {
                title = "三个火枪手原著",
                description = "按章节改编",
                content = "# 第一章\n达达尼昂离开故乡。\n\n# 第二章\n达达尼昂抵达巴黎。",
                fileName = "chapters-1-2.md"
            });
        createResponse.EnsureSuccessStatusCode();
        var source = Assert.IsType<ProjectSourceView>(
            await createResponse.Content.ReadFromJsonAsync<ProjectSourceView>());

        var generateResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft",
            new { mode = AdaptationModes.SourceChapters });
        generateResponse.EnsureSuccessStatusCode();
        var draft = Assert.IsType<AdaptationScriptView>(
            await generateResponse.Content.ReadFromJsonAsync<AdaptationScriptView>());
        Assert.Equal(AdaptationModes.SourceChapters, draft.Mode);
        Assert.Equal(source.Chapters.Select(item => item.Title), draft.Episodes.Select(item => item.Title));
        Assert.All(draft.Episodes, episode =>
        {
            Assert.Empty(episode.SmallHooks!);
            Assert.Empty(episode.BigHooks!);
            Assert.Single(episode.SourceChapterNumbers);
        });

        var deleteResponse = await client.DeleteAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft/episodes/1");
        deleteResponse.EnsureSuccessStatusCode();
        var deleted = Assert.IsType<AdaptationScriptView>(
            await deleteResponse.Content.ReadFromJsonAsync<AdaptationScriptView>());
        var remaining = Assert.Single(deleted.Episodes);
        Assert.Equal(1, remaining.ProposalNumber);
        Assert.Equal(source.Chapters[1].Title, remaining.Title);

        var confirmResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft/confirm",
            null);
        confirmResponse.EnsureSuccessStatusCode();
        var confirmed = Assert.IsType<AdaptationScriptView>(
            await confirmResponse.Content.ReadFromJsonAsync<AdaptationScriptView>());
        Assert.Single(confirmed.ProductionEpisodeIds);
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

    private static async Task<StoryMaterialAnalysisView> AnalyzeChapterAsync(
        HttpClient client,
        Guid projectId,
        ProjectSourceView source,
        Guid chapterId)
    {
        var response = await client.PostAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/chapters/{chapterId}/analysis",
            null);
        response.EnsureSuccessStatusCode();
        return Assert.IsType<StoryMaterialAnalysisView>(
            await response.Content.ReadFromJsonAsync<StoryMaterialAnalysisView>());
    }

    private sealed record CreatedProjectResponse(Guid Id);
}