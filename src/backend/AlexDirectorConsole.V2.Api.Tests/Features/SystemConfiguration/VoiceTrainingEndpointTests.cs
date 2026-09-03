using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.VoiceTraining;
using AlexDirectorConsole.V2.Api.Tests.Infrastructure;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AlexDirectorConsole.V2.Api.Tests.Features.SystemConfiguration;

[Collection("V2 API")]
public sealed class VoiceTrainingEndpointTests(V2ApiFactory factory)
    : IClassFixture<V2ApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Replica_job_is_forced_to_practice_only_and_cannot_export()
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v2/system/voice-training-jobs",
            new
            {
                name = "练习复刻女主",
                trainingMode = "replica",
                baseModelVersion = "v2ProPlus",
                language = "zh",
                dialect = "普通话",
                speakingStyle = "克制外声，快速内心独白",
                defaultSpeed = 1.08,
                sourceDescription = "授权练习素材",
                rightsConfirmed = true
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<VoiceTrainingJobView>();
        Assert.NotNull(job);
        Assert.Equal("practice-only", job.UsagePolicy);
        Assert.False(job.CanExport);
        Assert.Equal(1.08, job.DefaultSpeed);
    }

    [Fact]
    public async Task Created_jobs_can_be_listed_with_sqlite()
    {
        using var client = factory.CreateClient();
        var created = await CreateReplicaJobAsync(client);

        var response = await client.GetAsync("/api/v2/system/voice-training-jobs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jobs = await response.Content.ReadFromJsonAsync<VoiceTrainingJobView[]>();
        Assert.Contains(jobs!, item => item.Id == created.Id);
    }

    [Fact]
    public async Task Start_rejects_an_insufficient_dataset()
    {
        using var client = factory.CreateClient();
        var job = await CreateReplicaJobAsync(client);
        using var content = new MultipartFormDataContent
        {
            { new StringContent("这是一条准确的训练文本。"), "transcript" },
            { new ByteArrayContent(CreateWave(1)), "file", "sample.wav" }
        };
        content.Last().Headers.ContentType = new("audio/wav");
        var upload = await client.PostAsync(
            $"/api/v2/system/voice-training-jobs/{job.Id}/samples",
            content);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);

        var response = await client.PostAsync(
            $"/api/v2/system/voice-training-jobs/{job.Id}/start",
            null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("至少需要 3 条", problem.GetProperty("errors").GetProperty("samples")[0].GetString());
    }

    [Fact]
    public async Task Upload_reads_duration_after_a_list_metadata_chunk()
    {
        using var client = factory.CreateClient();
        var job = await CreateReplicaJobAsync(client);
        using var content = new MultipartFormDataContent
        {
            { new StringContent("带元数据的真实训练样本。"), "transcript" },
            { new ByteArrayContent(CreateWaveWithListChunk(1)), "file", "sample.wav" }
        };
        content.Last().Headers.ContentType = new("audio/wav");

        var response = await client.PostAsync(
            $"/api/v2/system/voice-training-jobs/{job.Id}/samples",
            content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sample = await response.Content.ReadFromJsonAsync<VoiceTrainingSampleView>();
        Assert.NotNull(sample);
        Assert.Equal(1, sample.DurationSeconds);
    }

    [Fact]
    public async Task Completed_replica_registers_a_non_exportable_versioned_package()
    {
        using var client = factory.CreateClient();
        var now = DateTimeOffset.UtcNow;
        var job = new VoiceTrainingJob
        {
            Name = "练习复刻长老",
            TrainingMode = "replica",
            Engine = "gpt-sovits",
            BaseModelVersion = "v2ProPlus",
            Language = "zh",
            Dialect = "普通话",
            SpeakingStyle = "低沉、威严、停顿明确",
            DefaultSpeed = 0.9,
            SourceDescription = "仅限练习",
            UsagePolicy = "practice-only",
            CanExport = false,
            RightsConfirmed = true,
            Status = "completed",
            ProgressPercent = 100,
            ExternalJobId = "worker-1",
            GptWeightsPath = "/models/elder.ckpt",
            SoVitsWeightsPath = "/models/elder.pth",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
            dbContext.VoiceTrainingJobs.Add(job);
            dbContext.VoiceTrainingSamples.Add(new VoiceTrainingSample
            {
                VoiceTrainingJobId = job.Id,
                FileName = "elder.wav",
                ContentType = "audio/wav",
                AudioContent = CreateWave(2),
                Transcript = "老夫今日亲自前来。",
                DurationSeconds = 2,
                SortOrder = 1,
                CreatedAtUtc = now
            });
            await dbContext.SaveChangesAsync();
        }

        var response = await client.PostAsync(
            $"/api/v2/system/voice-training-jobs/{job.Id}/register-package",
            null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var packages = await client.GetFromJsonAsync<JsonElement[]>("/api/v2/system/voice-packages");
        var voicePackage = Assert.Single(packages!, item =>
            item.GetProperty("voiceTrainingJobId").ValueKind == JsonValueKind.String
            && item.GetProperty("voiceTrainingJobId").GetGuid() == job.Id);
        Assert.Equal("practice-only", voicePackage.GetProperty("usagePolicy").GetString());
        Assert.False(voicePackage.GetProperty("canExport").GetBoolean());
        Assert.Equal(0.9, voicePackage.GetProperty("defaultSpeed").GetDouble());
    }

    private static async Task<VoiceTrainingJobView> CreateReplicaJobAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v2/system/voice-training-jobs",
            new
            {
                name = "练习复刻",
                trainingMode = "replica",
                baseModelVersion = "v2ProPlus",
                language = "zh",
                dialect = "普通话",
                speakingStyle = "克制",
                defaultSpeed = 1.0,
                sourceDescription = "练习素材",
                rightsConfirmed = true
            });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VoiceTrainingJobView>())!;
    }

    private static byte[] CreateWave(int seconds)
    {
        const int sampleRate = 16_000;
        var dataSize = sampleRate * seconds * sizeof(short);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] CreateWaveWithListChunk(int seconds)
    {
        const int sampleRate = 16_000;
        var dataSize = sampleRate * seconds * sizeof(short);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(48 + dataSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write("LIST"u8.ToArray());
        writer.Write(4);
        writer.Write("INFO"u8.ToArray());
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
        writer.Flush();
        return stream.ToArray();
    }
}
