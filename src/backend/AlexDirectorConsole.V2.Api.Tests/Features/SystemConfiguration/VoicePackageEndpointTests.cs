using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.VoicePackages;
using AlexDirectorConsole.V2.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AlexDirectorConsole.V2.Api.Tests.Features.SystemConfiguration;

public sealed class VoicePackageEndpointTests(V2ApiFactory factory)
    : IClassFixture<V2ApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Default_voice_packages_are_real_idempotent_and_respect_user_archive()
    {
        using var client = factory.CreateClient();
        var packages = await client.GetFromJsonAsync<List<VoicePackageResponse>>(
            "/api/v2/system/voice-packages");

        Assert.NotNull(packages);
        Assert.Equal(9, packages.Count);
        Assert.Equal(3, packages.Count(item => item.Engine == "gpt-sovits"));
        Assert.Equal(6, packages.Count(item => item.Engine == "cosyvoice"));
        Assert.All(packages.Where(item => item.Engine == "cosyvoice"), item => Assert.Equal("zh", item.Language));
        Assert.Contains(packages, item => item.Name == "开放普通话·超文" && item.License.StartsWith("CC0-1.0"));
        Assert.Contains(packages, item => item.Name == "开放女声·LJSpeech" && item.License.StartsWith("Public Domain"));
        Assert.Contains(packages, item => item.Name == "非商用普通话·小雅" && item.License.StartsWith("Non-commercial"));
        Assert.Contains(packages, item => item.Name == "CosyVoice·开放普通话·超文" && item.License.StartsWith("CC0-1.0"));
        Assert.Contains(packages, item => item.Name == "CosyVoice·非商用普通话·小雅" && item.License.StartsWith("Non-commercial"));
        Assert.Equal(4, packages.Count(item => item.Name.StartsWith("CosyVoice·AISHELL") && item.License.StartsWith("Apache-2.0")));
        foreach (var package in packages)
        {
            var audio = await client.GetByteArrayAsync(package.ReferenceAudioUrl);
            Assert.True(audio.Length > 10_000);
            Assert.Equal("RIFF"u8.ToArray(), audio[..4]);
            Assert.Equal("WAVE"u8.ToArray(), audio[8..12]);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDefaultVoicePackageSynchronizer>()
                .SynchronizeAsync();
        }
        var afterSecondSync = await client.GetFromJsonAsync<List<VoicePackageResponse>>(
            "/api/v2/system/voice-packages");
        Assert.Equal(9, afterSecondSync?.Count);

        var archived = packages[0];
        var archive = await client.DeleteAsync($"/api/v2/system/voice-packages/{archived.ResourceId}");
        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDefaultVoicePackageSynchronizer>()
                .SynchronizeAsync();
        }
        var afterArchive = await client.GetFromJsonAsync<List<VoicePackageResponse>>(
            "/api/v2/system/voice-packages");
        Assert.Equal(8, afterArchive?.Count);
        Assert.DoesNotContain(afterArchive!, item => item.ResourceId == archived.ResourceId);
    }

    [Fact]
    public async Task Voice_package_is_global_versioned_and_archivable()
    {
        using var client = factory.CreateClient();
        using var createContent = CreateContent(includeAudio: true, name: "旁白标准音");

        var create = await client.PostAsync("/api/v2/system/voice-packages", createContent);

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var versionOne = await create.Content.ReadFromJsonAsync<VoicePackageResponse>()
            ?? throw new InvalidOperationException("创建语音包未返回响应内容。");
        Assert.Equal(1, versionOne.Version);
        Assert.Equal("gpt-sovits", versionOne.Engine);
        Assert.Equal("普通话", versionOne.Dialect);

        var listed = await client.GetFromJsonAsync<List<VoicePackageResponse>>(
            "/api/v2/system/voice-packages");
        var listedPackage = Assert.Single(listed!, item => item.ResourceId == versionOne.ResourceId);
        Assert.Equal(versionOne.Id, listedPackage.Id);

        using var updateContent = CreateContent(includeAudio: false, name: "旁白标准音 2");
        var update = await client.PutAsync(
            $"/api/v2/system/voice-packages/{versionOne.ResourceId}",
            updateContent);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var versionTwo = await update.Content.ReadFromJsonAsync<VoicePackageResponse>()
            ?? throw new InvalidOperationException("更新语音包未返回响应内容。");
        Assert.Equal(2, versionTwo.Version);
        Assert.NotEqual(versionOne.Id, versionTwo.Id);
        Assert.Equal(versionOne.ResourceId, versionTwo.ResourceId);

        var oldReference = await client.GetAsync(versionOne.ReferenceAudioUrl);
        Assert.Equal(HttpStatusCode.OK, oldReference.StatusCode);
        Assert.Equal("audio/wav", oldReference.Content.Headers.ContentType?.MediaType);
        Assert.Equal(CreateWave(), await oldReference.Content.ReadAsByteArrayAsync());

        var archive = await client.DeleteAsync(
            $"/api/v2/system/voice-packages/{versionOne.ResourceId}");
        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        listed = await client.GetFromJsonAsync<List<VoicePackageResponse>>(
            "/api/v2/system/voice-packages");
        Assert.DoesNotContain(listed!, item => item.ResourceId == versionOne.ResourceId);

        var historicalPackage = await client.GetAsync(
            $"/api/v2/system/voice-packages/{versionTwo.Id}");
        Assert.Equal(HttpStatusCode.OK, historicalPackage.StatusCode);
    }

    [Fact]
    public async Task Create_rejects_invalid_reference_audio()
    {
        using var client = factory.CreateClient();
        using var content = CreateContent(includeAudio: false, name: "无效语音包");
        var invalidAudio = new ByteArrayContent(new byte[44]);
        invalidAudio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(invalidAudio, "referenceAudio", "invalid.wav");

        var response = await client.PostAsync("/api/v2/system/voice-packages", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CosyVoice_package_does_not_require_GptSoVits_weights()
    {
        using var client = factory.CreateClient();
        using var content = CreateContent(
            includeAudio: true,
            name: "河南话固定音色",
            engine: "cosyvoice",
            baseModelVersion: "FunAudioLLM/Fun-CosyVoice3-0.5B-2512",
            includeWeights: false);

        var response = await client.PostAsync("/api/v2/system/voice-packages", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var package = await response.Content.ReadFromJsonAsync<VoicePackageResponse>();
        Assert.Equal("cosyvoice", package?.Engine);
    }

    private static MultipartFormDataContent CreateContent(
        bool includeAudio,
        string name,
        string? engine = null,
        string baseModelVersion = "v2ProPlus",
        bool includeWeights = true)
    {
        var content = new MultipartFormDataContent();
        Add(content, "name", name);
        Add(content, "description", "用于跨项目复用的固定旁白声音。");
        if (engine is not null) Add(content, "engine", engine);
        Add(content, "baseModelVersion", baseModelVersion);
        if (includeWeights)
        {
            Add(content, "gptWeightsPath", "GPT_weights/narrator.ckpt");
            Add(content, "soVitsWeightsPath", "SoVITS_weights/narrator.pth");
        }
        Add(content, "referenceText", "欢迎来到今天的故事现场。");
        Add(content, "language", "zh");
        Add(content, "dialect", "普通话");
        Add(content, "speakingStyle", "沉稳，短句间有自然停顿");
        Add(content, "defaultSpeed", "1.05");
        Add(content, "license", "CC-BY-4.0");
        Add(content, "sourceUrl", "https://example.test/voices/narrator");
        if (includeAudio)
        {
            var audio = new ByteArrayContent(CreateWave());
            audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            content.Add(audio, "referenceAudio", "narrator.wav");
        }
        return content;
    }

    private static void Add(MultipartFormDataContent content, string name, string value) =>
        content.Add(new StringContent(value), name);

    private static byte[] CreateWave()
    {
        var bytes = new byte[44];
        "RIFF"u8.CopyTo(bytes);
        "WAVE"u8.CopyTo(bytes.AsSpan(8));
        return bytes;
    }

    private sealed record VoicePackageResponse(
        Guid Id,
        Guid ResourceId,
        int Version,
        string Name,
        string Engine,
        string Language,
        string Dialect,
        string License,
        string ReferenceAudioUrl);
}