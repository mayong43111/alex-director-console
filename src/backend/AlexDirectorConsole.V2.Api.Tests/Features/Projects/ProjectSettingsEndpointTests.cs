using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AlexDirectorConsole.V2.Api.Features.Projects.Generation;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.Projects.Versions;
using AlexDirectorConsole.V2.Api.Tests.Infrastructure;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AlexDirectorConsole.V2.Api.Tests.Features.Projects;

public sealed class ProjectSettingsEndpointTests(V2ApiFactory factory)
    : IClassFixture<V2ApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Get_returns_unsaved_defaults_for_an_existing_project()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);

        var response = await client.GetAsync($"/api/v2/projects/{projectId}/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await response.Content.ReadFromJsonAsync<ProjectSettingsResponse>();
        Assert.NotNull(settings);
        Assert.Equal(0, settings.Version);
        Assert.Equal("三个火枪手", settings.ProjectName);
        Assert.Equal(-1, settings.PlannedEpisodeCount);
        Assert.Equal("16:9", settings.AspectRatio);
        Assert.Equal(1920, settings.OutputWidth);
        Assert.Equal(1080, settings.OutputHeight);
        Assert.Equal("minimax-h3-fl2va", settings.VideoPromptModel);
    }

    [Fact]
    public async Task Put_creates_successive_asset_versions_and_updates_project_pointer()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);

        var firstResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings",
            ValidSettings("欧式冒险漫画"));
        var secondResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings",
            ValidSettings("法式彩色冒险漫画"));

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<ProjectSettingsResponse>();
        var second = await secondResponse.Content.ReadFromJsonAsync<ProjectSettingsResponse>();
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(1, first.Version);
        Assert.Equal(2, second.Version);
        Assert.Equal("法式彩色冒险漫画", second.VisualStyle);
        Assert.Equal("minimax-h3", second.VideoPromptModel);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        var assets = await dbContext.Assets
            .Where(item => item.ProjectId == projectId && item.Type == "creative-settings")
            .OrderBy(item => item.Version)
            .ToListAsync();
        var project = await dbContext.Projects.SingleAsync(item => item.Id == projectId);
        Assert.Equal(2, assets.Count);
        Assert.Equal([1, 2], assets.Select(item => item.Version));
        Assert.Single(assets.Select(item => item.ResourceId).Distinct());
        Assert.Single(assets.Select(item => item.Number).Distinct());
        Assert.Equal(assets[1].Id, project.CurrentCreativeSettingsId);
        Assert.All(assets, asset => Assert.Equal(2, asset.SchemaVersion));
        Assert.All(assets, asset => Assert.DoesNotContain("protagonistSpecies", asset.DocumentJson));
    }

    [Fact]
    public async Task Put_marks_current_required_dependents_stale_recursively()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        var firstResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings",
            ValidSettings("欧式冒险漫画"));
        firstResponse.EnsureSuccessStatusCode();

        var shot = CreateDependentAsset(projectId, 100, "storyboard-shot", "测试镜头");
        var frame = CreateDependentAsset(projectId, 101, "storyboard-first-frame", "测试首帧");
        var unrelated = CreateDependentAsset(projectId, 102, "storyboard-shot", "无关镜头");
        var historicalShot = CreateDependentAsset(projectId, 103, "storyboard-shot", "历史镜头 v1");
        var currentShot = CreateDependentAsset(projectId, 103, "storyboard-shot", "当前镜头 v2");
        currentShot.ResourceId = historicalShot.ResourceId;
        currentShot.Version = 2;
        var historicalDownstream = CreateDependentAsset(projectId, 104, "storyboard-first-frame", "依赖历史镜头的当前首帧");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
            var firstAssetId = (await dbContext.Projects.SingleAsync(item => item.Id == projectId))
                .CurrentCreativeSettingsId;
            Assert.NotNull(firstAssetId);
            dbContext.Assets.AddRange(shot, frame, unrelated, historicalShot, currentShot, historicalDownstream);
            dbContext.ResourceStates.AddRange(
                CreateState(projectId, shot),
                CreateState(projectId, frame),
                CreateState(projectId, unrelated),
                CreateState(projectId, currentShot),
                CreateState(projectId, historicalDownstream));
            dbContext.AssetDependencies.AddRange(
                CreateDependency(projectId, shot.Id, firstAssetId.Value),
                CreateDependency(projectId, frame.Id, shot.Id),
                CreateDependency(projectId, historicalShot.Id, firstAssetId.Value),
                CreateDependency(projectId, historicalDownstream.Id, historicalShot.Id));
            await dbContext.SaveChangesAsync();
        }

        var secondResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings",
            ValidSettings("法式彩色冒险漫画"));
        secondResponse.EnsureSuccessStatusCode();

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<V2DbContext>();
        var states = await verificationDb.ResourceStates
            .Where(item => item.ProjectId == projectId)
            .ToDictionaryAsync(item => item.ResourceId);
        Assert.True(states[shot.ResourceId].IsStale);
        Assert.True(states[frame.ResourceId].IsStale);
        Assert.False(states[unrelated.ResourceId].IsStale);
        Assert.False(states[currentShot.ResourceId].IsStale);
        Assert.True(states[historicalDownstream.ResourceId].IsStale);
        Assert.Contains("v1 更新为 v2", states[frame.ResourceId].StaleReason);
    }

    [Fact]
    public async Task Versions_can_restore_historical_settings_as_current()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        var firstResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings",
            ValidSettings("欧式冒险漫画"));
        var secondResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings",
            ValidSettings("法式彩色冒险漫画"));
        var first = await firstResponse.Content.ReadFromJsonAsync<ProjectSettingsResponse>();
        var second = await secondResponse.Content.ReadFromJsonAsync<ProjectSettingsResponse>();
        Assert.NotNull(first?.AssetId);
        Assert.NotNull(second?.AssetId);

        var versions = await client.GetFromJsonAsync<List<ResourceVersionView>>(
            $"/api/v2/projects/{projectId}/assets/{second.AssetId}/versions");
        Assert.Equal([2, 1], versions?.Select(item => item.Version));
        Assert.True(versions?[0].IsCurrent);

        var historicalVersion = await client.GetFromJsonAsync<ResourceVersionDetailView>(
            $"/api/v2/projects/{projectId}/assets/{second.AssetId}/versions/{first.AssetId}");
        Assert.NotNull(historicalVersion);
        Assert.Equal(1, historicalVersion.Version);
        Assert.False(historicalVersion.IsCurrent);
        using var historicalDocument = JsonDocument.Parse(historicalVersion.DocumentJson!);
        Assert.Equal(
            "欧式冒险漫画",
            historicalDocument.RootElement.GetProperty("visualStyle").GetString());

        var switchResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/assets/{second.AssetId}/versions/current",
            new { assetId = first.AssetId });
        switchResponse.EnsureSuccessStatusCode();

        var restored = await client.GetFromJsonAsync<ProjectSettingsResponse>(
            $"/api/v2/projects/{projectId}/settings");
        Assert.Equal(1, restored?.Version);
        Assert.Equal("欧式冒险漫画", restored?.VisualStyle);

        var removedApprovalResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/settings/approve",
            null);
        Assert.Equal(HttpStatusCode.NotFound, removedApprovalResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        var project = await dbContext.Projects.SingleAsync(item => item.Id == projectId);
        Assert.Equal(first.AssetId, project.CurrentCreativeSettingsId);
    }

    [Fact]
    public async Task Invalid_ratio_returns_400_without_creating_an_asset()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        var request = ValidSettings("欧式冒险漫画") with { AspectRatio = "4:3" };

        var response = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings",
            request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        Assert.False(await dbContext.Assets.AnyAsync(item => item.ProjectId == projectId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task Invalid_planned_episode_count_returns_400(int plannedEpisodeCount)
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        var request = ValidSettings("欧式冒险漫画") with
        {
            PlannedEpisodeCount = plannedEpisodeCount
        };

        var response = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings",
            request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Automatic_planned_episode_count_can_be_saved()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        var request = ValidSettings("欧式冒险漫画") with { PlannedEpisodeCount = -1 };

        var response = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings",
            request);

        response.EnsureSuccessStatusCode();
        var settings = await response.Content.ReadFromJsonAsync<ProjectSettingsResponse>();
        Assert.Equal(-1, settings?.PlannedEpisodeCount);
    }

    [Fact]
    public async Task Post_cover_writes_versioned_png_asset_and_exposes_content()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        var saveResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings",
            ValidSettings("法式彩色冒险漫画"));
        saveResponse.EnsureSuccessStatusCode();

        var rejectedResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings/cover",
            new { instruction = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, rejectedResponse.StatusCode);

        var firstPreviewResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings/cover/preview",
            new { instruction = (string?)null });
        firstPreviewResponse.EnsureSuccessStatusCode();
        var firstPreview = await factory.CompleteGenerationTaskAsync<ImageGenerationPreviewView>(firstPreviewResponse);
        Assert.NotNull(firstPreview);
        Assert.Equal("Agent-authored cinematic cover prompt v1", firstPreview.Prompt);
        Assert.False(string.IsNullOrWhiteSpace(firstPreview.PreviewHash));
        var firstWriterCall = Assert.Single(factory.ProjectCoverPromptWriterCalls);
        Assert.Equal("gpt-image-2", firstWriterCall.TargetImageModel);
        Assert.Null(firstWriterCall.PreviousPrompt);
        Assert.Null(firstWriterCall.Instruction);
        Assert.Equal(
            "法式彩色冒险漫画",
            firstWriterCall.ProjectContext.GetProperty("visualStyle").GetString());
        var firstResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings/cover",
            new
            {
                instruction = (string?)null,
                confirmedPrompt = firstPreview.Prompt,
                previewHash = firstPreview.PreviewHash
            });
        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        var first = await factory.CompleteGenerationTaskAsync<ProjectCoverResponse>(firstResponse);
        Assert.Single(factory.ProjectCoverPromptWriterCalls);

        var unchangedPreviewResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings/cover/preview",
            new { instruction = (string?)null });
        var unchangedPreview = await factory.CompleteGenerationTaskAsync<ImageGenerationPreviewView>(
            unchangedPreviewResponse);
        Assert.Equal(firstPreview.Prompt, unchangedPreview.Prompt);
        Assert.Single(factory.ProjectCoverPromptWriterCalls);

        const string revision = "强化三位犬类火枪手的动作姿态，减少背景人物";
        var secondPreviewResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings/cover/preview",
            new { instruction = revision });
        secondPreviewResponse.EnsureSuccessStatusCode();
        var secondPreview = await factory.CompleteGenerationTaskAsync<ImageGenerationPreviewView>(secondPreviewResponse);
        Assert.NotNull(secondPreview);
        Assert.Equal("Agent-authored cinematic cover prompt v2", secondPreview.Prompt);
        Assert.Equal(2, factory.ProjectCoverPromptWriterCalls.Count);
        var secondWriterCall = factory.ProjectCoverPromptWriterCalls[1];
        Assert.Equal(firstPreview.Prompt, secondWriterCall.PreviousPrompt);
        Assert.Equal(revision, secondWriterCall.Instruction);
        var secondResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings/cover",
            new
            {
                instruction = revision,
                confirmedPrompt = secondPreview.Prompt,
                previewHash = secondPreview.PreviewHash
            });

        Assert.Equal(HttpStatusCode.Accepted, secondResponse.StatusCode);
        var second = await factory.CompleteGenerationTaskAsync<ProjectCoverResponse>(secondResponse);
        Assert.Equal(2, factory.ProjectCoverPromptWriterCalls.Count);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(1, first.Version);
        Assert.Equal(2, second.Version);
        Assert.Equal("image/png", second.ContentType);

        var content = await client.GetByteArrayAsync(second.ContentUrl);
        Assert.True(content.Length > 8);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47 }, content[..4]);

        var settings = await client.GetFromJsonAsync<ProjectSettingsResponse>(
            $"/api/v2/projects/{projectId}/settings");
        Assert.NotNull(settings?.Cover);
        Assert.Equal(second.AssetId, settings.Cover.AssetId);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        var covers = await dbContext.Assets
            .Where(item => item.ProjectId == projectId && item.Type == "project-cover")
            .OrderBy(item => item.Version)
            .ToListAsync();
        Assert.Equal(2, covers.Count);
        Assert.Single(covers.Select(item => item.ResourceId).Distinct());
        Assert.All(covers, item => Assert.NotNull(item.BlobContent));
        using var metadata = JsonDocument.Parse(covers[1].GenerationMetadataJson!);
        Assert.Equal(revision, metadata.RootElement.GetProperty("instruction").GetString());
        Assert.Equal(secondPreview.Prompt, metadata.RootElement.GetProperty("prompt").GetString());
        Assert.Equal("medium", metadata.RootElement.GetProperty("parameters").GetProperty("quality").GetString());
        var savedReference = Assert.Single(metadata.RootElement.GetProperty("references").EnumerateArray());
        Assert.Equal(1, savedReference.GetProperty("version").GetInt32());
        Assert.Equal("uses-settings", savedReference.GetProperty("role").GetString());
        Assert.True(await dbContext.AssetDependencies.AnyAsync(item =>
            item.ConsumerAssetId == second.AssetId && item.Role == "uses-settings"));

        var switchCoverResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/assets/{second.AssetId}/versions/current",
            new { assetId = first.AssetId });
        switchCoverResponse.EnsureSuccessStatusCode();
        var switchedSettings = await client.GetFromJsonAsync<ProjectSettingsResponse>(
            $"/api/v2/projects/{projectId}/settings");
        Assert.Equal(first.AssetId, switchedSettings?.Cover?.AssetId);

        var resaveResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings",
            ValidSettings("法式彩色冒险漫画"));
        var resaved = await resaveResponse.Content.ReadFromJsonAsync<ProjectSettingsResponse>();
        Assert.Equal(first.AssetId, resaved?.Cover?.AssetId);
    }

    [Fact]
    public async Task Post_cover_accepts_prompt_edited_after_agent_preview()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        var saveResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings",
            ValidSettings("法式彩色冒险漫画"));
        saveResponse.EnsureSuccessStatusCode();
        var previewResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings/cover/preview",
            new { instruction = "增加男性角色" });
        var preview = await factory.CompleteGenerationTaskAsync<ImageGenerationPreviewView>(previewResponse);

        const string editedPrompt = "Manually edited final cover prompt";
        var response = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings/cover",
            new
            {
                instruction = "增加男性角色",
                confirmedPrompt = editedPrompt,
                previewHash = preview.PreviewHash
            });
        var cover = await factory.CompleteGenerationTaskAsync<ProjectCoverResponse>(response);

        Assert.Equal(1, cover.Version);
        Assert.Single(factory.ProjectCoverPromptWriterCalls);
        await using var scope = factory.Services.CreateAsyncScope();
        var metadataJson = await scope.ServiceProvider.GetRequiredService<V2DbContext>()
            .Assets.Where(item => item.Id == cover.AssetId)
            .Select(item => item.GenerationMetadataJson)
            .SingleAsync();
        using var metadata = JsonDocument.Parse(metadataJson!);
        Assert.Equal(editedPrompt, metadata.RootElement.GetProperty("prompt").GetString());
    }

    [Fact]
    public async Task Post_assist_returns_field_patch_without_saving_a_settings_version()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings/assist",
            new
            {
                field = "artDirection",
                currentValue = "清晰墨线",
                instruction = "强化法国冒险漫画质感",
                context = ValidSettings("法式彩色冒险漫画")
            });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = await factory.CompleteGenerationTaskAsync<ProjectSettingsAssistResponse>(response);
        Assert.NotNull(result);
        Assert.Equal("artDirection", result.Field);
        Assert.Equal("AI 优化：清晰墨线", result.Value);
        Assert.Equal("MAF HarnessAgent", result.Runtime);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        Assert.False(await dbContext.Assets.AnyAsync(
            item => item.ProjectId == projectId && item.Type == "creative-settings"));
    }

    [Fact]
    public async Task Settings_tool_updates_only_requested_fields_and_creates_a_version()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        var saveResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings",
            ValidSettings("法式彩色冒险漫画"));
        saveResponse.EnsureSuccessStatusCode();

        await using var scope = factory.Services.CreateAsyncScope();
        var tool = scope.ServiceProvider.GetRequiredService<IProjectSettingsToolService>();
        var updated = await tool.UpdateAsync(
            projectId,
            """{"characterDesign":"三位主人公均为拟人牛。","imagePromptPrefix":"法式彩色冒险漫画，拟人牛主角。"}""",
            CancellationToken.None);

        Assert.Equal(2, updated.Version);
        Assert.Equal("三位主人公均为拟人牛。", updated.CharacterDesign);
        Assert.Equal("法式彩色冒险漫画，拟人牛主角。", updated.ImagePromptPrefix);
        Assert.Equal("法式彩色冒险漫画", updated.VisualStyle);
        Assert.Equal("三个火枪手", updated.ProjectName);
    }

    private static Asset CreateDependentAsset(Guid projectId, int number, string type, string name) => new()
    {
        ProjectId = projectId,
        ResourceId = Guid.NewGuid(),
        Version = 1,
        Number = number,
        Type = type,
        Name = name,
        ContentType = "application/json",
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow
    };

    private static ResourceState CreateState(Guid projectId, Asset asset) => new()
    {
        ProjectId = projectId,
        ResourceId = asset.ResourceId,
        ResourceType = asset.Type,
        CurrentAssetId = asset.Id,
        LifecycleStatus = "draft",
        UpdatedAtUtc = DateTimeOffset.UtcNow
    };

    private static AssetDependency CreateDependency(Guid projectId, Guid consumerAssetId, Guid sourceAssetId) => new()
    {
        ProjectId = projectId,
        ConsumerAssetId = consumerAssetId,
        SourceAssetId = sourceAssetId,
        Role = "required-test-input",
        IsRequired = true,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    private static async Task<Guid> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v2/projects",
            new { name = "三个火枪手", description = "犬类英雄的漫画冒险" });
        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<CreatedProjectResponse>();
        return Assert.IsType<CreatedProjectResponse>(project).Id;
    }

    private static SaveSettingsRequest ValidSettings(string visualStyle) => new(
        "三个火枪手",
        "三只年轻猎犬为了荣誉与友谊踏上冒险。",
        "动画短剧",
        "全年龄家庭与冒险故事观众",
        3,
        100,
        "16:9",
        1920,
        1080,
        visualStyle,
        "17 世纪法国质感与清晰墨线",
        "所有主人公均为拟人犬，保留犬种轮廓与佩剑服装。",
        "宝石红、法国蓝、羊皮纸金",
        "动态漫画构图与低机位英雄镜头",
        "管弦乐冒险主题与轻快喜剧节奏",
        "法式彩色冒险漫画，拟人犬角色，清晰墨线",
        "minimax-h3");

    private sealed record CreatedProjectResponse(Guid Id);

    private sealed record SaveSettingsRequest(
        string ProjectName,
        string Description,
        string ContentType,
        string TargetAudience,
        int PlannedEpisodeCount,
        int TargetEpisodeSeconds,
        string AspectRatio,
        int OutputWidth,
        int OutputHeight,
        string VisualStyle,
        string ArtDirection,
        string CharacterDesign,
        string ColorPalette,
        string CameraLanguage,
        string SoundStrategy,
        string ImagePromptPrefix,
        string VideoPromptModel);

    private sealed record ProjectSettingsResponse(
        Guid ProjectId,
        int Version,
        string ProjectName,
        int PlannedEpisodeCount,
        string AspectRatio,
        int OutputWidth,
        int OutputHeight,
        string VisualStyle,
        string VideoPromptModel,
        ProjectCoverResponse? Cover,
        Guid? AssetId = null);

    private sealed record ProjectCoverResponse(
        Guid AssetId,
        int Version,
        string ContentType,
        string ContentUrl,
        DateTimeOffset CreatedAtUtc);

    private sealed record ProjectSettingsAssistResponse(
        string Field,
        string Value,
        string Model,
        string Runtime);
}