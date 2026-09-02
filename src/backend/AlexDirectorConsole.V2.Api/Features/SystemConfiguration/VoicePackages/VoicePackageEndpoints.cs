using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;

namespace AlexDirectorConsole.V2.Api.Features.SystemConfiguration.VoicePackages;

public sealed record VoicePackageView(
    Guid Id,
    Guid ResourceId,
    int Version,
    string Name,
    string Description,
    string Engine,
    string BaseModelVersion,
    string GptWeightsPath,
    string SoVitsWeightsPath,
    string ReferenceAudioFileName,
    string ReferenceAudioUrl,
    string ReferenceText,
    string Language,
    string Dialect,
    string SpeakingStyle,
    double DefaultSpeed,
    string License,
    string? SourceUrl,
    bool IsEnabled,
    DateTimeOffset UpdatedAtUtc);

public static class VoicePackageEndpoints
{
    private const long MaximumReferenceAudioBytes = 20 * 1024 * 1024;
    private const string GptSoVitsEngine = "gpt-sovits";
    private const string CosyVoiceEngine = "cosyvoice";
    private const string CosyVoice3Model = "FunAudioLLM/Fun-CosyVoice3-0.5B-2512";
    private static readonly HashSet<string> SupportedBaseModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "v1", "v2", "v3", "v4", "v2Pro", "v2ProPlus"
    };

    public static WebApplication MapVoicePackages(this WebApplication app)
    {
        var group = app.MapGroup("/api/v2/system/voice-packages");
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapGet("/{id:guid}/reference-audio", GetReferenceAudioAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{resourceId:guid}", UpdateAsync);
        group.MapDelete("/{resourceId:guid}", ArchiveAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(V2DbContext dbContext, CancellationToken cancellationToken)
    {
        var packages = await dbContext.VoicePackages.AsNoTracking()
            .Where(item => item.IsCurrent && item.IsEnabled)
            .OrderBy(item => item.Dialect)
            .ThenBy(item => item.Name)
            .ToListAsync(cancellationToken);
        return Results.Ok(packages.Select(ToView));
    }

    private static async Task<IResult> GetAsync(Guid id, V2DbContext dbContext, CancellationToken cancellationToken)
    {
        var package = await dbContext.VoicePackages.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return package is null ? Results.NotFound() : Results.Ok(ToView(package));
    }

    private static async Task<IResult> GetReferenceAudioAsync(
        Guid id,
        V2DbContext dbContext,
        CancellationToken cancellationToken)
    {
        var package = await dbContext.VoicePackages.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return package is null || package.ReferenceAudioContent.Length == 0
            ? Results.NotFound()
            : Results.File(
                package.ReferenceAudioContent,
                package.ReferenceAudioContentType,
                package.ReferenceAudioFileName,
                enableRangeProcessing: true);
    }

    private static async Task<IResult> CreateAsync(
        HttpRequest request,
        V2DbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            var input = await ReadInputAsync(request, null, cancellationToken);
            var now = timeProvider.GetUtcNow();
            var package = CreateVersion(input, Guid.NewGuid(), 1, now);
            dbContext.VoicePackages.Add(package);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v2/system/voice-packages/{package.Id}", ToView(package));
        }
        catch (ArgumentException error)
        {
            return Results.BadRequest(new { error = error.Message });
        }
    }

    private static async Task<IResult> UpdateAsync(
        Guid resourceId,
        HttpRequest request,
        V2DbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var current = await dbContext.VoicePackages.SingleOrDefaultAsync(
            item => item.ResourceId == resourceId && item.IsCurrent,
            cancellationToken);
        if (current is null) return Results.NotFound();

        try
        {
            var input = await ReadInputAsync(request, current, cancellationToken);
            current.IsCurrent = false;
            current.UpdatedAtUtc = timeProvider.GetUtcNow();
            var package = CreateVersion(input, resourceId, current.Version + 1, current.UpdatedAtUtc);
            dbContext.VoicePackages.Add(package);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToView(package));
        }
        catch (ArgumentException error)
        {
            return Results.BadRequest(new { error = error.Message });
        }
    }

    private static async Task<IResult> ArchiveAsync(
        Guid resourceId,
        V2DbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var current = await dbContext.VoicePackages.SingleOrDefaultAsync(
            item => item.ResourceId == resourceId && item.IsCurrent,
            cancellationToken);
        if (current is null) return Results.NotFound();

        var now = timeProvider.GetUtcNow();
        current.IsCurrent = false;
        current.UpdatedAtUtc = now;
        dbContext.VoicePackages.Add(new VoicePackage
        {
            ResourceId = resourceId,
            Version = current.Version + 1,
            Name = current.Name,
            Description = current.Description,
            Engine = current.Engine,
            BaseModelVersion = current.BaseModelVersion,
            GptWeightsPath = current.GptWeightsPath,
            SoVitsWeightsPath = current.SoVitsWeightsPath,
            ReferenceAudioFileName = current.ReferenceAudioFileName,
            ReferenceAudioContentType = current.ReferenceAudioContentType,
            ReferenceAudioContent = current.ReferenceAudioContent,
            ReferenceText = current.ReferenceText,
            Language = current.Language,
            Dialect = current.Dialect,
            SpeakingStyle = current.SpeakingStyle,
            DefaultSpeed = current.DefaultSpeed,
            License = current.License,
            SourceUrl = current.SourceUrl,
            IsEnabled = false,
            IsCurrent = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<VoicePackageInput> ReadInputAsync(
        HttpRequest request,
        VoicePackage? current,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
            throw new ArgumentException("请使用 multipart/form-data 提交语音包。");

        var form = await request.ReadFormAsync(cancellationToken);
        var name = Required(form["name"], "名称", 200);
        var description = Optional(form["description"], 2000);
        var engine = Optional(form["engine"], 50);
        if (string.IsNullOrWhiteSpace(engine)) engine = current?.Engine ?? GptSoVitsEngine;
        engine = engine.ToLowerInvariant();
        if (engine is not GptSoVitsEngine and not CosyVoiceEngine)
            throw new ArgumentException("TTS 引擎仅支持 GPT-SoVITS 或 CosyVoice。");
        var baseModelVersion = Required(form["baseModelVersion"], "底模版本", 50);
        if (engine == GptSoVitsEngine && !SupportedBaseModels.Contains(baseModelVersion))
            throw new ArgumentException("底模版本仅支持 v1、v2、v3、v4、v2Pro 或 v2ProPlus。");
        if (engine == CosyVoiceEngine && !string.Equals(baseModelVersion, CosyVoice3Model, StringComparison.Ordinal))
            throw new ArgumentException($"CosyVoice 模型仅支持 {CosyVoice3Model}。");
        var gptWeightsPath = engine == GptSoVitsEngine
            ? Required(form["gptWeightsPath"], "GPT 权重路径", 1000)
            : Optional(form["gptWeightsPath"], 1000);
        var soVitsWeightsPath = engine == GptSoVitsEngine
            ? Required(form["soVitsWeightsPath"], "SoVITS 权重路径", 1000)
            : Optional(form["soVitsWeightsPath"], 1000);
        var referenceText = Required(form["referenceText"], "参考音文本", 2000);
        var language = Required(form["language"], "语言", 40);
        var dialect = Required(form["dialect"], "方言或口音", 100);
        var speakingStyle = Optional(form["speakingStyle"], 2000);
        var license = Required(form["license"], "许可证", 200);
        var sourceUrl = Optional(form["sourceUrl"], 2000);
        if (!double.TryParse(
                form["defaultSpeed"].ToString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var defaultSpeed))
        {
            defaultSpeed = 1;
        }
        if (defaultSpeed is < 0.5 or > 2)
            throw new ArgumentException("默认语速必须在 0.5 到 2.0 之间。");

        var audio = form.Files.GetFile("referenceAudio");
        byte[] audioBytes;
        string audioFileName;
        string audioContentType;
        if (audio is null)
        {
            if (current is null) throw new ArgumentException("必须上传参考 WAV。");
            audioBytes = current.ReferenceAudioContent;
            audioFileName = current.ReferenceAudioFileName;
            audioContentType = current.ReferenceAudioContentType;
        }
        else
        {
            if (audio.Length is < 44 or > MaximumReferenceAudioBytes)
                throw new ArgumentException("参考 WAV 必须大于 44 字节且不超过 20 MB。");
            await using var stream = new MemoryStream();
            await audio.CopyToAsync(stream, cancellationToken);
            audioBytes = stream.ToArray();
            ValidateWave(audioBytes);
            audioFileName = Path.GetFileName(audio.FileName);
            audioContentType = "audio/wav";
        }

        return new(
            name,
            description,
            engine,
            baseModelVersion,
            gptWeightsPath,
            soVitsWeightsPath,
            audioFileName,
            audioContentType,
            audioBytes,
            referenceText,
            language,
            dialect,
            speakingStyle,
            defaultSpeed,
            license,
            string.IsNullOrWhiteSpace(sourceUrl) ? null : sourceUrl);
    }

    private static VoicePackage CreateVersion(
        VoicePackageInput input,
        Guid resourceId,
        int version,
        DateTimeOffset now) => new()
    {
        ResourceId = resourceId,
        Version = version,
        Name = input.Name,
        Description = input.Description,
        Engine = input.Engine,
        BaseModelVersion = input.BaseModelVersion,
        GptWeightsPath = input.GptWeightsPath,
        SoVitsWeightsPath = input.SoVitsWeightsPath,
        ReferenceAudioFileName = input.ReferenceAudioFileName,
        ReferenceAudioContentType = input.ReferenceAudioContentType,
        ReferenceAudioContent = input.ReferenceAudioContent,
        ReferenceText = input.ReferenceText,
        Language = input.Language,
        Dialect = input.Dialect,
        SpeakingStyle = input.SpeakingStyle,
        DefaultSpeed = input.DefaultSpeed,
        License = input.License,
        SourceUrl = input.SourceUrl,
        IsEnabled = true,
        IsCurrent = true,
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };

    private static VoicePackageView ToView(VoicePackage package) => new(
        package.Id,
        package.ResourceId,
        package.Version,
        package.Name,
        package.Description,
        package.Engine,
        package.BaseModelVersion,
        package.GptWeightsPath,
        package.SoVitsWeightsPath,
        package.ReferenceAudioFileName,
        $"/api/v2/system/voice-packages/{package.Id}/reference-audio",
        package.ReferenceText,
        package.Language,
        package.Dialect,
        package.SpeakingStyle,
        package.DefaultSpeed,
        package.License,
        package.SourceUrl,
        package.IsEnabled,
        package.UpdatedAtUtc);

    private static string Required(StringValues value, string field, int maximumLength)
    {
        var text = value.ToString().Trim();
        if (text.Length == 0) throw new ArgumentException($"{field}不能为空。");
        if (text.Length > maximumLength) throw new ArgumentException($"{field}不能超过 {maximumLength} 个字符。");
        return text;
    }

    private static string Optional(StringValues value, int maximumLength)
    {
        var text = value.ToString().Trim();
        if (text.Length > maximumLength) throw new ArgumentException($"字段不能超过 {maximumLength} 个字符。");
        return text;
    }

    private static void ValidateWave(byte[] bytes)
    {
        if (!bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new ArgumentException("参考音频必须是有效的 WAV 文件。");
        }
    }

    private sealed record VoicePackageInput(
        string Name,
        string Description,
        string Engine,
        string BaseModelVersion,
        string GptWeightsPath,
        string SoVitsWeightsPath,
        string ReferenceAudioFileName,
        string ReferenceAudioContentType,
        byte[] ReferenceAudioContent,
        string ReferenceText,
        string Language,
        string Dialect,
        string SpeakingStyle,
        double DefaultSpeed,
        string License,
        string? SourceUrl);
}