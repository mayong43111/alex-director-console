using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AlexDirectorConsole.V2.Api.Features.Projects;
using AlexDirectorConsole.V2.Api.Features.Projects.Assets;
using AlexDirectorConsole.V2.Api.Features.Projects.CreateProject;
using AlexDirectorConsole.V2.Api.Features.Projects.Sources;
using AlexDirectorConsole.V2.Api.Features.Projects.Voice;
using AlexDirectorConsole.V2.Api.Tests.Infrastructure;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace AlexDirectorConsole.V2.Api.Tests.Features.Projects;

public sealed class VisualAssetEndpointTests(V2ApiFactory factory)
    : IClassFixture<V2ApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task New_project_has_no_visual_assets()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);

        var assets = await client.GetFromJsonAsync<VisualAssetView[]>(
            $"/api/v2/projects/{projectId}/visual-assets");

        Assert.NotNull(assets);
        Assert.Empty(assets);
    }

    [Fact]
    public async Task Character_voice_profile_generates_a_versioned_local_wav_reference()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        var characterResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/visual-assets",
            new
            {
                kind = "character",
                name = "达达尼昂",
                summary = "年轻的加斯科涅冒险者",
                visualDescription = "棕色拟人犬，身形灵敏",
                mustKeep = new[] { "短耳", "旧佩剑" },
                avoid = Array.Empty<string>(),
                storyReferences = new[] { "第一章" }
            });
        characterResponse.EnsureSuccessStatusCode();
        var character = await characterResponse.Content.ReadFromJsonAsync<VisualAssetView>();
        Assert.NotNull(character);

        var saveResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/visual-assets/{character.ResourceId}/voice-profile",
            new
            {
                name = "达达尼昂标准音色",
                designPrompt = "二十岁左右的年轻男性，中音，清亮但略带粗粝，勇敢而冲动。",
                sampleText = "巴黎，我来了。特雷维尔先生一定会见我。",
                language = "Chinese",
                seed = 1701
            });
        var saveContent = await saveResponse.Content.ReadAsStringAsync();
        Assert.True(
            saveResponse.IsSuccessStatusCode,
            $"保存音色配置失败：{(int)saveResponse.StatusCode} {saveContent}");
        var profile = JsonSerializer.Deserialize<VoiceProfileView>(
            saveContent,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(profile);
        Assert.Equal(character.ResourceId, profile.CharacterResourceId);
        Assert.Equal(1, profile.Version);
        Assert.Null(profile.Reference);

        var generateResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/visual-assets/{character.ResourceId}/voice-profile/generate",
            null);
        generateResponse.EnsureSuccessStatusCode();
        var generated = await generateResponse.Content.ReadFromJsonAsync<VoiceProfileView>();
        Assert.NotNull(generated?.Reference);
        Assert.Equal("qwen3-tts-1.7b-voice-design-test", generated.Reference.Model);
        Assert.Equal("cpu", generated.Reference.Device);

        var content = await client.GetByteArrayAsync(generated.Reference.ContentUrl);
        Assert.True(content.Length >= 44);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(content, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(content, 8, 4));

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        var reference = await dbContext.Assets.SingleAsync(item => item.Id == generated.Reference.AssetId);
        Assert.Equal("voice-reference", reference.Type);
        Assert.True(await dbContext.AssetDependencies.AnyAsync(item =>
            item.ConsumerAssetId == reference.Id
            && item.SourceAssetId == generated.AssetId
            && item.Role == "uses-voice-profile"));
        Assert.True(await dbContext.AssetDependencies.AnyAsync(item =>
            item.ConsumerAssetId == reference.Id
            && item.SourceAssetId == character.AssetId
            && item.Role == "voices-character"));

        var audioAssets = await client.GetFromJsonAsync<AudioMaterialView[]>(
            $"/api/v2/projects/{projectId}/audio-assets");
        var voiceMaterial = Assert.Single(audioAssets!);
        Assert.Equal(generated.Reference.AssetId, voiceMaterial.AssetId);
        Assert.Equal("voice-reference", voiceMaterial.Kind);
        Assert.Equal("角色参考音", voiceMaterial.Source);
    }

    [Fact]
    public async Task Uploaded_audio_material_is_listed_and_streamed()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        var wave = new byte[44];
        "RIFF"u8.CopyTo(wave);
        "WAVE"u8.CopyTo(wave.AsSpan(8));
        BitConverter.GetBytes(24000).CopyTo(wave, 24);
        BitConverter.GetBytes(48000).CopyTo(wave, 28);
        BitConverter.GetBytes(0).CopyTo(wave, 40);
        using var content = new MultipartFormDataContent
        {
            { new StringContent("雨夜环境声"), "name" },
            { new ByteArrayContent(wave) { Headers = { ContentType = new("audio/wav") } }, "file", "rain.wav" }
        };

        var uploadResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/audio-assets",
            content);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<AudioMaterialView>();
        Assert.NotNull(uploaded);
        Assert.Equal("upload", uploaded.Kind);
        Assert.Equal("雨夜环境声", uploaded.Name);
        Assert.Equal("audio/wav", uploaded.ContentType);

        var listed = await client.GetFromJsonAsync<AudioMaterialView[]>(
            $"/api/v2/projects/{projectId}/audio-assets");
        var material = Assert.Single(listed!);
        Assert.Equal(uploaded.AssetId, material.AssetId);
        Assert.Equal(wave, await client.GetByteArrayAsync(material.ContentUrl));
    }

    [Fact]
    public async Task Updating_visual_asset_creates_a_new_version()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/visual-assets",
            new
            {
                kind = "character",
                name = "达达尼昂",
                summary = "年轻的加斯科涅冒险者",
                visualDescription = "棕色拟人牛，身形灵敏",
                mustKeep = new[] { "短角", "旧佩剑" },
                avoid = new[] { "人类头部" },
                storyReferences = new[] { "第一章" }
            });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<VisualAssetView>();
        Assert.NotNull(created);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/visual-assets/{created.ResourceId}",
            new
            {
                kind = "character",
                name = "达达尼昂",
                summary = "年轻的加斯科涅冒险者",
                visualDescription = "棕色拟人牛，短角，蓝色旧披风",
                mustKeep = new[] { "短角", "旧佩剑", "蓝色披风" },
                avoid = new[] { "人类头部" },
                storyReferences = new[] { "第一章", "第二章" }
            });
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<VisualAssetView>();
        Assert.NotNull(updated);
        Assert.Equal(created.ResourceId, updated.ResourceId);
        Assert.Equal(2, updated.Version);
        Assert.Equal(3, updated.MustKeep.Count);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        var versions = await dbContext.Assets
            .Where(item => item.ProjectId == projectId && item.Type == "visual-asset")
            .OrderBy(item => item.Version)
            .ToListAsync();
        Assert.Equal([1, 2], versions.Select(item => item.Version));
        Assert.Single(versions.Select(item => item.ResourceId).Distinct());
        Assert.False(await dbContext.ProductionEpisodes.AnyAsync());
    }

    [Fact]
    public async Task Updating_visual_asset_marks_its_current_required_consumer_stale()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/visual-assets",
            new
            {
                kind = "character",
                name = "达达尼昂",
                summary = "年轻的加斯科涅冒险者",
                visualDescription = "棕色拟人牛，身形灵敏",
                mustKeep = new[] { "短角" },
                avoid = Array.Empty<string>(),
                storyReferences = new[] { "第一章" }
            });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<VisualAssetView>();
        Assert.NotNull(created);

        var shot = new Asset
        {
            ProjectId = projectId,
            ResourceId = Guid.NewGuid(),
            Version = 1,
            Number = 100,
            Type = "storyboard-shot",
            Name = "依赖人物的镜头",
            ContentType = "application/json",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
            dbContext.Assets.Add(shot);
            dbContext.ResourceStates.Add(new ResourceState
            {
                ProjectId = projectId,
                ResourceId = shot.ResourceId,
                ResourceType = shot.Type,
                CurrentAssetId = shot.Id,
                LifecycleStatus = "draft",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            dbContext.AssetDependencies.Add(new AssetDependency
            {
                ProjectId = projectId,
                ConsumerAssetId = shot.Id,
                SourceAssetId = created.AssetId,
                Role = "uses-character",
                IsRequired = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/visual-assets/{created.ResourceId}",
            new
            {
                kind = "character",
                name = "达达尼昂",
                summary = "年轻的加斯科涅冒险者",
                visualDescription = "棕色拟人牛，蓝色旧披风",
                mustKeep = new[] { "短角", "蓝色披风" },
                avoid = Array.Empty<string>(),
                storyReferences = new[] { "第一章" }
            });
        updateResponse.EnsureSuccessStatusCode();

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<V2DbContext>();
        var state = await verificationDb.ResourceStates.SingleAsync(item => item.ResourceId == shot.ResourceId);
        Assert.True(state.IsStale);
        Assert.NotNull(state.StaleSinceUtc);
    }

    [Fact]
    public async Task Character_reference_generation_saves_and_binds_a_real_image_asset()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        (await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings",
            new
            {
                projectName = "三个火枪手",
                description = "经典文学动画改编",
                contentType = "动画短剧",
                targetAudience = "全年龄观众",
                plannedEpisodeCount = 3,
                targetEpisodeSeconds = 100,
                aspectRatio = "16:9",
                outputWidth = 854,
                outputHeight = 480,
                visualStyle = "法式彩色冒险漫画",
                artDirection = "17 世纪法国质感与清晰墨线",
                protagonistSpecies = "犬类",
                characterDesign = "保持角色身份和服装一致",
                colorPalette = "宝石红、法国蓝、羊皮纸金",
                cameraLanguage = "动态漫画构图",
                soundStrategy = "管弦乐冒险主题",
                imagePromptPrefix = "清晰墨线，制作级细节"
            })).EnsureSuccessStatusCode();
        var createResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/visual-assets",
            new
            {
                kind = "character",
                name = "达达尼昂",
                summary = "年轻的加斯科涅冒险者",
                visualDescription = "棕色拟人犬，身形灵敏",
                mustKeep = new[] { "短耳", "旧佩剑" },
                avoid = new[] { "人类头部" },
                storyReferences = new[] { "第一章" }
            });
        createResponse.EnsureSuccessStatusCode();
        var character = await createResponse.Content.ReadFromJsonAsync<VisualAssetView>();
        Assert.NotNull(character);

        var generateResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/visual-assets/{character.ResourceId}/reference/generate",
            null);

        generateResponse.EnsureSuccessStatusCode();
        var reference = await generateResponse.Content.ReadFromJsonAsync<VisualReferenceImageView>();
        Assert.NotNull(reference);
        Assert.Equal(character.ResourceId, reference.SubjectResourceId);
        Assert.Equal("character", reference.SubjectType);
        var content = await client.GetByteArrayAsync(reference.ContentUrl);
        Assert.True(content.Length > 8);
        Assert.Equal([0x89, 0x50, 0x4e, 0x47], content[..4]);
        using var bitmap = SKBitmap.Decode(content);
        Assert.NotNull(bitmap);
        Assert.Equal(1024, bitmap.Width);
        Assert.Equal(1024, bitmap.Height);
        Assert.Contains("left 55%", reference.Prompt);
        Assert.Contains("pure solid white", reference.Prompt);

        var listedAssets = await client.GetFromJsonAsync<VisualAssetView[]>(
            $"/api/v2/projects/{projectId}/visual-assets");
        var listedCharacter = Assert.Single(listedAssets!);
        Assert.Equal(reference.AssetId, listedCharacter.ReferenceImage?.AssetId);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        var binding = await dbContext.VisualReferences.SingleAsync();
        Assert.Equal(reference.AssetId, binding.ImageAssetId);
        Assert.Equal(character.ResourceId, binding.SubjectResourceId);
        Assert.Equal("generation-reference", binding.Purpose);
        var imageAsset = await dbContext.Assets.SingleAsync(item => item.Id == reference.AssetId);
        using var metadata = JsonDocument.Parse(imageAsset.GenerationMetadataJson!);
        Assert.Equal(
            "法式彩色冒险漫画",
            metadata.RootElement.GetProperty("projectStyle").GetProperty("visualStyle").GetString());
        Assert.Equal(1024, metadata.RootElement.GetProperty("outputWidth").GetInt32());
        Assert.Equal(1024, metadata.RootElement.GetProperty("outputHeight").GetInt32());
        Assert.Equal("medium", metadata.RootElement.GetProperty("parameters").GetProperty("quality").GetString());
        Assert.Equal(2, metadata.RootElement.GetProperty("references").GetArrayLength());
        Assert.All(
            metadata.RootElement.GetProperty("references").EnumerateArray(),
            item => Assert.True(item.GetProperty("version").GetInt32() > 0));
        Assert.True(await dbContext.AssetDependencies.AnyAsync(item =>
            item.ConsumerAssetId == reference.AssetId && item.SourceAssetId == character.AssetId));
        Assert.True(await dbContext.AssetDependencies.AnyAsync(item =>
            item.ConsumerAssetId == reference.AssetId && item.Role == "uses-settings"));

        var retryResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/visual-assets/{character.ResourceId}/reference/generate",
            null);
        retryResponse.EnsureSuccessStatusCode();
        var retried = await retryResponse.Content.ReadFromJsonAsync<VisualReferenceImageView>();
        Assert.NotNull(retried);
        Assert.Equal(reference.Version + 1, retried.Version);
        var imageVersions = await dbContext.Assets
            .Where(item => item.Type == "visual-reference-image")
            .OrderBy(item => item.Version)
            .ToArrayAsync();
        Assert.Equal([1, 2], imageVersions.Select(item => item.Version));
        Assert.Single(imageVersions.Select(item => item.ResourceId).Distinct());
    }

    [Theory]
    [InlineData("character", "left 55%")]
    [InlineData("scene", "upper 58%")]
    [InlineData("prop", "exactly one prop only")]
    public async Task Reference_generation_uses_the_required_square_layout_for_each_asset_kind(
        string kind,
        string expectedPromptRule)
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        (await client.PutAsJsonAsync(
            $"/api/v2/projects/{projectId}/settings",
            new
            {
                projectName = "设定图测试",
                description = "验证资产设定图",
                contentType = "动画短剧",
                targetAudience = "全年龄观众",
                plannedEpisodeCount = 1,
                targetEpisodeSeconds = 100,
                aspectRatio = "16:9",
                outputWidth = 854,
                outputHeight = 480,
                visualStyle = "法式彩色冒险漫画",
                artDirection = "清晰制作设定稿",
                protagonistSpecies = "拟人牛",
                characterDesign = "保持身份一致",
                colorPalette = "法国蓝与红色",
                cameraLanguage = "清晰构图",
                soundStrategy = "管弦乐",
                imagePromptPrefix = "制作级细节"
            })).EnsureSuccessStatusCode();
        var createResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/visual-assets",
            new
            {
                kind,
                name = "测试资产",
                summary = "用于验证构图",
                visualDescription = "清晰稳定的视觉定义",
                mustKeep = new[] { "身份一致" },
                avoid = Array.Empty<string>(),
                storyReferences = Array.Empty<string>()
            });
        createResponse.EnsureSuccessStatusCode();
        var asset = await createResponse.Content.ReadFromJsonAsync<VisualAssetView>();
        Assert.NotNull(asset);

        var generateResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/visual-assets/{asset.ResourceId}/reference/generate",
            null);

        generateResponse.EnsureSuccessStatusCode();
        var reference = await generateResponse.Content.ReadFromJsonAsync<VisualReferenceImageView>();
        Assert.NotNull(reference);
        Assert.Equal(kind, reference.SubjectType);
        Assert.Contains("1024x1024", reference.Prompt);
        Assert.Contains("pure solid white", reference.Prompt);
        Assert.Contains(expectedPromptRule, reference.Prompt);
        var content = await client.GetByteArrayAsync(reference.ContentUrl);
        using var bitmap = SKBitmap.Decode(content);
        Assert.NotNull(bitmap);
        Assert.Equal(1024, bitmap.Width);
        Assert.Equal(1024, bitmap.Height);
    }

    [Fact]
    public async Task Import_story_materials_creates_character_and_scene_once()
    {
        using var client = factory.CreateClient();
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
        var analysisResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/analysis",
            null);
        analysisResponse.EnsureSuccessStatusCode();
        var draftResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft",
            new { desiredEpisodeCount = 1 });
        draftResponse.EnsureSuccessStatusCode();

        var firstImport = await client.PostAsync(
            $"/api/v2/projects/{projectId}/visual-assets/import-story-materials",
            null);
        firstImport.EnsureSuccessStatusCode();
        var firstAssets = await firstImport.Content.ReadFromJsonAsync<VisualAssetView[]>();
        Assert.NotNull(firstAssets);
        Assert.Equal(3, firstAssets.Length);
        Assert.Contains(firstAssets, item => item.Kind == "character" && item.Name == "达达尼昂");
        Assert.Contains(firstAssets, item => item.Kind == "scene" && item.Name == "巴黎");
        Assert.Contains(firstAssets, item => item.Kind == "prop" && item.Name == "推荐信");
        Assert.DoesNotContain(firstAssets, item => item.Kind == "prop" && item.Name == "椅子");

        var secondImport = await client.PostAsync(
            $"/api/v2/projects/{projectId}/visual-assets/import-story-materials",
            null);
        secondImport.EnsureSuccessStatusCode();
        var secondAssets = await secondImport.Content.ReadFromJsonAsync<VisualAssetView[]>();
        Assert.NotNull(secondAssets);
        Assert.Equal(3, secondAssets.Length);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        var visualAssetIds = await dbContext.Assets
            .Where(item => item.ProjectId == projectId && item.Type == "visual-asset")
            .Select(item => item.Id)
            .ToArrayAsync();
        Assert.Equal(3, await dbContext.AssetDependencies
            .CountAsync(item => visualAssetIds.Contains(item.ConsumerAssetId)));
        Assert.False(await dbContext.ProductionEpisodes.AnyAsync());
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