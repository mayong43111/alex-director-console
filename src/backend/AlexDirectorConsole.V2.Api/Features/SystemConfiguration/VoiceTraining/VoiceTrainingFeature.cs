using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AlexDirectorConsole.V2.Api.Features.Projects.Voice;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.SystemConfiguration.VoiceTraining;

public sealed record VoiceTrainingSampleView(
    Guid Id,
    string FileName,
    string Transcript,
    double DurationSeconds,
    int SortOrder,
    string ContentUrl,
    DateTimeOffset CreatedAtUtc);

public sealed record VoiceTrainingJobView(
    Guid Id,
    string Name,
    string TrainingMode,
    string Engine,
    string BaseModelVersion,
    string Language,
    string Dialect,
    string SpeakingStyle,
    double DefaultSpeed,
    string SourceDescription,
    string UsagePolicy,
    bool CanExport,
    bool RightsConfirmed,
    string Status,
    int ProgressPercent,
    string? ExternalJobId,
    string? Error,
    Guid? VoicePackageId,
    int SampleCount,
    double TotalDurationSeconds,
    bool CanStart,
    IReadOnlyList<VoiceTrainingSampleView> Samples,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateVoiceTrainingJobRequest(
    string? Name,
    string? TrainingMode,
    string? BaseModelVersion,
    string? Language,
    string? Dialect,
    string? SpeakingStyle,
    double DefaultSpeed,
    string? SourceDescription,
    bool RightsConfirmed);

public sealed record VoiceTrainingWorkerSample(
    Guid Id,
    string FileName,
    string Transcript,
    byte[] AudioContent);

public sealed record VoiceTrainingWorkerRequest(
    Guid JobId,
    string Name,
    string BaseModelVersion,
    string Language,
    string Dialect,
    string SpeakingStyle,
    double DefaultSpeed,
    string UsagePolicy,
    IReadOnlyList<VoiceTrainingWorkerSample> Samples);

public sealed record VoiceTrainingWorkerState(
    string ExternalJobId,
    string Status,
    int ProgressPercent,
    string? GptWeightsPath,
    string? SoVitsWeightsPath,
    string? Error);

public interface IVoiceTrainingWorkerClient
{
    Task<VoiceTrainingWorkerState> StartAsync(VoiceTrainingWorkerRequest request, CancellationToken cancellationToken);
    Task<VoiceTrainingWorkerState> GetAsync(string externalJobId, CancellationToken cancellationToken);
}

public sealed class VoiceTrainingWorkerClient(HttpClient httpClient) : IVoiceTrainingWorkerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<VoiceTrainingWorkerState> StartAsync(
        VoiceTrainingWorkerRequest request,
        CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(JsonSerializer.Serialize(new
        {
            request.JobId,
            request.Name,
            request.BaseModelVersion,
            request.Language,
            request.Dialect,
            request.SpeakingStyle,
            request.DefaultSpeed,
            request.UsagePolicy,
            samples = request.Samples.Select(item => new
            {
                item.Id,
                item.FileName,
                item.Transcript
            })
        }, JsonOptions), Encoding.UTF8, "application/json"), "specification");
        foreach (var sample in request.Samples)
        {
            var audio = new ByteArrayContent(sample.AudioContent);
            audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            content.Add(audio, $"sample-{sample.Id:N}", sample.FileName);
        }

        using var response = await httpClient.PostAsync("v1/training/jobs", content, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<VoiceTrainingWorkerState>(JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode || result is null)
        {
            throw new InvalidOperationException(result?.Error ?? $"训练 Worker 拒绝任务（{(int)response.StatusCode}）。");
        }
        return result;
    }

    public async Task<VoiceTrainingWorkerState> GetAsync(
        string externalJobId,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            $"v1/training/jobs/{Uri.EscapeDataString(externalJobId)}",
            cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<VoiceTrainingWorkerState>(JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode || result is null)
        {
            throw new InvalidOperationException(result?.Error ?? $"训练 Worker 状态查询失败（{(int)response.StatusCode}）。");
        }
        return result;
    }
}

