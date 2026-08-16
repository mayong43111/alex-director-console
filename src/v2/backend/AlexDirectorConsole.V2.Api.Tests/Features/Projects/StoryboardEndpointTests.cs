using System.Net;
using System.Net.Http.Json;
using AlexDirectorConsole.V2.Api.Features.Projects;
using AlexDirectorConsole.V2.Api.Features.Projects.Assets;
using AlexDirectorConsole.V2.Api.Features.Projects.Generation;
using AlexDirectorConsole.V2.Api.Features.Projects.Production;
using AlexDirectorConsole.V2.Api.Features.Projects.Sources;
using AlexDirectorConsole.V2.Api.Features.Projects.Storyboard;
using AlexDirectorConsole.V2.Api.Tests.Infrastructure;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace AlexDirectorConsole.V2.Api.Tests.Features.Projects;

public sealed class StoryboardEndpointTests(V2ApiFactory factory)
    : IClassFixture<V2ApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Unknown_episode_has_no_storyboard()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);

        var response = await client.GetAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{Guid.NewGuid()}/storyboard");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Generate_creates_versioned_shots_from_formal_script()
    {
        using var client = factory.CreateClient();
        var (projectId, productionEpisodeId) = await CreateFormalScriptAsync(client);
        (await client.PostAsync(
            $"/api/v2/projects/{projectId}/visual-assets/import-story-materials",
            null)).EnsureSuccessStatusCode();

        var firstResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/generate",
            null);
        firstResponse.EnsureSuccessStatusCode();
        var first = await firstResponse.Content.ReadFromJsonAsync<StoryboardView>();
        Assert.NotNull(first);
        Assert.Equal(2, first.Shots.Count);
        Assert.Equal(first.TargetSeconds, first.TotalDurationSeconds);
        Assert.All(first.Shots, shot => Assert.Equal(1, shot.Version));
        Assert.Equal([1, 2], first.Shots.Select(item => item.ShotNumber));
        var hooks = first.Shots.SelectMany(item => item.Hooks).ToArray();
        Assert.Equal(["small", "big"], hooks.Select(item => item.Type));
        Assert.Equal(["推荐信不翼而飞", "幕后势力首次现身"], hooks.Select(item => item.Description));
        var resourceIds = first.Shots.Select(item => item.ResourceId).ToArray();

        var getResponse = await client.GetFromJsonAsync<StoryboardView>(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard");
        Assert.NotNull(getResponse);
        Assert.Equal(first.ScriptPackageAssetId, getResponse.ScriptPackageAssetId);
        Assert.False(getResponse.IsStale);

        var secondResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/generate",
            null);
        secondResponse.EnsureSuccessStatusCode();
        var second = await secondResponse.Content.ReadFromJsonAsync<StoryboardView>();
        Assert.NotNull(second);
        Assert.Equal(2, second.Revision);
        Assert.Equal(resourceIds, second.Shots.Select(item => item.ResourceId));
        Assert.All(second.Shots, shot => Assert.Equal(2, shot.Version));

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        Assert.Equal(2, await dbContext.ShotDefinitions.CountAsync());
        Assert.Equal(4, await dbContext.Assets.CountAsync(item => item.Type == "storyboard-shot"));
        Assert.Equal(4, await dbContext.AssetDependencies.CountAsync(item => item.Role == "derived-from-script"));
        Assert.True(await dbContext.AssetDependencies.AnyAsync(item => item.Role.StartsWith("uses-")));
        Assert.Equal(1, await dbContext.ProductionEpisodes.CountAsync());
    }

    [Fact]
    public async Task Shot_assets_can_be_updated_and_production_uses_duration_mode()
    {
        using var client = factory.CreateClient();
        var (projectId, productionEpisodeId) = await CreateFormalScriptAsync(client);
        var importResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/visual-assets/import-story-materials",
            null);
        importResponse.EnsureSuccessStatusCode();
        var visualAssets = await importResponse.Content.ReadFromJsonAsync<VisualAssetView[]>();
        Assert.NotNull(visualAssets);
        Assert.Equal(3, visualAssets.Length);

        var generateResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/generate",
            null);
        generateResponse.EnsureSuccessStatusCode();
        var storyboard = await generateResponse.Content.ReadFromJsonAsync<StoryboardView>();
        Assert.NotNull(storyboard);
        Assert.All(storyboard.Shots, shot => Assert.Equal(3, shot.LinkedAssets.Count));
        var firstShot = storyboard.Shots[0];
        var secondShot = storyboard.Shots[1];

        var selectedAssets = visualAssets.Where(item => item.Kind != "prop").ToArray();
        var updateResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{firstShot.ResourceId}/assets",
            new { assetResourceIds = selectedAssets.Select(item => item.ResourceId).ToArray() });
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<StoryboardView>();
        Assert.NotNull(updated);
        Assert.Equal(2, updated.Shots[0].LinkedAssets.Count);
        Assert.DoesNotContain(updated.Shots[0].LinkedAssets, item => item.Kind == "prop");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
            var now = DateTimeOffset.UtcNow;
            var settingsAsset = new Asset
            {
                ProjectId = projectId,
                ResourceId = Guid.NewGuid(),
                Version = 1,
                Number = 999,
                Type = "creative-settings",
                Name = "测试项目设定",
                DocumentJson = JsonSerializer.Serialize(new
                {
                    projectName = "三个火枪手",
                    description = "经典文学动画改编",
                    contentType = "动画短剧",
                    targetAudience = "全年龄观众",
                    plannedEpisodeCount = 1,
                    targetEpisodeSeconds = 100,
                    aspectRatio = "16:9",
                    outputWidth = 854,
                    outputHeight = 480,
                    visualStyle = "法式彩色冒险漫画",
                    artDirection = "17 世纪法国质感与清晰墨线",
                    protagonistSpecies = "牛类",
                    characterDesign = "保持拟人牛角色身份和服装一致",
                    colorPalette = "宝石红、法国蓝、羊皮纸金",
                    cameraLanguage = "动态漫画构图",
                    soundStrategy = "管弦乐冒险主题",
                    imagePromptPrefix = "清晰墨线，制作级细节"
                }),
                ContentType = "application/json",
                SizeBytes = 2,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            dbContext.Assets.Add(settingsAsset);
            var project = await dbContext.Projects.SingleAsync(item => item.Id == projectId);
            project.CurrentCreativeSettingsId = settingsAsset.Id;
            var secondDefinition = await dbContext.ShotDefinitions.SingleAsync(
                item => item.ShotResourceId == secondShot.ResourceId);
            secondDefinition.DurationSeconds = 15;
            var nextAssetNumber = await dbContext.Assets.MaxAsync(item => item.Number);
            foreach (var linkedAsset in updated.Shots.SelectMany(shot => shot.LinkedAssets)
                .Where(item => item.Kind is "character" or "scene")
                .DistinctBy(item => item.ResourceId))
            {
                var image = new Asset
                {
                    ProjectId = projectId,
                    ResourceId = Guid.NewGuid(),
                    Version = 1,
                    Number = ++nextAssetNumber,
                    Type = "visual-reference-image",
                    Name = $"{linkedAsset.Name}参考图",
                    BlobContent = [1, 2, 3],
                    ContentType = "image/png",
                    SizeBytes = 3,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                dbContext.Assets.Add(image);
                dbContext.VisualReferences.Add(new VisualReference
                {
                    ProjectId = projectId,
                    ImageAssetId = image.Id,
                    SubjectResourceId = linkedAsset.ResourceId,
                    SubjectType = linkedAsset.Kind,
                    Purpose = "generation-reference",
                    Source = "test",
                    ReviewStatus = "approved",
                    CreatedAtUtc = now
                });
            }
            await dbContext.SaveChangesAsync();
        }

        var rejectedResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{firstShot.ResourceId}/production/start",
            null);
        Assert.Equal(HttpStatusCode.BadRequest, rejectedResponse.StatusCode);

        var longPreviewResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{firstShot.ResourceId}/production/preview",
            null);
        longPreviewResponse.EnsureSuccessStatusCode();
        var longPreview = await longPreviewResponse.Content.ReadFromJsonAsync<ImageGenerationPreviewView>();
        Assert.NotNull(longPreview);
        Assert.Equal(ShotProductionModes.FirstLastContinuous, longPreview.Parameters.ProductionMode);
        Assert.All(longPreview.References, reference => Assert.True(reference.Version > 0));
        var longResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{firstShot.ResourceId}/production/start",
            new { confirmedPrompt = longPreview.Prompt });
        longResponse.EnsureSuccessStatusCode();
        var longProduction = await longResponse.Content.ReadFromJsonAsync<ShotProductionView>();
        Assert.NotNull(longProduction);
        Assert.Equal(ShotProductionModes.FirstLastContinuous, longProduction.Mode);
        Assert.Equal(["first-frame", "last-frame"], longProduction.Stages);

        var repeatResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{firstShot.ResourceId}/production/start",
            new { confirmedPrompt = longPreview.Prompt });
        repeatResponse.EnsureSuccessStatusCode();
        var repeated = await repeatResponse.Content.ReadFromJsonAsync<ShotProductionView>();
        Assert.Equal(longProduction.RunId, repeated!.RunId);

        var directPreviewResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{secondShot.ResourceId}/production/preview",
            null);
        directPreviewResponse.EnsureSuccessStatusCode();
        var directPreview = await directPreviewResponse.Content.ReadFromJsonAsync<ImageGenerationPreviewView>();
        Assert.NotNull(directPreview);
        Assert.Contains("small: 推荐信不翼而飞", directPreview.Prompt);
        Assert.Contains("big: 幕后势力首次现身", directPreview.Prompt);
        var directResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{secondShot.ResourceId}/production/start",
            new { confirmedPrompt = directPreview.Prompt });
        directResponse.EnsureSuccessStatusCode();
        var directProduction = await directResponse.Content.ReadFromJsonAsync<ShotProductionView>();
        Assert.NotNull(directProduction);
        Assert.Equal(ShotProductionModes.DirectFirstFrame, directProduction.Mode);
        Assert.Equal(["first-frame"], directProduction.Stages);
        Assert.Equal("completed", directProduction.Status);
        Assert.NotNull(directProduction.OutputAssetId);
        Assert.NotNull(directProduction.OutputUrl);
        var outputBytes = await client.GetByteArrayAsync(directProduction.OutputUrl);
        Assert.Equal([0x89, 0x50, 0x4e, 0x47], outputBytes[..4]);

        var runs = await client.GetFromJsonAsync<ProductionRunView[]>(
            $"/api/v2/projects/{projectId}/production-runs?productionEpisodeId={productionEpisodeId}");
        Assert.NotNull(runs);
        Assert.Equal(2, runs.Length);
        var directRun = Assert.Single(runs.Where(item => item.Id == directProduction.RunId));
        Assert.Equal("completed", directRun.Status);
        Assert.Equal(ShotProductionModes.DirectFirstFrame, directRun.Mode);
        var directItem = Assert.Single(directRun.Items);
        Assert.Equal("first-frame", directItem.Stage);
        Assert.Equal(directProduction.OutputAssetId, directItem.OutputAssetId);

        var detail = await client.GetFromJsonAsync<ProductionRunView>(
            $"/api/v2/projects/{projectId}/production-runs/{directProduction.RunId}");
        Assert.NotNull(detail);
        Assert.Equal(directProduction.RunId, detail.Id);
        Assert.Equal(directProduction.OutputUrl, Assert.Single(detail.Items).OutputUrl);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<V2DbContext>();
        Assert.Equal(2, await verificationDb.ShotAssetLinks.CountAsync());
        Assert.Equal(2, await verificationDb.ProductionRuns.CountAsync());
        Assert.Equal(3, await verificationDb.ProductionRunItems.CountAsync());
        Assert.Equal(2, await verificationDb.Assets.CountAsync(item => item.Type == ShotFrameService.AssetType));
        Assert.All(
            await verificationDb.ProductionRunItems.Where(item => item.Stage == "first-frame").ToListAsync(),
            item => Assert.NotNull(item.OutputAssetId));
        var directOutput = await verificationDb.Assets.SingleAsync(item => item.Id == directProduction.OutputAssetId);
        using var directMetadata = JsonDocument.Parse(directOutput.GenerationMetadataJson!);
        Assert.Equal(directPreview.Prompt, directMetadata.RootElement.GetProperty("prompt").GetString());
        Assert.Equal(
            directPreview.References.Count,
            directMetadata.RootElement.GetProperty("references").GetArrayLength());
        Assert.All(
            directMetadata.RootElement.GetProperty("references").EnumerateArray(),
            reference => Assert.True(reference.GetProperty("version").GetInt32() > 0));
    }

    private static async Task<(Guid ProjectId, Guid ProductionEpisodeId)> CreateFormalScriptAsync(HttpClient client)
    {
        var projectId = await CreateProjectAsync(client);
        var sourceResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources",
            new
            {
                title = "三个火枪手原著",
                content = "# 第一章\n达达尼昂离开故乡。\n\n# 第二章\n达达尼昂抵达巴黎。"
            });
        sourceResponse.EnsureSuccessStatusCode();
        var source = await sourceResponse.Content.ReadFromJsonAsync<ProjectSourceView>();
        Assert.NotNull(source);
        (await client.PostAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/analysis",
            null)).EnsureSuccessStatusCode();
        var draftResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft",
            new { desiredEpisodeCount = 1 });
        draftResponse.EnsureSuccessStatusCode();
        var confirmResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft/confirm",
            null);
        confirmResponse.EnsureSuccessStatusCode();
        var confirmed = await confirmResponse.Content.ReadFromJsonAsync<AdaptationScriptView>();
        Assert.NotNull(confirmed);
        return (projectId, Assert.Single(confirmed.ProductionEpisodeIds));
    }

    private static async Task<Guid> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v2/projects",
            new { name = "三个火枪手", description = "经典文学动画改编" });
        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<ProjectView>();
        Assert.NotNull(project);
        return project.Id;
    }
}