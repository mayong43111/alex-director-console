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
    public async Task Dialogue_audio_is_generated_by_comfyui_and_saved_as_a_versioned_asset()
    {
        using var client = factory.CreateClient();
        var (projectId, productionEpisodeId) = await CreateFormalScriptAsync(client);
        var importResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/visual-assets/import-story-materials",
            null);
        importResponse.EnsureSuccessStatusCode();
        var visualAssets = await importResponse.Content.ReadFromJsonAsync<VisualAssetView[]>();
        var character = Assert.Single(visualAssets!, asset => asset.Kind == "character");
        var voiceResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/visual-assets/{character.ResourceId}/voice-profile",
            new
            {
                name = "测试角色普通话",
                designPrompt = "清晰自然的青年普通话声线，语速中等，情绪克制。",
                sampleText = "巴黎，我来了。",
                language = "Chinese",
                seed = 2718
            });
        voiceResponse.EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync(
            "/api/v2/system/comfyui-configuration",
            new { baseUrl = "http://127.0.0.1:8188", isEnabled = true })).EnsureSuccessStatusCode();
        var storyboardResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/generate",
            null);
        storyboardResponse.EnsureSuccessStatusCode();
        var storyboard = await factory.CompleteGenerationTaskAsync<StoryboardView>(storyboardResponse);
        var dialogueShot = Assert.Single(storyboard!.Shots, shot => !string.IsNullOrWhiteSpace(shot.Dialogue));
        Assert.NotNull(dialogueShot.DialogueAudio);

        var route = $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/batch/dialogue-audio";
        var result = await (await client.PostAsync(route, null)).Content.ReadFromJsonAsync<BatchStoryboardMediaResult>();
        Assert.Equal(0, result?.Generated);
        Assert.Equal(2, result?.Skipped);
        Assert.Equal(0, result?.Failed);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        var audio = Assert.Single(await dbContext.Assets
            .Where(item => item.ProjectId == projectId && item.Type == StoryboardDialogueAudioService.AssetType)
            .ToListAsync());
        Assert.Equal("audio/wav", audio.ContentType);
        Assert.NotNull(audio.BlobContent);
        Assert.True(await dbContext.AssetDependencies.AnyAsync(item =>
            item.ConsumerAssetId == audio.Id && item.Role == "dialogue-for-shot"));
        Assert.True(await dbContext.AssetDependencies.AnyAsync(item =>
            item.ConsumerAssetId == audio.Id && item.Role == "uses-voice-profile"));
        var content = await client.GetByteArrayAsync(
            $"/api/v2/projects/{projectId}/storyboard/dialogue-audio/{audio.Id}/content");
        Assert.Equal("RIFF"u8.ToArray(), content[..4]);

        var repeated = await (await client.PostAsync(route, null)).Content.ReadFromJsonAsync<BatchStoryboardMediaResult>();
        Assert.Equal(0, repeated?.Generated);
        Assert.Equal(2, repeated?.Skipped);
    }

    [Fact]
    public async Task Local_comfyui_video_normalizes_project_resolution_and_allows_missing_last_frame()
    {
        const string spokenDialogue = "达达尼昂（低声）：巴黎，我来了。\n达达尼昂（坚定地）：这次不会再离开。";
        using var client = factory.CreateClient();
        var (projectId, productionEpisodeId) = await CreateFormalScriptAsync(client);
        (await client.PostAsync(
            $"/api/v2/projects/{projectId}/visual-assets/import-story-materials",
            null)).EnsureSuccessStatusCode();
        var storyboardResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/generate",
            null);
        storyboardResponse.EnsureSuccessStatusCode();
        var storyboard = await factory.CompleteGenerationTaskAsync<StoryboardView>(storyboardResponse);
        var shot = Assert.IsType<StoryboardShotView>(storyboard?.Shots[0]);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
            var shotAsset = await dbContext.Assets.SingleAsync(item => item.Id == shot.AssetId);
            var shotDocument = JsonNode.Parse(shotAsset.DocumentJson!)!.AsObject();
            shotDocument["dialogue"] = spokenDialogue;
            shotAsset.DocumentJson = shotDocument.ToJsonString();
            var project = await dbContext.Projects.SingleAsync(item => item.Id == projectId);
            var nextNumber = await dbContext.Assets.MaxAsync(item => item.Number) + 1;
            var linkedAssetIds = await dbContext.ShotAssetLinks
                .Where(item => item.ProjectId == projectId && item.ShotResourceId == shot.ResourceId)
                .Select(item => item.AssetId)
                .ToArrayAsync();
            if (linkedAssetIds.Length == 0)
            {
                linkedAssetIds = await dbContext.AssetDependencies
                    .Where(item => item.ProjectId == projectId
                        && item.ConsumerAssetId == shot.AssetId
                        && item.Role.StartsWith("uses-"))
                    .Select(item => item.SourceAssetId)
                    .ToArrayAsync();
            }
            var linkedAssets = await dbContext.Assets
                .Where(item => linkedAssetIds.Contains(item.Id))
                .ToListAsync();
            var character = linkedAssets.Single(item =>
                JsonNode.Parse(item.DocumentJson!)?["kind"]?.GetValue<string>() == "character");
            var voiceProfile = new Asset
            {
                ProjectId = projectId,
                ResourceId = Guid.NewGuid(),
                Version = 1,
                Number = nextNumber,
                Type = "voice-profile",
                Name = "达达尼昂青年音色",
                DocumentJson = JsonSerializer.Serialize(new
                {
                    characterResourceId = character.ResourceId,
                    name = "达达尼昂青年音色",
                    designPrompt = "清亮的青年男声，坚定但略带初入巴黎的紧张感",
                    sampleText = "巴黎，我来了。",
                    language = "zh-CN",
                    seed = 2718,
                    provider = "local-qwen3-tts",
                    model = "qwen3-tts-1.7b-voice-design"
                }),
                ContentType = "application/json",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.Assets.Add(voiceProfile);
            dbContext.ResourceStates.Add(new ResourceState
            {
                ProjectId = projectId,
                ResourceId = voiceProfile.ResourceId,
                ResourceType = "voice-profile",
                CurrentAssetId = voiceProfile.Id,
                LifecycleStatus = "active",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            var settings = new Asset
            {
                ProjectId = projectId,
                ResourceId = Guid.NewGuid(),
                Version = 1,
                Number = nextNumber + 1,
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
                    imagePromptPrefix = "清晰墨线，制作级细节",
                    videoPromptModel = "minimax-h3"
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
                Number = nextNumber + 2,
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
            var hiddenLastFrame = new Asset
            {
                ProjectId = projectId,
                ProductionEpisodeId = productionEpisodeId,
                ResourceId = Guid.NewGuid(),
                Version = 1,
                Number = nextNumber + 3,
                Type = ShotFrameService.AssetType,
                Name = "历史尾帧",
                BlobKey = $"test/{Guid.NewGuid():N}.png",
                BlobContent = [0x89, 0x50, 0x4e, 0x47],
                FileName = "last.png",
                ContentType = "image/png",
                SizeBytes = 4,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.Assets.Add(hiddenLastFrame);
            dbContext.ResourceStates.Add(new ResourceState
            {
                ProjectId = projectId,
                ResourceId = hiddenLastFrame.ResourceId,
                ResourceType = ShotFrameService.AssetType,
                CurrentAssetId = hiddenLastFrame.Id,
                LifecycleStatus = "active",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            dbContext.AssetDependencies.Add(new AssetDependency
            {
                ProjectId = projectId,
                ConsumerAssetId = hiddenLastFrame.Id,
                SourceAssetId = shot.AssetId,
                Role = "last-frame-for-shot",
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
        const string videoInstruction = "动作节奏更克制，保持固定机位";
        var missingVideoPromptResponse = await client.PostAsync($"{route}/generate", null);
        Assert.Equal(HttpStatusCode.Accepted, missingVideoPromptResponse.StatusCode);
        Assert.Equal("failed", (await factory.FailGenerationTaskAsync(missingVideoPromptResponse)).Status);
        var previewResponse = await client.PostAsync($"{route}/preview?instruction={Uri.EscapeDataString(videoInstruction)}", null);
        previewResponse.EnsureSuccessStatusCode();
        var preview = await factory.CompleteGenerationTaskAsync<ShotVideoPreview>(previewResponse);
        Assert.NotNull(preview);
        Assert.Equal((864, 480), (preview.Width, preview.Height));
        Assert.Null(preview.LastFrameAssetId);
        Assert.Equal(24, preview.Fps);
        Assert.DoesNotContain(videoInstruction, preview.Prompt);
        Assert.Equal(videoInstruction, factory.LastShotVideoPromptAgentInput?.Instruction);
        Assert.Equal("minimax-h3", factory.LastShotVideoPromptAgentInput?.VideoPromptModel);
        Assert.StartsWith("For the target video, at 0.00 seconds", preview.Prompt);
        Assert.Contains("integrated_multimodal_description:", preview.Prompt);
        Assert.Contains("overall_soundscape:", preview.Prompt);
        Assert.Contains("non_diegetic_music: N/A", preview.Prompt);
        Assert.Matches(@"\(S\d+\).*says: <d>\[Chinese\] 巴黎，我来了。</d>", preview.Prompt);
        Assert.DoesNotContain("<d>[Chinese] 达达尼昂：", preview.Prompt, StringComparison.Ordinal);
        var speakerMatches = System.Text.RegularExpressions.Regex.Matches(
            preview.Prompt,
            @"\((S\d+)\).*?<d>\[Chinese\] (?:巴黎，我来了。|这次不会再离开。)</d>");
        Assert.Equal(2, speakerMatches.Count);
        Assert.Equal(speakerMatches[0].Groups[1].Value, speakerMatches[1].Groups[1].Value);
        Assert.Contains("performing with 低声", preview.Prompt);
        Assert.Contains("performing with 坚定地", preview.Prompt);
        Assert.Contains("absolute vocal silence", preview.Prompt);
        Assert.Contains("completely blank lower third", preview.Prompt);
        Assert.Contains("no readable glyphs anywhere", preview.Prompt);
        Assert.DoesNotContain("subtitle", preview.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("字幕", preview.Prompt, StringComparison.Ordinal);
        var agentCharacter = Assert.Single(factory.LastShotVideoPromptAgentInput?.Characters ?? []);
        Assert.Equal("达达尼昂青年音色", agentCharacter.VoiceName);
        Assert.Equal("清亮的青年男声，坚定但略带初入巴黎的紧张感", agentCharacter.VoiceDesignPrompt);
        Assert.Equal("zh-CN", agentCharacter.VoiceLanguage);
        Assert.Equal(2718, agentCharacter.VoiceSeed);

        var promptResponse = await client.PostAsJsonAsync(
            $"{route}/prompt",
            new { instruction = videoInstruction });
        promptResponse.EnsureSuccessStatusCode();
        var savedPrompt = await factory.CompleteGenerationTaskAsync<StoryboardMediaPromptView>(promptResponse);
        Assert.NotNull(savedPrompt);
        Assert.Equal(preview.Prompt, savedPrompt.Prompt);
        Assert.Equal(preview.PreviewHash, savedPrompt.PreviewHash);
        var storyboardWithPrompt = await client.GetFromJsonAsync<StoryboardView>(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard");
        Assert.Equal(savedPrompt.AssetId, storyboardWithPrompt?.Shots.Single(item => item.ResourceId == shot.ResourceId).VideoPrompt?.AssetId);

        var callsBeforeStart = factory.ShotVideoPromptAgentCallCount;
        var startResponse = await client.PostAsync($"{route}/generate", null);
        Assert.Equal(HttpStatusCode.Accepted, startResponse.StatusCode);
        Assert.Equal(callsBeforeStart, factory.ShotVideoPromptAgentCallCount);
        var started = await factory.CompleteGenerationTaskAsync<ShotVideoProductionView>(startResponse);
        Assert.NotNull(started);
        Assert.Equal(1, factory.Services.GetRequiredService<TestComfyUiVideoClient>().SubmissionCount);

        ShotVideoProductionView? completed = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await using (var workerScope = factory.Services.CreateAsyncScope())
            {
                var service = workerScope.ServiceProvider.GetRequiredService<IShotVideoService>();
                await service.ProcessAsync(started.RunId, CancellationToken.None);
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

        var batchPromptResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/batch/video-prompts",
            null);
        var batchPrompts = await factory.CompleteGenerationTaskAsync<BatchStoryboardMediaResult>(batchPromptResponse);
        Assert.NotNull(batchPrompts);
        Assert.Equal(1, batchPrompts.Skipped);
        Assert.Equal(1, batchPrompts.Failed);
        var batchVideoResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/batch/videos",
            null);
        var batchVideos = await factory.CompleteGenerationTaskAsync<BatchStoryboardMediaResult>(batchVideoResponse);
        Assert.NotNull(batchVideos);
        Assert.Equal(1, batchVideos.Skipped);
        Assert.Equal(1, batchVideos.Failed);

        var modeResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{shot.ResourceId}/mode",
            new { requiresLastFrame = shot.ProductionMode != ShotProductionModes.FirstLastContinuous });
        modeResponse.EnsureSuccessStatusCode();
        var updatedStoryboard = await modeResponse.Content.ReadFromJsonAsync<StoryboardView>();
        Assert.NotNull(updatedStoryboard);
        Assert.NotEqual(shot.AssetId, updatedStoryboard.Shots.Single(item => item.ResourceId == shot.ResourceId).AssetId);
        await using (var versionScope = factory.Services.CreateAsyncScope())
        {
            var versionDb = versionScope.ServiceProvider.GetRequiredService<V2DbContext>();
            var definition = await versionDb.ShotDefinitions.SingleAsync(item => item.ShotResourceId == shot.ResourceId);
            var videoItem = await versionDb.ProductionRunItems.SingleAsync(item => item.RunId == started.RunId);
            Assert.NotEqual(definition.ShotAssetId, videoItem.ShotAssetId);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route)).StatusCode);

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
    public async Task Shot_mode_and_agent_text_rewrite_create_new_versions()
    {
        using var client = factory.CreateClient();
        var (projectId, productionEpisodeId) = await CreateFormalScriptAsync(client);
        var generateResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/generate",
            null);
        generateResponse.EnsureSuccessStatusCode();
        var storyboard = await factory.CompleteGenerationTaskAsync<StoryboardView>(generateResponse);
        Assert.NotNull(storyboard);
        var shot = storyboard.Shots[0];
        Assert.Equal(ShotProductionModes.DirectFirstFrame, shot.ProductionMode);

        var modeResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{shot.ResourceId}/mode",
            new { requiresLastFrame = true });
        modeResponse.EnsureSuccessStatusCode();
        var withLastFrame = await modeResponse.Content.ReadFromJsonAsync<StoryboardView>();
        Assert.NotNull(withLastFrame);
        var modeShot = withLastFrame.Shots.Single(item => item.ResourceId == shot.ResourceId);
        Assert.Equal(shot.Version + 1, modeShot.Version);
        Assert.Equal(ShotProductionModes.FirstLastContinuous, modeShot.ProductionMode);
        Assert.False(string.IsNullOrWhiteSpace(modeShot.LastFrameDescription));

        const string instruction = "让首尾帧描述更突出人物迟疑和视线变化";
        var rewriteResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{shot.ResourceId}/rewrite",
            new { instruction });
        rewriteResponse.EnsureSuccessStatusCode();
        var rewritten = await rewriteResponse.Content.ReadFromJsonAsync<StoryboardView>();
        Assert.NotNull(rewritten);
        var rewrittenShot = rewritten.Shots.Single(item => item.ResourceId == shot.ResourceId);
        Assert.Equal(modeShot.Version + 1, rewrittenShot.Version);
        Assert.Contains(instruction, rewrittenShot.FirstFrameDescription);
        Assert.Contains(instruction, rewrittenShot.LastFrameDescription);

        var directResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{shot.ResourceId}/mode",
            new { requiresLastFrame = false });
        directResponse.EnsureSuccessStatusCode();
        var direct = await directResponse.Content.ReadFromJsonAsync<StoryboardView>();
        Assert.NotNull(direct);
        var directShot = direct.Shots.Single(item => item.ResourceId == shot.ResourceId);
        Assert.Equal(rewrittenShot.Version + 1, directShot.Version);
        Assert.Equal(ShotProductionModes.DirectFirstFrame, directShot.ProductionMode);
        Assert.Empty(directShot.LastFrameDescription);

        var claimedShot = storyboard.Shots.First(item => item.Hooks.Count > 0);
        var claimedModeResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{claimedShot.ResourceId}/mode",
            new { requiresLastFrame = claimedShot.ProductionMode != ShotProductionModes.FirstLastContinuous });
        claimedModeResponse.EnsureSuccessStatusCode();
        var claimedStoryboard = await claimedModeResponse.Content.ReadFromJsonAsync<StoryboardView>();
        Assert.NotNull(claimedStoryboard);
        var currentClaimedShot = claimedStoryboard.Shots.Single(item => item.ResourceId == claimedShot.ResourceId);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        var claims = await dbContext.ShotBeatClaims
            .Where(item => item.ShotResourceId == claimedShot.ResourceId)
            .ToListAsync();
        Assert.NotEmpty(claims);
        Assert.All(claims, claim => Assert.Equal(currentClaimedShot.AssetId, claim.ShotAssetId));
    }

    [Fact]
    public async Task Shot_text_fields_can_be_manually_edited()
    {
        using var client = factory.CreateClient();
        var (projectId, productionEpisodeId) = await CreateFormalScriptAsync(client);
        var generateResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/generate",
            null);
        generateResponse.EnsureSuccessStatusCode();
        var storyboard = await factory.CompleteGenerationTaskAsync<StoryboardView>(generateResponse);
        Assert.NotNull(storyboard);
        var original = storyboard.Shots[0];

        const string manualDescription = "手动调整后的镜头描述，只强调父子之间的视线。";
        var manualResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{original.ResourceId}/text/visualDescription",
            new { value = manualDescription });
        manualResponse.EnsureSuccessStatusCode();
        var manuallyEdited = await manualResponse.Content.ReadFromJsonAsync<StoryboardView>();
        Assert.NotNull(manuallyEdited);
        var manualShot = manuallyEdited.Shots.Single(item => item.ResourceId == original.ResourceId);
        Assert.Equal(original.Version + 1, manualShot.Version);
        Assert.Equal(manualDescription, manualShot.VisualDescription);
        Assert.Equal(original.FirstFrameDescription, manualShot.FirstFrameDescription);

        var setDialogueResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{original.ResourceId}/text/dialogue",
            new { value = "父亲：记住自己的名字。" });
        setDialogueResponse.EnsureSuccessStatusCode();
        var withDialogue = await setDialogueResponse.Content.ReadFromJsonAsync<StoryboardView>();
        Assert.NotNull(withDialogue);
        var withDialogueShot = withDialogue.Shots.Single(item => item.ResourceId == original.ResourceId);

        var clearDialogueResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{original.ResourceId}/text/dialogue",
            new { value = "  " });
        clearDialogueResponse.EnsureSuccessStatusCode();
        var cleared = await clearDialogueResponse.Content.ReadFromJsonAsync<StoryboardView>();
        Assert.NotNull(cleared);
        var clearedShot = cleared.Shots.Single(item => item.ResourceId == original.ResourceId);
        Assert.Equal(withDialogueShot.Version + 1, clearedShot.Version);
        Assert.Empty(clearedShot.Dialogue);
        Assert.Equal(manualShot.FirstFrameDescription, clearedShot.FirstFrameDescription);

        var invalidFieldResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{original.ResourceId}/text/unknownField",
            new { value = "不应保存" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidFieldResponse.StatusCode);
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
        var first = await factory.CompleteGenerationTaskAsync<StoryboardView>(firstResponse);
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
        var second = await factory.CompleteGenerationTaskAsync<StoryboardView>(secondResponse);
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
        var storyboard = await factory.CompleteGenerationTaskAsync<StoryboardView>(generateResponse);
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

        const string imageInstruction = "人物表情更克制，晨光更柔和";
        var directRoute = $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{firstShot.ResourceId}/image";
        var missingImagePromptResponse = await client.PostAsync($"{directRoute}/generate", null);
        Assert.Equal(HttpStatusCode.Accepted, missingImagePromptResponse.StatusCode);
        Assert.Equal("failed", (await factory.FailGenerationTaskAsync(missingImagePromptResponse)).Status);
        var directPreviewResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{firstShot.ResourceId}/production/preview?instruction={Uri.EscapeDataString(imageInstruction)}",
            null);
        directPreviewResponse.EnsureSuccessStatusCode();
        var directPreview = await factory.CompleteGenerationTaskAsync<ImageGenerationPreviewView>(directPreviewResponse);
        Assert.NotNull(directPreview);
        Assert.Equal(ShotProductionModes.DirectFirstFrame, directPreview.Parameters.ProductionMode);
        Assert.Contains(imageInstruction, directPreview.Prompt);
        Assert.All(directPreview.References, reference => Assert.True(reference.Version > 0));
        var directPromptResponse = await client.PostAsJsonAsync(
            $"{directRoute}/prompt",
            new { instruction = imageInstruction });
        directPromptResponse.EnsureSuccessStatusCode();
        var directPrompt = await factory.CompleteGenerationTaskAsync<StoryboardMediaPromptView>(directPromptResponse);
        Assert.NotNull(directPrompt);
        Assert.Equal(directPreview.Prompt, directPrompt.Prompt);
        var storyboardWithImagePrompt = await client.GetFromJsonAsync<StoryboardView>(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard");
        Assert.Equal(directPrompt.AssetId, storyboardWithImagePrompt?.Shots.Single(item => item.ResourceId == firstShot.ResourceId).ImagePrompt?.AssetId);
        var directResponse = await client.PostAsync($"{directRoute}/generate", null);
        directResponse.EnsureSuccessStatusCode();
        var directProduction = await factory.CompleteGenerationTaskAsync<ShotProductionView>(directResponse);
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
        var continuousPreview = await factory.CompleteGenerationTaskAsync<ImageGenerationPreviewView>(continuousPreviewResponse);
        Assert.NotNull(continuousPreview);
        Assert.Equal(ShotProductionModes.FirstLastContinuous, continuousPreview.Parameters.ProductionMode);
        Assert.Contains("small: 推荐信不翼而飞", continuousPreview.Prompt);
        Assert.Contains("big: 幕后势力首次现身", continuousPreview.Prompt);
        var continuousRoute = $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{secondShot.ResourceId}/image";
        var continuousPromptResponse = await client.PostAsJsonAsync($"{continuousRoute}/prompt", new { instruction = (string?)null });
        continuousPromptResponse.EnsureSuccessStatusCode();
        await factory.CompleteGenerationTaskAsync<StoryboardMediaPromptView>(continuousPromptResponse);
        var continuousResponse = await client.PostAsync($"{continuousRoute}/generate", null);
        Assert.True(continuousResponse.IsSuccessStatusCode, await continuousResponse.Content.ReadAsStringAsync());
        var continuousProduction = await factory.CompleteGenerationTaskAsync<ShotProductionView>(continuousResponse);
        Assert.NotNull(continuousProduction);
        Assert.Equal(ShotProductionModes.FirstLastContinuous, continuousProduction.Mode);
        Assert.Equal(["first-frame", "last-frame"], continuousProduction.Stages);
        Assert.Equal("completed", continuousProduction.Status);
        Assert.NotNull(continuousProduction.OutputAssetId);
        Assert.NotNull(continuousProduction.OutputPrompt);
        Assert.NotNull(continuousProduction.LastFrameAssetId);
        Assert.NotNull(continuousProduction.LastFrameUrl);
        Assert.Contains("Model-aware gpt-image-2 last-frame", continuousProduction.LastFramePrompt);
        Assert.Equal("last-frame", factory.LastShotImagePromptAgentInput?.FrameStage);
        Assert.Equal("gpt-image-2", factory.LastShotImagePromptAgentInput?.ImageModel);
        var lastFrameCall = Assert.Single(factory.ShotFrameCalls.Where(call =>
            call.References.Any(reference => reference.SubjectType == "first-frame")));
        var generatedFirstFrameReference = Assert.Single(lastFrameCall.References.Where(reference =>
            reference.SubjectType == "first-frame"));
        Assert.Equal(continuousProduction.OutputAssetId, generatedFirstFrameReference.AssetId);
        var lastFrameBytes = await client.GetByteArrayAsync(continuousProduction.LastFrameUrl);
        Assert.Equal([0x89, 0x50, 0x4e, 0x47], lastFrameBytes[..4]);

        var imagePromptBatchResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/batch/image-prompts",
            null);
        var imagePromptBatch = await factory.CompleteGenerationTaskAsync<BatchStoryboardMediaResult>(imagePromptBatchResponse);
        Assert.NotNull(imagePromptBatch);
        Assert.Equal((0, 2, 0), (imagePromptBatch.Generated, imagePromptBatch.Skipped, imagePromptBatch.Failed));
        var selectedImagePromptBatchResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/batch/image-prompts",
            new { shotResourceIds = new[] { secondShot.ResourceId } });
        var selectedImagePromptBatch = await factory.CompleteGenerationTaskAsync<BatchStoryboardMediaResult>(selectedImagePromptBatchResponse);
        Assert.NotNull(selectedImagePromptBatch);
        Assert.Equal((1, 0, 0), (selectedImagePromptBatch.Generated, selectedImagePromptBatch.Skipped, selectedImagePromptBatch.Failed));
        var imageBatchResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/batch/images",
            null);
        var imageBatch = await factory.CompleteGenerationTaskAsync<BatchStoryboardMediaResult>(imageBatchResponse);
        Assert.NotNull(imageBatch);
        Assert.Equal((0, 2, 0), (imageBatch.Generated, imageBatch.Skipped, imageBatch.Failed));

        var repeatPreviewResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{secondShot.ResourceId}/production/preview",
            null);
        repeatPreviewResponse.EnsureSuccessStatusCode();
        var repeatPreview = await factory.CompleteGenerationTaskAsync<ImageGenerationPreviewView>(repeatPreviewResponse);
        Assert.NotNull(repeatPreview);
        Assert.Equal("generate-storyboard-last-frame", repeatPreview.Operation);
        Assert.Contains("Model-aware gpt-image-2 last-frame", repeatPreview.Prompt);
        var repeatPromptResponse = await client.PostAsJsonAsync(
            $"{continuousRoute}/prompt",
            new { instruction = (string?)null });
        repeatPromptResponse.EnsureSuccessStatusCode();
        var repeatPrompt = await factory.CompleteGenerationTaskAsync<StoryboardMediaPromptView>(repeatPromptResponse);
        Assert.Contains("Model-aware gpt-image-2 last-frame", repeatPrompt.Prompt);
        var repeatResponse = await client.PostAsync($"{continuousRoute}/generate", null);
        repeatResponse.EnsureSuccessStatusCode();
        var repeated = await factory.CompleteGenerationTaskAsync<ShotProductionView>(repeatResponse);
        Assert.NotNull(repeated);
        Assert.NotEqual(continuousProduction.RunId, repeated.RunId);
        Assert.Equal(continuousProduction.OutputAssetId, repeated.OutputAssetId);
        Assert.NotEqual(continuousProduction.LastFrameAssetId, repeated.LastFrameAssetId);
        Assert.Equal("completed", repeated.Status);

        var hideLastFrameResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{secondShot.ResourceId}/mode",
            new { requiresLastFrame = false });
        hideLastFrameResponse.EnsureSuccessStatusCode();
        var withoutLastFrame = await hideLastFrameResponse.Content.ReadFromJsonAsync<StoryboardView>();
        Assert.NotNull(withoutLastFrame);
        var directSecondShot = withoutLastFrame.Shots.Single(item => item.ResourceId == secondShot.ResourceId);
        Assert.Equal(repeated.OutputAssetId, directSecondShot.Production?.OutputAssetId);
        Assert.Null(directSecondShot.Production?.LastFrameAssetId);

        var restoreLastFrameResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/production-episodes/{productionEpisodeId}/storyboard/shots/{secondShot.ResourceId}/mode",
            new { requiresLastFrame = true });
        restoreLastFrameResponse.EnsureSuccessStatusCode();
        var withRestoredLastFrame = await restoreLastFrameResponse.Content.ReadFromJsonAsync<StoryboardView>();
        Assert.NotNull(withRestoredLastFrame);
        var continuousSecondShot = withRestoredLastFrame.Shots.Single(item => item.ResourceId == secondShot.ResourceId);
        Assert.Equal(repeated.OutputAssetId, continuousSecondShot.Production?.OutputAssetId);
        Assert.Equal(repeated.LastFrameAssetId, continuousSecondShot.Production?.LastFrameAssetId);

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
        Assert.Equal(4, await verificationDb.Assets.CountAsync(item => item.Type == ShotFrameService.AssetType));
        Assert.All(
            await verificationDb.ProductionRunItems.ToListAsync(),
            item => Assert.NotNull(item.OutputAssetId));
        var continuousFirstV1 = await verificationDb.Assets.SingleAsync(
            item => item.Id == continuousProduction.OutputAssetId);
        var continuousFirstV2 = await verificationDb.Assets.SingleAsync(
            item => item.Id == repeated.OutputAssetId);
        Assert.Equal(continuousFirstV1.ResourceId, continuousFirstV2.ResourceId);
        Assert.Equal((1, 1), (continuousFirstV1.Version, continuousFirstV2.Version));
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
        var firstFrameReference = Assert.Single(
            lastFrameMetadata.RootElement.GetProperty("references").EnumerateArray(),
            reference => reference.GetProperty("role").GetString() == "continues-from-first-frame");
        Assert.Equal(continuousProduction.OutputAssetId, firstFrameReference.GetProperty("assetId").GetGuid());
        Assert.True(await verificationDb.AssetDependencies.AnyAsync(dependency =>
            dependency.ConsumerAssetId == continuousProduction.LastFrameAssetId
                && dependency.SourceAssetId == continuousProduction.OutputAssetId
                && dependency.Role == "continues-from-first-frame"));
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
            verificationScope.ServiceProvider.GetRequiredService<IShotImagePromptAgent>(),
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
            verificationScope.ServiceProvider.GetRequiredService<IShotImagePromptAgent>(),
            TimeProvider.System);
        await Assert.ThrowsAsync<OperationCanceledException>(() => cancellingService.GenerateFirstFrameAsync(
            cancelledRun.Id,
            directPreview.Prompt,
            CancellationToken.None));
        Assert.Equal("failed", cancelledRun.Status);
        Assert.Equal("failed", cancelledItem.Status);
        Assert.Equal("The operation was canceled.", cancelledRun.LastError);
        Assert.NotNull(cancelledRun.CompletedAtUtc);
        Assert.Equal(nameof(OperationCanceledException), cancelledItem.ErrorCode);
        Assert.Equal("The operation was canceled.", cancelledItem.ErrorDetail);
        Assert.NotNull(cancelledItem.CompletedAtUtc);
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