public static class VoiceTrainingEndpoints
{
    private const long MaximumSampleBytes = 50 * 1024 * 1024;
    private const double MinimumTrainingSeconds = 60;
    private const int MinimumSampleCount = 3;
    private static readonly HashSet<string> SupportedBaseModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "v1", "v2", "v3", "v4", "v2Pro", "v2ProPlus"
    };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapVoiceTraining(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/system/voice-training-jobs");

        group.MapGet("/", async (V2DbContext dbContext, CancellationToken cancellationToken) =>
        {
            var jobs = (await dbContext.VoiceTrainingJobs.AsNoTracking()
                .ToArrayAsync(cancellationToken))
                .OrderByDescending(item => item.UpdatedAtUtc)
                .ToArray();
            var samples = await dbContext.VoiceTrainingSamples.AsNoTracking()
                .OrderBy(item => item.SortOrder)
                .ToArrayAsync(cancellationToken);
            var packages = await dbContext.VoicePackages.AsNoTracking()
                .Where(item => item.VoiceTrainingJobId != null && item.IsCurrent)
                .ToArrayAsync(cancellationToken);
            return Results.Ok(jobs.Select(job => ToView(
                job,
                samples.Where(item => item.VoiceTrainingJobId == job.Id),
                packages.SingleOrDefault(item => item.VoiceTrainingJobId == job.Id))));
        });

        group.MapPost("/", async (
            CreateVoiceTrainingJobRequest request,
            V2DbContext dbContext,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var mode = request.TrainingMode!.Trim().ToLowerInvariant();
            var now = timeProvider.GetUtcNow();
            var job = new VoiceTrainingJob
            {
                Name = request.Name!.Trim(),
                TrainingMode = mode,
                Engine = "gpt-sovits",
                BaseModelVersion = request.BaseModelVersion!.Trim(),
                Language = request.Language!.Trim(),
                Dialect = request.Dialect!.Trim(),
                SpeakingStyle = request.SpeakingStyle?.Trim() ?? string.Empty,
                DefaultSpeed = request.DefaultSpeed,
                SourceDescription = request.SourceDescription?.Trim() ?? string.Empty,
                UsagePolicy = mode == "replica" ? "practice-only" : "licensed",
                CanExport = mode != "replica",
                RightsConfirmed = request.RightsConfirmed,
                Status = "draft",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            dbContext.VoiceTrainingJobs.Add(job);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v2/system/voice-training-jobs/{job.Id}", ToView(job, [], null));
        });

        group.MapGet("/{jobId:guid}", async (
            Guid jobId,
            V2DbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var job = await dbContext.VoiceTrainingJobs.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
            if (job is null) return Results.NotFound();
            var samples = await dbContext.VoiceTrainingSamples.AsNoTracking()
                .Where(item => item.VoiceTrainingJobId == jobId)
                .OrderBy(item => item.SortOrder)
                .ToArrayAsync(cancellationToken);
            var package = await dbContext.VoicePackages.AsNoTracking()
                .SingleOrDefaultAsync(item => item.VoiceTrainingJobId == jobId && item.IsCurrent, cancellationToken);
            return Results.Ok(ToView(job, samples, package));
        });

        group.MapPost("/{jobId:guid}/samples", async (
            Guid jobId,
            HttpRequest request,
            V2DbContext dbContext,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            var job = await dbContext.VoiceTrainingJobs.SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
            if (job is null) return Results.NotFound();
            if (!IsEditable(job.Status)) return Results.Conflict(new { error = "训练已启动，不能再修改数据集。" });
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "请使用 multipart/form-data 上传训练样本。" });
            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            var transcript = form["transcript"].ToString().Trim();
            if (file is null || file.Length is < 44 or > MaximumSampleBytes)
                return Results.BadRequest(new { error = "训练样本必须是大于 44 字节且不超过 50 MB 的 WAV。" });
            if (transcript.Length is < 1 or > 2000)
                return Results.BadRequest(new { error = "训练样本必须提供 1 至 2000 字符的准确文本。" });
            await using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();
            try
            {
                VoiceWave.Validate(bytes);
            }
            catch (InvalidOperationException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
            var nextOrder = (await dbContext.VoiceTrainingSamples
                .Where(item => item.VoiceTrainingJobId == jobId)
                .Select(item => (int?)item.SortOrder)
                .MaxAsync(cancellationToken) ?? 0) + 1;
            var sample = new VoiceTrainingSample
            {
                VoiceTrainingJobId = jobId,
                FileName = Path.GetFileName(file.FileName),
                ContentType = "audio/wav",
                AudioContent = bytes,
                Transcript = transcript,
                DurationSeconds = VoiceWave.ReadDurationSeconds(bytes),
                SortOrder = nextOrder,
                CreatedAtUtc = timeProvider.GetUtcNow()
            };
            dbContext.VoiceTrainingSamples.Add(sample);
            job.Status = "draft";
            job.Error = null;
            job.UpdatedAtUtc = sample.CreatedAtUtc;
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Created(
                $"/api/v2/system/voice-training-jobs/{jobId}/samples/{sample.Id}/content",
                ToSampleView(sample));
        });

        group.MapDelete("/{jobId:guid}/samples/{sampleId:guid}", async (
            Guid jobId,
            Guid sampleId,
            V2DbContext dbContext,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            var job = await dbContext.VoiceTrainingJobs.SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
            if (job is null) return Results.NotFound();
            if (!IsEditable(job.Status)) return Results.Conflict(new { error = "训练已启动，不能再修改数据集。" });
            var sample = await dbContext.VoiceTrainingSamples.SingleOrDefaultAsync(
                item => item.Id == sampleId && item.VoiceTrainingJobId == jobId,
                cancellationToken);
            if (sample is null) return Results.NotFound();
            dbContext.VoiceTrainingSamples.Remove(sample);
            job.UpdatedAtUtc = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });

        group.MapGet("/{jobId:guid}/samples/{sampleId:guid}/content", async (
            Guid jobId,
            Guid sampleId,
            V2DbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var sample = await dbContext.VoiceTrainingSamples.AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == sampleId && item.VoiceTrainingJobId == jobId,
                cancellationToken);
            return sample is null
                ? Results.NotFound()
                : Results.File(sample.AudioContent, sample.ContentType, sample.FileName, enableRangeProcessing: true);
        });

        group.MapPost("/{jobId:guid}/start", async (
            Guid jobId,
            V2DbContext dbContext,
            IVoiceTrainingWorkerClient worker,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            var job = await dbContext.VoiceTrainingJobs.SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
            if (job is null) return Results.NotFound();
            if (!IsEditable(job.Status)) return Results.Conflict(new { error = "训练任务已经启动。" });
            var samples = await dbContext.VoiceTrainingSamples
                .Where(item => item.VoiceTrainingJobId == jobId)
                .OrderBy(item => item.SortOrder)
                .ToArrayAsync(cancellationToken);
            var totalSeconds = samples.Sum(item => item.DurationSeconds);
            if (samples.Length < MinimumSampleCount || totalSeconds < MinimumTrainingSeconds)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["samples"] = [$"每个音色至少需要 {MinimumSampleCount} 条、合计 {MinimumTrainingSeconds:0} 秒的干净单人 WAV；当前 {samples.Length} 条、{totalSeconds:0.0} 秒。"]
                });
            }
            try
            {
                var state = await worker.StartAsync(new(
                    job.Id,
                    job.Name,
                    job.BaseModelVersion,
                    job.Language,
                    job.Dialect,
                    job.SpeakingStyle,
                    job.DefaultSpeed,
                    job.UsagePolicy,
                    samples.Select(item => new VoiceTrainingWorkerSample(
                        item.Id, item.FileName, item.Transcript, item.AudioContent)).ToArray()), cancellationToken);
                ApplyWorkerState(job, state, timeProvider.GetUtcNow());
                await dbContext.SaveChangesAsync(cancellationToken);
                return Results.Accepted($"/api/v2/system/voice-training-jobs/{job.Id}", ToView(job, samples, null));
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                job.Status = "failed";
                job.Error = $"训练 Worker 不可用或拒绝任务：{error.Message}";
                job.UpdatedAtUtc = timeProvider.GetUtcNow();
                await dbContext.SaveChangesAsync(cancellationToken);
                return Results.Problem(job.Error, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        group.MapPost("/{jobId:guid}/sync", async (
            Guid jobId,
            V2DbContext dbContext,
            IVoiceTrainingWorkerClient worker,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            var job = await dbContext.VoiceTrainingJobs.SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
            if (job is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(job.ExternalJobId))
                return Results.Conflict(new { error = "训练任务尚未提交到 Worker。" });
            try
            {
                ApplyWorkerState(job, await worker.GetAsync(job.ExternalJobId, cancellationToken), timeProvider.GetUtcNow());
                await dbContext.SaveChangesAsync(cancellationToken);
                var samples = await dbContext.VoiceTrainingSamples.AsNoTracking()
                    .Where(item => item.VoiceTrainingJobId == jobId)
                    .OrderBy(item => item.SortOrder)
                    .ToArrayAsync(cancellationToken);
                return Results.Ok(ToView(job, samples, null));
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                return Results.Problem($"训练状态同步失败：{error.Message}", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        group.MapPost("/{jobId:guid}/register-package", async (
            Guid jobId,
            V2DbContext dbContext,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            var job = await dbContext.VoiceTrainingJobs.SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
            if (job is null) return Results.NotFound();
            if (job.Status != "completed" || string.IsNullOrWhiteSpace(job.GptWeightsPath) || string.IsNullOrWhiteSpace(job.SoVitsWeightsPath))
                return Results.Conflict(new { error = "训练尚未完成或 Worker 未返回完整权重路径。" });
            var existing = await dbContext.VoicePackages.AsNoTracking()
                .SingleOrDefaultAsync(item => item.VoiceTrainingJobId == jobId && item.IsCurrent, cancellationToken);
            if (existing is not null) return Results.Ok(ToView(job, await SamplesAsync(dbContext, jobId, cancellationToken), existing));
            var samples = await SamplesAsync(dbContext, jobId, cancellationToken);
            var reference = samples.OrderByDescending(item => item.DurationSeconds).FirstOrDefault();
            if (reference is null) return Results.Conflict(new { error = "训练任务没有可用参考音。" });
            var now = timeProvider.GetUtcNow();
            var voicePackage = new VoicePackage
            {
                ResourceId = Guid.NewGuid(),
                Version = 1,
                Name = job.Name,
                Description = $"由训练任务 {job.Id} 生成。{job.SourceDescription}",
                Engine = "gpt-sovits",
                BaseModelVersion = job.BaseModelVersion,
                GptWeightsPath = job.GptWeightsPath,
                SoVitsWeightsPath = job.SoVitsWeightsPath,
                ReferenceAudioFileName = reference.FileName,
                ReferenceAudioContentType = reference.ContentType,
                ReferenceAudioContent = reference.AudioContent,
                ReferenceText = reference.Transcript,
                Language = job.Language,
                Dialect = job.Dialect,
                SpeakingStyle = job.SpeakingStyle,
                DefaultSpeed = job.DefaultSpeed,
                License = job.TrainingMode == "replica" ? "Practice only; redistribution prohibited" : "User-provided licensed voice",
                VoiceTrainingJobId = job.Id,
                UsagePolicy = job.UsagePolicy,
                CanExport = job.CanExport,
                IsEnabled = true,
                IsCurrent = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            dbContext.VoicePackages.Add(voicePackage);
            job.UpdatedAtUtc = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToView(job, samples, voicePackage));
        });

        return app;
    }

    private static Dictionary<string, string[]> Validate(CreateVoiceTrainingJobRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
            errors["name"] = ["名称必须为 1 至 200 个字符。"]; 
        var mode = request.TrainingMode?.Trim().ToLowerInvariant();
        if (mode is not ("original" or "replica")) errors["trainingMode"] = ["训练模式必须是 original 或 replica。"]; 
        if (string.IsNullOrWhiteSpace(request.BaseModelVersion) || !SupportedBaseModels.Contains(request.BaseModelVersion.Trim()))
            errors["baseModelVersion"] = ["请选择支持的 GPT-SoVITS 底模版本。"]; 
        if (string.IsNullOrWhiteSpace(request.Language) || request.Language.Trim().Length > 40)
            errors["language"] = ["语言不能为空且不能超过 40 个字符。"]; 
        if (string.IsNullOrWhiteSpace(request.Dialect) || request.Dialect.Trim().Length > 100)
            errors["dialect"] = ["方言或口音不能为空且不能超过 100 个字符。"]; 
        if ((request.SpeakingStyle?.Trim().Length ?? 0) > 2000) errors["speakingStyle"] = ["说话习惯不能超过 2000 个字符。"]; 
        if ((request.SourceDescription?.Trim().Length ?? 0) > 2000) errors["sourceDescription"] = ["来源说明不能超过 2000 个字符。"]; 
        if (request.DefaultSpeed is < 0.5 or > 2) errors["defaultSpeed"] = ["默认语速必须在 0.5 到 2.0 之间。"]; 
        if (!request.RightsConfirmed) errors["rightsConfirmed"] = ["必须确认训练数据的授权范围；练习复刻将被强制标记为禁止导出。"]; 
        return errors;
    }

    private static bool IsEditable(string status) => status is "draft" or "failed";

    private static async Task<VoiceTrainingSample[]> SamplesAsync(
        V2DbContext dbContext,
        Guid jobId,
        CancellationToken cancellationToken) => await dbContext.VoiceTrainingSamples
        .Where(item => item.VoiceTrainingJobId == jobId)
        .OrderBy(item => item.SortOrder)
        .ToArrayAsync(cancellationToken);

    private static void ApplyWorkerState(VoiceTrainingJob job, VoiceTrainingWorkerState state, DateTimeOffset now)
    {
        var status = state.Status.Trim().ToLowerInvariant();
        if (status is not ("queued" or "running" or "completed" or "failed"))
            throw new InvalidOperationException($"训练 Worker 返回了未知状态：{state.Status}");
        job.ExternalJobId = state.ExternalJobId;
        job.Status = status;
        job.ProgressPercent = Math.Clamp(state.ProgressPercent, 0, 100);
        job.GptWeightsPath = state.GptWeightsPath;
        job.SoVitsWeightsPath = state.SoVitsWeightsPath;
        job.Error = state.Error;
        job.UpdatedAtUtc = now;
    }

    private static VoiceTrainingJobView ToView(
        VoiceTrainingJob job,
        IEnumerable<VoiceTrainingSample> sourceSamples,
        VoicePackage? voicePackage)
    {
        var samples = sourceSamples.OrderBy(item => item.SortOrder).ToArray();
        var totalSeconds = samples.Sum(item => item.DurationSeconds);
        return new(
            job.Id,
            job.Name,
            job.TrainingMode,
            job.Engine,
            job.BaseModelVersion,
            job.Language,
            job.Dialect,
            job.SpeakingStyle,
            job.DefaultSpeed,
            job.SourceDescription,
            job.UsagePolicy,
            job.CanExport,
            job.RightsConfirmed,
            job.Status,
            job.ProgressPercent,
            job.ExternalJobId,
            job.Error,
            voicePackage?.Id,
            samples.Length,
            totalSeconds,
            samples.Length >= MinimumSampleCount && totalSeconds >= MinimumTrainingSeconds,
            samples.Select(ToSampleView).ToArray(),
            job.CreatedAtUtc,
            job.UpdatedAtUtc);
    }

    private static VoiceTrainingSampleView ToSampleView(VoiceTrainingSample sample) => new(
        sample.Id,
        sample.FileName,
        sample.Transcript,
        sample.DurationSeconds,
        sample.SortOrder,
        $"/api/v2/system/voice-training-jobs/{sample.VoiceTrainingJobId}/samples/{sample.Id}/content",
        sample.CreatedAtUtc);
}
