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
        var generated = await factory.CompleteGenerationTaskAsync<VoiceProfileView>(generateResponse);
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

        var imageWithoutPromptResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/visual-assets/{character.ResourceId}/reference/generate",
            null);
        Assert.Equal(HttpStatusCode.Accepted, imageWithoutPromptResponse.StatusCode);
        var failedImageTask = await factory.FailGenerationTaskAsync(imageWithoutPromptResponse);
        Assert.Equal("failed", failedImageTask.Status);
        Assert.Contains("提示词", failedImageTask.LastError);

        var promptResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/visual-assets/{character.ResourceId}/reference/prompt/generate",
            null);
        promptResponse.EnsureSuccessStatusCode();
        var referencePrompt = await factory.CompleteGenerationTaskAsync<VisualReferencePromptView>(promptResponse);
        Assert.NotNull(referencePrompt);
        Assert.Contains("left 55%", referencePrompt.Prompt);
        Assert.Contains("pure solid white", referencePrompt.Prompt);
        var initialPromptCall = Assert.Single(factory.VisualReferencePromptWriterCalls);
        Assert.Equal("gpt-image-2", initialPromptCall.TargetImageModel);
        Assert.Equal("character", initialPromptCall.SubjectKind);
        Assert.False(initialPromptCall.IsImageEdit);
        Assert.Null(initialPromptCall.PreviousPrompt);

        var promptedAssets = await client.GetFromJsonAsync<VisualAssetView[]>(
            $"/api/v2/projects/{projectId}/visual-assets");
        var promptedCharacter = Assert.Single(promptedAssets!);
        Assert.Null(promptedCharacter.ReferenceImage);
        Assert.Equal(referencePrompt.AssetId, promptedCharacter.ReferencePrompt?.AssetId);

        var generateResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/visual-assets/{character.ResourceId}/reference/generate",
            null);

        generateResponse.EnsureSuccessStatusCode();
        var reference = await factory.CompleteGenerationTaskAsync<VisualReferenceImageView>(generateResponse);
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
        Assert.Equal(referencePrompt.AssetId, listedCharacter.ReferencePrompt?.AssetId);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        var binding = await dbContext.VisualReferences.SingleAsync(item => item.ImageAssetId == reference.AssetId);
        Assert.Equal(reference.AssetId, binding.ImageAssetId);
        Assert.Equal(character.ResourceId, binding.SubjectResourceId);
        Assert.Equal("generation-reference", binding.Purpose);
        var imageAsset = await dbContext.Assets.SingleAsync(item => item.Id == reference.AssetId);
        using var metadata = JsonDocument.Parse(imageAsset.GenerationMetadataJson!);
        Assert.Equal(1024, metadata.RootElement.GetProperty("outputWidth").GetInt32());
        Assert.Equal(1024, metadata.RootElement.GetProperty("outputHeight").GetInt32());
        Assert.Equal("medium", metadata.RootElement.GetProperty("parameters").GetProperty("quality").GetString());
        Assert.True(await dbContext.AssetDependencies.AnyAsync(item =>
            item.ConsumerAssetId == reference.AssetId && item.SourceAssetId == character.AssetId));
        Assert.True(await dbContext.AssetDependencies.AnyAsync(item =>
            item.ConsumerAssetId == reference.AssetId && item.Role == "uses-prompt"));

        var foundryConfiguration = await dbContext.FoundryConfigurations.SingleOrDefaultAsync();
        if (foundryConfiguration is null)
        {
            foundryConfiguration = new FoundryConfiguration { UpdatedAtUtc = DateTimeOffset.UtcNow };
            dbContext.FoundryConfigurations.Add(foundryConfiguration);
        }
        foundryConfiguration.ImageProvider = "comfyui";
        await dbContext.SaveChangesAsync();

        const string revisionInstruction = "缩短牛角，披风改为深蓝色，保持四视图布局。";
        var retryPromptResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/visual-assets/{character.ResourceId}/reference/prompt/generate",
            new { instruction = revisionInstruction, useCurrentReference = true });
        retryPromptResponse.EnsureSuccessStatusCode();
        var retriedPrompt = await factory.CompleteGenerationTaskAsync<VisualReferencePromptView>(retryPromptResponse);
        Assert.NotNull(retriedPrompt);
        Assert.Equal(referencePrompt.Version + 1, retriedPrompt.Version);
        Assert.Equal(2, factory.VisualReferencePromptWriterCalls.Count);
        var revisionPromptCall = factory.VisualReferencePromptWriterCalls[1];
        Assert.Equal(revisionInstruction, revisionPromptCall.Instruction);
        Assert.Equal(referencePrompt.Prompt, revisionPromptCall.PreviousPrompt);
        Assert.True(revisionPromptCall.IsImageEdit);
        Assert.Equal("Qwen Image Edit 2511", revisionPromptCall.TargetImageModel);
        var retryResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/visual-assets/{character.ResourceId}/reference/generate",
            null);
        retryResponse.EnsureSuccessStatusCode();
        var retried = await factory.CompleteGenerationTaskAsync<VisualReferenceImageView>(retryResponse);
        Assert.NotNull(retried);
        Assert.Equal(reference.Version + 1, retried.Version);
        Assert.Equal(retriedPrompt.Prompt, retried.Prompt);
        var imageVersions = await dbContext.Assets
            .Where(item => item.Type == "visual-reference-image")
            .OrderBy(item => item.Version)
            .ToArrayAsync();
        Assert.Equal([1, 2], imageVersions.Select(item => item.Version));
        Assert.Single(imageVersions.Select(item => item.ResourceId).Distinct());
        var retriedAsset = imageVersions[^1];
        using var retriedMetadata = JsonDocument.Parse(retriedAsset.GenerationMetadataJson!);
        Assert.True(retriedMetadata.RootElement.GetProperty("useCurrentReference").GetBoolean());
        Assert.Equal(reference.AssetId, retriedMetadata.RootElement.GetProperty("basedOnReferenceAssetId").GetGuid());
        Assert.True(await dbContext.AssetDependencies.AnyAsync(item =>
            item.ConsumerAssetId == retried.AssetId
            && item.SourceAssetId == reference.AssetId
            && item.Role == "uses-current-reference"));

        using var upload = new MultipartFormDataContent();
        using var uploadBytes = new ByteArrayContent(content);
        uploadBytes.Headers.ContentType = new("image/png");
        upload.Add(uploadBytes, "file", "director-reference.png");
        var uploadResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/visual-assets/{character.ResourceId}/reference/upload",
            upload);
        uploadResponse.EnsureSuccessStatusCode();
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<VisualReferenceImageView>();
        Assert.NotNull(uploaded);
        Assert.Equal(retried.Version + 1, uploaded.Version);
        Assert.Equal(content, await client.GetByteArrayAsync(uploaded.ContentUrl));
        var uploadedAsset = await dbContext.Assets.SingleAsync(item => item.Id == uploaded.AssetId);
        using var uploadedMetadata = JsonDocument.Parse(uploadedAsset.GenerationMetadataJson!);
        Assert.Equal("upload-visual-reference", uploadedMetadata.RootElement.GetProperty("operation").GetString());
        Assert.Equal("director-reference.png", uploadedMetadata.RootElement.GetProperty("sourceFileName").GetString());

        var secondCreateResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/visual-assets",
            new
            {
                kind = "character",
                name = "阿托斯",
                summary = "沉稳的火枪手",
                visualDescription = "高大稳重，深蓝披风",
                mustKeep = new[] { "佩剑" },
                avoid = Array.Empty<string>(),
                storyReferences = new[] { "第三章" }
            });
        secondCreateResponse.EnsureSuccessStatusCode();

        var batchPromptResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/visual-assets/reference/prompts/generate-missing",
            new { kind = "character" });
        batchPromptResponse.EnsureSuccessStatusCode();
        var batchPrompts = await factory.CompleteGenerationTaskAsync<BatchVisualReferenceResult>(batchPromptResponse);
        Assert.Equal(2, batchPrompts?.Generated);
        Assert.Equal(0, batchPrompts?.Skipped);
        Assert.Equal(0, batchPrompts?.Failed);
        var batchPromptCall = factory.VisualReferencePromptWriterCalls[^1];
        Assert.Equal("Krea 2 Turbo", batchPromptCall.TargetImageModel);
        Assert.False(batchPromptCall.IsImageEdit);

        var repeatedPromptResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/visual-assets/reference/prompts/generate-missing",
            new { kind = "character" });
        repeatedPromptResponse.EnsureSuccessStatusCode();
        var repeatedPrompts = await factory.CompleteGenerationTaskAsync<BatchVisualReferenceResult>(repeatedPromptResponse);
        Assert.Equal(0, repeatedPrompts?.Generated);
        Assert.Equal(2, repeatedPrompts?.Skipped);

        var batchImageResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/visual-assets/reference/images/generate-missing",
            new { kind = "character" });
        batchImageResponse.EnsureSuccessStatusCode();
        var batchImages = await factory.CompleteGenerationTaskAsync<BatchVisualReferenceResult>(batchImageResponse);
        Assert.Equal(1, batchImages?.Generated);
        Assert.Equal(1, batchImages?.Skipped);
        Assert.Equal(0, batchImages?.Failed);

        var repeatedImageResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/visual-assets/reference/images/generate-missing",
            new { kind = "character" });
        repeatedImageResponse.EnsureSuccessStatusCode();
        var repeatedImages = await factory.CompleteGenerationTaskAsync<BatchVisualReferenceResult>(repeatedImageResponse);
        Assert.Equal(0, repeatedImages?.Generated);
        Assert.Equal(2, repeatedImages?.Skipped);
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

        var promptResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/visual-assets/{asset.ResourceId}/reference/prompt/generate",
            null);
        promptResponse.EnsureSuccessStatusCode();
        var prompt = await factory.CompleteGenerationTaskAsync<VisualReferencePromptView>(promptResponse);
        Assert.NotNull(prompt);
        Assert.Contains("1024x1024", prompt.Prompt);
        Assert.Contains("pure solid white", prompt.Prompt);
        Assert.Contains(expectedPromptRule, prompt.Prompt);

        var generateResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/visual-assets/{asset.ResourceId}/reference/generate",
            null);

        generateResponse.EnsureSuccessStatusCode();
        var reference = await factory.CompleteGenerationTaskAsync<VisualReferenceImageView>(generateResponse);
        Assert.NotNull(reference);
        Assert.Equal(kind, reference.SubjectType);
        Assert.Equal(prompt.Prompt, reference.Prompt);
        var content = await client.GetByteArrayAsync(reference.ContentUrl);
        using var bitmap = SKBitmap.Decode(content);
        Assert.NotNull(bitmap);
        Assert.Equal(1024, bitmap.Width);
        Assert.Equal(1024, bitmap.Height);
    }

    [Fact]
    public async Task Import_script_materials_uses_formal_script_without_source_analysis()
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
        var draftResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft",
            new { mode = AdaptationModes.SourceChapters, desiredEpisodeCount = 1 });
        draftResponse.EnsureSuccessStatusCode();
        var formalResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/sources/{source.Id}/script-draft/episodes/1/production-script/tasks",
            null);
        await factory.CompleteGenerationTaskAsync<AdaptationScriptView>(formalResponse);

        var pendingStatuses = await client.GetFromJsonAsync<ScriptMaterialAnalysisStatusView[]>(
            $"/api/v2/projects/{projectId}/visual-assets/script-material-analysis-status");
        Assert.Collection(
            Assert.IsType<ScriptMaterialAnalysisStatusView[]>(pendingStatuses),
            status =>
            {
                Assert.Equal("正式剧本", status.ScriptType);
                Assert.False(status.IsAnalyzed);
            },
            status =>
            {
                Assert.Equal("改编方案", status.ScriptType);
                Assert.False(status.IsAnalyzed);
            });

        var firstImport = await client.PostAsync(
            $"/api/v2/projects/{projectId}/visual-assets/import-script-materials/tasks",
            null);
        var taskJson = await firstImport.Content.ReadAsStringAsync();
        var taskId = JsonDocument.Parse(taskJson).RootElement.GetProperty("id").GetGuid();
        var firstAssets = await factory.CompleteGenerationTaskAsync<VisualAssetView[]>(firstImport);
        var taskEvents = await client.GetStringAsync($"/api/v2/tasks/{taskId}/events?after=0");
        Assert.Contains("\"stage\":\"queued\"", taskEvents, StringComparison.Ordinal);
        Assert.Contains("\"stage\":\"running\"", taskEvents, StringComparison.Ordinal);
        Assert.Contains("\"stage\":\"completed\"", taskEvents, StringComparison.Ordinal);
        Assert.NotNull(firstAssets);
        Assert.Equal(4, firstAssets.Length);
        Assert.Contains(firstAssets, item => item.Kind == "character" && item.Name == "达达尼昂");
        Assert.Contains(firstAssets, item => item.Kind == "scene" && item.Name == "外景 · 巴黎街道 · 日");
        Assert.Contains(firstAssets, item => item.Kind == "scene" && item.Name == "第二章");
        Assert.Contains(firstAssets, item => item.Kind == "prop" && item.Name == "推荐信");
        Assert.DoesNotContain(firstAssets, item => item.Kind == "prop" && item.Name == "椅子");

        var analyzedStatuses = await client.GetFromJsonAsync<ScriptMaterialAnalysisStatusView[]>(
            $"/api/v2/projects/{projectId}/visual-assets/script-material-analysis-status");
        Assert.All(
            Assert.IsType<ScriptMaterialAnalysisStatusView[]>(analyzedStatuses),
            status => Assert.True(status.IsAnalyzed));

        var secondImport = await client.PostAsync(
            $"/api/v2/projects/{projectId}/visual-assets/import-story-materials",
            null);
        secondImport.EnsureSuccessStatusCode();
        var secondAssets = await secondImport.Content.ReadFromJsonAsync<VisualAssetView[]>();
        Assert.NotNull(secondAssets);
        Assert.Equal(4, secondAssets.Length);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        var visualAssetIds = await dbContext.Assets
            .Where(item => item.ProjectId == projectId && item.Type == "visual-asset")
            .Select(item => item.Id)
            .ToArrayAsync();
        Assert.Equal(4, await dbContext.AssetDependencies
            .CountAsync(item => visualAssetIds.Contains(item.ConsumerAssetId)));
        Assert.True(await dbContext.ProductionEpisodes.AnyAsync());
        Assert.False(await dbContext.Assets.AnyAsync(item => item.Type == StoryMaterialAnalysisQueries.AssetType));
    }

    [Theory]
    [InlineData("药物柜", 2, true)]
    [InlineData("药物柜", 1, false)]
    [InlineData("研究文件夹", 3, false)]
    public void Agent_asset_breakdown_only_keeps_large_recurring_props(
        string name,
        int sceneCount,
        bool expected)
    {
        Assert.Equal(expected, SpecialPropPolicy.RequiresLargeRecurringAsset(name, sceneCount));
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