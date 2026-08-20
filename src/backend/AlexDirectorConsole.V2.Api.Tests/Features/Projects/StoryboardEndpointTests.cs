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
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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
    public async Task Local_comfyui_video_normalizes_project_resolution_and_allows_missing_last_frame()
    {
        using var client = factory.CreateClient();
        var (projectId, productionEpisodeId) = await CreateFormalScriptAsync(client);
        (await client.PostAsync(
            $"/api/v2/projects/{projectId}/visual-assets/import-story-materials",
            null)).EnsureSuccessStatusCode();
        var storyboardResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/generate",
            null);
        storyboardResponse.EnsureSuccessStatusCode();
        var storyboard = await storyboardResponse.Content.ReadFromJsonAsync<StoryboardView>();
        var shot = Assert.IsType<StoryboardShotView>(storyboard?.Shots[0]);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
            var project = await dbContext.Projects.SingleAsync(item => item.Id == projectId);
            var nextNumber = await dbContext.Assets.MaxAsync(item => item.Number) + 1;
            var settings = new Asset
            {
                ProjectId = projectId,
                ResourceId = Guid.NewGuid(),
                Version = 1,
                Number = nextNumber,
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
                    characterDesign = "保持拟人牛角色身份和服装一致",
                    colorPalette = "宝石红、法国蓝、羊皮纸金",
                    cameraLanguage = "动态漫画构图",
                    soundStrategy = "管弦乐冒险主题",
                    imagePromptPrefix = "清晰墨线，制作级细节"
                }),
                ContentType = "application/json",
                SizeBytes = 2,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.Assets.Add(settings);
            project.CurrentCreativeSettingsId = settings.Id;
            dbContext.ResourceStates.Add(new ResourceState
            {
                ProjectId = projectId,
                ResourceId = settings.ResourceId,
                ResourceType = "creative-settings",
                CurrentAssetId = settings.Id,
                LifecycleStatus = "active",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });

            var frame = new Asset
            {
                ProjectId = projectId,
                ProductionEpisodeId = productionEpisodeId,
                ResourceId = Guid.NewGuid(),
                Version = 1,
                Number = nextNumber + 1,
                Type = ShotFrameService.AssetType,
                Name = "测试首帧",
                BlobKey = $"test/{Guid.NewGuid():N}.png",
                BlobContent = [0x89, 0x50, 0x4e, 0x47],
                FileName = "first.png",
                ContentType = "image/png",
                SizeBytes = 4,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.Assets.Add(frame);
            dbContext.ResourceStates.Add(new ResourceState
            {
                ProjectId = projectId,
                ResourceId = frame.ResourceId,
                ResourceType = ShotFrameService.AssetType,
                CurrentAssetId = frame.Id,
                LifecycleStatus = "active",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            dbContext.AssetDependencies.Add(new AssetDependency
            {
                ProjectId = projectId,
                ConsumerAssetId = frame.Id,
                SourceAssetId = shot.AssetId,
                Role = "frame-for-shot",
                IsRequired = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        var configurationResponse = await client.PutAsJsonAsync(
            "/api/v2/system/comfyui-configuration",
            new { baseUrl = "http://127.0.0.1:8188", isEnabled = true });
        configurationResponse.EnsureSuccessStatusCode();
        var route = $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{shot.ResourceId}/video";
        var previewResponse = await client.PostAsync($"{route}/preview", null);
        previewResponse.EnsureSuccessStatusCode();
        var preview = await previewResponse.Content.ReadFromJsonAsync<ShotVideoPreview>();
        Assert.NotNull(preview);
        Assert.Equal((864, 480), (preview.Width, preview.Height));
        Assert.Null(preview.LastFrameAssetId);
        Assert.Equal(24, preview.Fps);

        var startResponse = await client.PostAsJsonAsync(
            $"{route}/start",
            new { confirmedPrompt = preview.Prompt, previewHash = preview.PreviewHash });
        Assert.Equal(HttpStatusCode.Accepted, startResponse.StatusCode);
        var started = await startResponse.Content.ReadFromJsonAsync<ShotVideoProductionView>();
        Assert.NotNull(started);

        ShotVideoProductionView? completed = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await using (var workerScope = factory.Services.CreateAsyncScope())
            {
                var service = workerScope.ServiceProvider.GetRequiredService<IShotVideoService>();
                await service.ProcessNextAsync(CancellationToken.None);
            }
            completed = await client.GetFromJsonAsync<ShotVideoProductionView>(route);
            if (completed?.Status == "completed") break;
        }
        Assert.NotNull(completed);
        Assert.Equal("completed", completed.Status);
        Assert.Equal(1, completed.Version);
        Assert.NotNull(completed.Url);
        var videoBytes = await client.GetByteArrayAsync(completed.Url);
        Assert.Equal("ftyp", Encoding.ASCII.GetString(videoBytes, 4, 4));

        var comfyUi = factory.Services.GetRequiredService<TestComfyUiVideoClient>();
        Assert.NotNull(comfyUi.LastSubmission);
        Assert.Null(comfyUi.LastSubmission.LastFrame);
        Assert.Equal((864, 480), (comfyUi.LastSubmission.Width, comfyUi.LastSubmission.Height));

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<V2DbContext>();
        var run = await verificationDb.ProductionRuns.SingleAsync(item => item.Id == started.RunId);
        var item = await verificationDb.ProductionRunItems.SingleAsync(candidate => candidate.RunId == run.Id);
        Assert.Equal(ShotVideoService.RunType, run.RunType);
        Assert.StartsWith("test-prompt-", item.ExternalJobId);
        var video = await verificationDb.Assets.SingleAsync(asset => asset.Id == item.OutputAssetId);
        Assert.Equal(ShotVideoService.AssetType, video.Type);
        using var metadata = JsonDocument.Parse(video.GenerationMetadataJson!);
        Assert.Equal(preview.Prompt, metadata.RootElement.GetProperty("prompt").GetString());
        Assert.False(metadata.RootElement.GetProperty("parameters").GetProperty("hasLastFrame").GetBoolean());
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
        Assert.Equal([40d, 60d], first.Shots.Select(item => item.DurationSeconds));
        Assert.Equal(["全景", "中景"], first.Shots.Select(item => item.ShotSize));
        Assert.Equal(["平视", "平视"], first.Shots.Select(item => item.CameraAngle));
        Assert.Equal(["固定", "缓慢推进"], first.Shots.Select(item => item.CameraMovement));
        Assert.Equal(
            [ShotProductionModes.DirectFirstFrame, ShotProductionModes.FirstLastContinuous],
            first.Shots.Select(item => item.ProductionMode));
        Assert.All(first.Shots, shot => Assert.False(string.IsNullOrWhiteSpace(shot.FrameStrategyReason)));
        Assert.All(first.Shots, shot => Assert.False(string.IsNullOrWhiteSpace(shot.FirstFrameDescription)));
        Assert.All(first.Shots, shot => Assert.False(string.IsNullOrWhiteSpace(shot.CutDescription)));
        Assert.Empty(first.Shots[0].LastFrameDescription);
        Assert.False(string.IsNullOrWhiteSpace(first.Shots[1].LastFrameDescription));
        Assert.All(first.Shots, shot => Assert.Equal(
            "达达尼昂攥紧推荐信，穿过拥挤的街道，抬头寻找特雷维尔府邸。",
            shot.Action));
        Assert.Contains("达达尼昂：巴黎，我来了。", first.Shots[1].Dialogue);
        Assert.Contains("达达尼昂：特雷维尔先生一定会见我。", first.Shots[1].Dialogue);
        var hooks = first.Shots.SelectMany(item => item.Hooks).ToArray();
        Assert.Equal(["small", "big"], hooks.Select(item => item.Type));
        Assert.Equal(["推荐信不翼而飞", "幕后势力首次现身"], hooks.Select(item => item.Description));
        var resourceIds = first.Shots.Select(item => item.ResourceId).ToArray();

        var getResponse = await client.GetFromJsonAsync<StoryboardView>(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard");
        Assert.NotNull(getResponse);
        Assert.Equal(first.ScriptPackageAssetId, getResponse.ScriptPackageAssetId);
        Assert.False(getResponse.IsStale);
        Guid[] firstBeatIds;
        await using (var firstScope = factory.Services.CreateAsyncScope())
        {
            var firstDb = firstScope.ServiceProvider.GetRequiredService<V2DbContext>();
            firstBeatIds = await firstDb.ShotBeatClaims
                .OrderBy(item => item.BeatId)
                .Select(item => item.BeatId)
                .ToArrayAsync();
        }
        Assert.Equal(2, firstBeatIds.Length);

        var secondResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/generate",
            null);
        secondResponse.EnsureSuccessStatusCode();
        var second = await secondResponse.Content.ReadFromJsonAsync<StoryboardView>();
        Assert.NotNull(second);
        Assert.Equal(2, second.Revision);
        Assert.Equal(resourceIds, second.Shots.Select(item => item.ResourceId));
        Assert.All(second.Shots, shot => Assert.Equal(2, shot.Version));

        var crossResourceResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/assets/{second.Shots[0].AssetId}/versions/current",
            new { assetId = first.Shots[1].AssetId });
        Assert.Equal(HttpStatusCode.NotFound, crossResourceResponse.StatusCode);

        var switchResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/assets/{second.Shots[0].AssetId}/versions/current",
            new { assetId = first.Shots[0].AssetId });
        switchResponse.EnsureSuccessStatusCode();
        var restored = await client.GetFromJsonAsync<StoryboardView>(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard");
        Assert.Equal(1, restored?.Shots.Single(item => item.ResourceId == first.Shots[0].ResourceId).Version);
        Assert.Equal(2, restored?.Shots.Single(item => item.ResourceId == first.Shots[1].ResourceId).Version);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        Assert.Equal(2, await dbContext.ShotDefinitions.CountAsync());
        Assert.Equal(4, await dbContext.Assets.CountAsync(item => item.Type == "storyboard-shot"));
        Assert.Equal(4, await dbContext.AssetDependencies.CountAsync(item => item.Role == "derived-from-script"));
        Assert.True(await dbContext.AssetDependencies.AnyAsync(item => item.Role.StartsWith("uses-")));
        var claims = await dbContext.ShotBeatClaims.OrderBy(item => item.BeatId).ToListAsync();
        Assert.Equal(firstBeatIds, claims.Select(item => item.BeatId));
        Assert.Equal(2, claims.Count);
        var currentShotAssetIds = await dbContext.ShotDefinitions.Select(item => item.ShotAssetId).ToArrayAsync();
        Assert.Contains(first.Shots[0].AssetId, currentShotAssetIds);
        Assert.All(claims, claim => Assert.Contains(claim.ShotAssetId, currentShotAssetIds));
        Assert.Equal(1, await dbContext.ProductionEpisodes.CountAsync());
    }

    [Fact]
    public async Task Shot_assets_can_be_updated_and_production_uses_analyzed_frame_mode()
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
        Assert.All(storyboard.Shots, shot => Assert.Equal(2, shot.LinkedAssets.Count));
        Assert.All(storyboard.Shots, shot => Assert.DoesNotContain(shot.LinkedAssets, item => item.Kind == "prop"));
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
            await dbContext.SaveChangesAsync();
        }

        var rejectedResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{firstShot.ResourceId}/production/start",
            new { confirmedPrompt = "尚未通过预检" });
        Assert.Equal(HttpStatusCode.BadRequest, rejectedResponse.StatusCode);
        await using (var failedValidationScope = factory.Services.CreateAsyncScope())
        {
            var failedValidationDb = failedValidationScope.ServiceProvider.GetRequiredService<V2DbContext>();
            var failedRun = Assert.Single(await failedValidationDb.ValidationRuns.ToListAsync());
            Assert.Equal("completed", failedRun.Status);
            var failedResults = await failedValidationDb.ValidationResults
                .Where(item => item.ValidationRunId == failedRun.Id)
                .ToListAsync();
            Assert.Equal(2, failedResults.Count);
            Assert.Contains(failedResults, item => item.GateId == "shot.references-complete"
                && item.Status == "fail"
                && item.Severity == "blocker");
        }

        await using (var referenceScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = referenceScope.ServiceProvider.GetRequiredService<V2DbContext>();
            var now = DateTimeOffset.UtcNow;
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
                    ReviewStatus = "active",
                    CreatedAtUtc = now
                });
                dbContext.AssetDependencies.Add(new AssetDependency
                {
                    ProjectId = projectId,
                    ConsumerAssetId = image.Id,
                    SourceAssetId = linkedAsset.AssetId,
                    Role = "reference-for",
                    IsRequired = true,
                    CreatedAtUtc = now
                });
            }
            await dbContext.SaveChangesAsync();
        }

        var stalePromptResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{firstShot.ResourceId}/production/start",
            new { confirmedPrompt = "过期的确认提示词" });
        Assert.Equal(HttpStatusCode.BadRequest, stalePromptResponse.StatusCode);
        await using (var stalePromptScope = factory.Services.CreateAsyncScope())
        {
            var stalePromptDb = stalePromptScope.ServiceProvider.GetRequiredService<V2DbContext>();
            Assert.Equal(1, await stalePromptDb.ValidationRuns.CountAsync());
            Assert.Equal(0, await stalePromptDb.ProductionRuns.CountAsync());
        }

        var directPreviewResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{firstShot.ResourceId}/production/preview",
            null);
        directPreviewResponse.EnsureSuccessStatusCode();
        var directPreview = await directPreviewResponse.Content.ReadFromJsonAsync<ImageGenerationPreviewView>();
        Assert.NotNull(directPreview);
        Assert.Equal(ShotProductionModes.DirectFirstFrame, directPreview.Parameters.ProductionMode);
        Assert.All(directPreview.References, reference => Assert.True(reference.Version > 0));
        var directResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{firstShot.ResourceId}/production/start",
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

        var continuousPreviewResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{secondShot.ResourceId}/production/preview",
            null);
        continuousPreviewResponse.EnsureSuccessStatusCode();
        var continuousPreview = await continuousPreviewResponse.Content.ReadFromJsonAsync<ImageGenerationPreviewView>();
        Assert.NotNull(continuousPreview);
        Assert.Equal(ShotProductionModes.FirstLastContinuous, continuousPreview.Parameters.ProductionMode);
        Assert.Contains("small: 推荐信不翼而飞", continuousPreview.Prompt);
        Assert.Contains("big: 幕后势力首次现身", continuousPreview.Prompt);
        var continuousResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{secondShot.ResourceId}/production/start",
            new { confirmedPrompt = continuousPreview.Prompt });
        continuousResponse.EnsureSuccessStatusCode();
        var continuousProduction = await continuousResponse.Content.ReadFromJsonAsync<ShotProductionView>();
        Assert.NotNull(continuousProduction);
        Assert.Equal(ShotProductionModes.FirstLastContinuous, continuousProduction.Mode);
        Assert.Equal(["first-frame", "last-frame"], continuousProduction.Stages);
        Assert.Equal("completed", continuousProduction.Status);
        Assert.NotNull(continuousProduction.OutputAssetId);
        Assert.NotNull(continuousProduction.OutputPrompt);
        Assert.NotNull(continuousProduction.LastFrameAssetId);
        Assert.NotNull(continuousProduction.LastFrameUrl);
        Assert.Contains("Required final-frame state", continuousProduction.LastFramePrompt);
        var lastFrameBytes = await client.GetByteArrayAsync(continuousProduction.LastFrameUrl);
        Assert.Equal([0x89, 0x50, 0x4e, 0x47], lastFrameBytes[..4]);

        var repeatResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{secondShot.ResourceId}/production/start",
            new { confirmedPrompt = continuousPreview.Prompt });
        repeatResponse.EnsureSuccessStatusCode();
        var repeated = await repeatResponse.Content.ReadFromJsonAsync<ShotProductionView>();
        Assert.NotNull(repeated);
        Assert.NotEqual(continuousProduction.RunId, repeated.RunId);
        Assert.NotEqual(continuousProduction.OutputAssetId, repeated.OutputAssetId);
        Assert.NotEqual(continuousProduction.LastFrameAssetId, repeated.LastFrameAssetId);
        Assert.Equal("completed", repeated.Status);

        var runs = await client.GetFromJsonAsync<ProductionRunView[]>(
            $"/api/v2/projects/{projectId}/production-runs?productionEpisodeId={productionEpisodeId}");
        Assert.NotNull(runs);
        Assert.Equal(3, runs.Length);
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
        Assert.Equal(3, await verificationDb.ProductionRuns.CountAsync());
        Assert.Equal(5, await verificationDb.ProductionRunItems.CountAsync());
        Assert.Equal(4, await verificationDb.ValidationRuns.CountAsync());
        Assert.Equal(8, await verificationDb.ValidationResults.CountAsync());
        var productionRuns = await verificationDb.ProductionRuns.ToListAsync();
        Assert.All(productionRuns, run => Assert.NotNull(run.PreflightValidationRunId));
        foreach (var productionRun in productionRuns)
        {
            var validationResults = await verificationDb.ValidationResults
                .Where(item => item.ValidationRunId == productionRun.PreflightValidationRunId)
                .ToListAsync();
            Assert.Equal(2, validationResults.Count);
            Assert.All(validationResults, result => Assert.Equal("pass", result.Status));
        }
        Assert.Equal(5, await verificationDb.Assets.CountAsync(item => item.Type == ShotFrameService.AssetType));
        Assert.All(
            await verificationDb.ProductionRunItems.ToListAsync(),
            item => Assert.NotNull(item.OutputAssetId));
        var continuousFirstV1 = await verificationDb.Assets.SingleAsync(
            item => item.Id == continuousProduction.OutputAssetId);
        var continuousFirstV2 = await verificationDb.Assets.SingleAsync(
            item => item.Id == repeated.OutputAssetId);
        Assert.Equal(continuousFirstV1.ResourceId, continuousFirstV2.ResourceId);
        Assert.Equal((1, 2), (continuousFirstV1.Version, continuousFirstV2.Version));
        var continuousLastV1 = await verificationDb.Assets.SingleAsync(
            item => item.Id == continuousProduction.LastFrameAssetId);
        var continuousLastV2 = await verificationDb.Assets.SingleAsync(
            item => item.Id == repeated.LastFrameAssetId);
        Assert.Equal(continuousLastV1.ResourceId, continuousLastV2.ResourceId);
        Assert.Equal((1, 2), (continuousLastV1.Version, continuousLastV2.Version));
        using var lastFrameMetadata = JsonDocument.Parse(continuousLastV1.GenerationMetadataJson!);
        Assert.Equal(
            continuousProduction.LastFramePrompt,
            lastFrameMetadata.RootElement.GetProperty("prompt").GetString());
        Assert.Equal("last-frame", lastFrameMetadata.RootElement.GetProperty("frameStage").GetString());
        var directOutput = await verificationDb.Assets.SingleAsync(item => item.Id == directProduction.OutputAssetId);
        using var directMetadata = JsonDocument.Parse(directOutput.GenerationMetadataJson!);
        Assert.Equal(directPreview.Prompt, directMetadata.RootElement.GetProperty("prompt").GetString());
        Assert.Equal(
            directPreview.References.Count,
            directMetadata.RootElement.GetProperty("references").GetArrayLength());
        Assert.All(
            directMetadata.RootElement.GetProperty("references").EnumerateArray(),
            reference => Assert.True(reference.GetProperty("version").GetInt32() > 0));

        var sourceRun = productionRuns.Single(item => item.Id == directProduction.RunId);
        var sourceItem = await verificationDb.ProductionRunItems.SingleAsync(
            item => item.RunId == directProduction.RunId);
        var inputAssetIds = JsonSerializer.Deserialize<Guid[]>(sourceItem.InputAssetIdsJson)!;
        var referenceImage = await verificationDb.Assets.FirstAsync(item =>
            inputAssetIds.Contains(item.Id) && item.Type == VisualReferenceService.AssetType);
        var referenceDependency = await verificationDb.AssetDependencies.SingleAsync(item =>
            item.ConsumerAssetId == referenceImage.Id && item.Role == "reference-for");
        var referencedSubject = await verificationDb.Assets.SingleAsync(
            item => item.Id == referenceDependency.SourceAssetId);
        var subjectState = await verificationDb.ResourceStates.SingleAsync(item =>
            item.ProjectId == projectId && item.ResourceId == referencedSubject.ResourceId);
        var updatedDocument = JsonNode.Parse(referencedSubject.DocumentJson!)!.AsObject();
        updatedDocument["name"] = $"{referencedSubject.Name}新版";
        var referencedSubjectV2 = new Asset
        {
            ProjectId = referencedSubject.ProjectId,
            ResourceId = referencedSubject.ResourceId,
            Version = referencedSubject.Version + 1,
            Number = referencedSubject.Number,
            Type = referencedSubject.Type,
            Name = $"{referencedSubject.Name}新版",
            DocumentJson = updatedDocument.ToJsonString(),
            ContentType = referencedSubject.ContentType,
            SizeBytes = referencedSubject.SizeBytes,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        verificationDb.Assets.Add(referencedSubjectV2);
        subjectState.CurrentAssetId = referencedSubjectV2.Id;

        var (snapshotRun, snapshotItem) = CloneFirstFrameRun(sourceRun, sourceItem);
        verificationDb.ProductionRuns.Add(snapshotRun);
        verificationDb.ProductionRunItems.Add(snapshotItem);
        await verificationDb.SaveChangesAsync();
        var snapshotService = new ShotFrameService(
            verificationDb,
            verificationScope.ServiceProvider.GetRequiredService<IShotFrameGenerator>(),
            TimeProvider.System);
        await snapshotService.GenerateFirstFrameAsync(
            snapshotRun.Id,
            directPreview.Prompt,
            CancellationToken.None);
        Assert.Equal("completed", snapshotItem.Status);
        Assert.NotNull(snapshotItem.OutputAssetId);

        var (cancelledRun, cancelledItem) = CloneFirstFrameRun(sourceRun, sourceItem);
        verificationDb.ProductionRuns.Add(cancelledRun);
        verificationDb.ProductionRunItems.Add(cancelledItem);
        await verificationDb.SaveChangesAsync();
        var cancellingService = new ShotFrameService(
            verificationDb,
            new CancellingShotFrameGenerator(),
            TimeProvider.System);
        await Assert.ThrowsAsync<OperationCanceledException>(() => cancellingService.GenerateFirstFrameAsync(
            cancelledRun.Id,
            directPreview.Prompt,
            CancellationToken.None));
        Assert.Equal("running", cancelledRun.Status);
        Assert.Equal("running", cancelledItem.Status);
        Assert.Null(cancelledRun.LastError);
        Assert.Null(cancelledRun.CompletedAtUtc);
        Assert.Null(cancelledItem.ErrorCode);
        Assert.Null(cancelledItem.ErrorDetail);
        Assert.Null(cancelledItem.CompletedAtUtc);
    }

    private static (ProductionRun Run, ProductionRunItem Item) CloneFirstFrameRun(
        ProductionRun sourceRun,
        ProductionRunItem sourceItem)
    {
        var now = DateTimeOffset.UtcNow;
        var run = new ProductionRun
        {
            ProjectId = sourceRun.ProjectId,
            ProductionEpisodeId = sourceRun.ProductionEpisodeId,
            ScriptPackageAssetId = sourceRun.ScriptPackageAssetId,
            CreativeSettingsAssetId = sourceRun.CreativeSettingsAssetId,
            Status = "queued",
            CurrentStage = "first-frame",
            SpecJson = sourceRun.SpecJson,
            OriginalInstruction = "测试固定输入快照重放。",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var item = new ProductionRunItem
        {
            RunId = run.Id,
            ProjectId = sourceItem.ProjectId,
            ProductionEpisodeId = sourceItem.ProductionEpisodeId,
            ShotResourceId = sourceItem.ShotResourceId,
            ShotAssetId = sourceItem.ShotAssetId,
            ShotName = sourceItem.ShotName,
            Stage = "first-frame",
            Status = "queued",
            InputAssetIdsJson = sourceItem.InputAssetIdsJson,
            CreatedAtUtc = now
        };
        return (run, item);
    }

    private sealed class CancellingShotFrameGenerator : IShotFrameGenerator
    {
        public Task<GeneratedShotFrame> GenerateAsync(
            string prompt,
            string size,
            IReadOnlyList<ShotFrameReference> references,
            CancellationToken cancellationToken) =>
            Task.FromException<GeneratedShotFrame>(new OperationCanceledException(cancellationToken));
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