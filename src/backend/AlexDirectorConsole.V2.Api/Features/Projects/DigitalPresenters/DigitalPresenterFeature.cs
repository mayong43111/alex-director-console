using System.Text.RegularExpressions;
using System.Text.Json;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;
using AlexDirectorConsole.V2.Api.Features.Projects.Storyboard;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.ComfyUi;

namespace AlexDirectorConsole.V2.Api.Features.Projects.DigitalPresenters;

public sealed record DigitalPresenterShotView(
    Guid Id,
    int SortOrder,
    string Dialogue,
    string ImagePrompt,
    string VideoPrompt,
    int EffectiveCharacterCount,
    int DurationSeconds,
    Guid? FirstFrameAssetId,
    Guid? VideoAssetId,
    string Status,
    string? Error);

public sealed record DigitalPresenterEpisodeView(
    Guid Id,
    int EpisodeNumber,
    string Title,
    string Dialogue,
    Guid? BackgroundImageAssetId,
    Guid? OutfitImageAssetId,
    string Status,
    IReadOnlyList<DigitalPresenterShotView> Shots,
    DateTimeOffset UpdatedAtUtc);

public sealed record DigitalPresenterView(
    Guid Id,
    string Name,
    Guid IdentityImageAssetId,
    Guid? BackgroundImageAssetId,
    Guid? OutfitImageAssetId,
    Guid VoiceAssetId,
    IReadOnlyList<DigitalPresenterEpisodeView> Episodes,
    DateTimeOffset UpdatedAtUtc);

public sealed record SaveDigitalPresenterEpisodeRequest(
    string? Title,
    string? Dialogue,
    Guid? BackgroundImageAssetId,
    Guid? OutfitImageAssetId);

public sealed record SaveDigitalPresenterShotRequest(string? ImagePrompt, string? VideoPrompt);

public static partial class DigitalPresenterShotSplitter
{
    public const double CharactersPerSecond = 3.8;
    public const double TailBufferSeconds = 1.0;
    private const int MaximumCharacters = 52;

    public static IReadOnlyList<(string Dialogue, int Characters, int DurationSeconds)> Split(string dialogue)
    {
        var normalized = Regex.Replace(dialogue.Trim(), @"\s+", " ");
        if (normalized.Length == 0) return [];
        var result = new List<string>();
        foreach (var sentence in SentenceRegex().Matches(normalized).Select(match => match.Value.Trim()))
        {
            AppendWithinLimit(sentence, result);
        }
        if (result.Count == 0) AppendWithinLimit(normalized, result);
        return result.Select(text =>
        {
            var count = text.Count(char.IsLetterOrDigit);
            var duration = Math.Clamp((int)Math.Ceiling(count / CharactersPerSecond + TailBufferSeconds), 4, 15);
            return (text, count, duration);
        }).ToArray();
    }

    private static void AppendWithinLimit(string text, List<string> result)
    {
        if (text.Count(char.IsLetterOrDigit) <= MaximumCharacters)
        {
            if (text.Length > 0) result.Add(text);
            return;
        }
        var parts = ClauseRegex().Matches(text).Select(match => match.Value.Trim()).Where(value => value.Length > 0);
        var current = string.Empty;
        foreach (var part in parts)
        {
            if (part.Count(char.IsLetterOrDigit) > MaximumCharacters)
            {
                if (current.Length > 0)
                {
                    result.Add(current);
                    current = string.Empty;
                }
                AppendHardLimited(part, result);
                continue;
            }
            if ((current + part).Count(char.IsLetterOrDigit) <= MaximumCharacters)
            {
                current += part;
                continue;
            }
            if (current.Length > 0) result.Add(current);
            current = part;
        }
        if (current.Length > 0) result.Add(current);
    }

    private static void AppendHardLimited(string text, List<string> result)
    {
        var current = new List<char>();
        var count = 0;
        foreach (var character in text)
        {
            current.Add(character);
            if (char.IsLetterOrDigit(character)) count++;
            if (count < MaximumCharacters) continue;
            result.Add(new string([.. current]).Trim());
            current.Clear();
            count = 0;
        }
        if (current.Count > 0) result.Add(new string([.. current]).Trim());
    }

    [GeneratedRegex(@"[^。！？!?]+[。！？!?]?", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceRegex();

    [GeneratedRegex(@"[^，,；;：:]+[，,；;：:]?", RegexOptions.CultureInvariant)]
    private static partial Regex ClauseRegex();
}

public static class DigitalPresenterEndpoints
{
    private const long MaximumImageBytes = 30 * 1024 * 1024;
    private const long MaximumAudioBytes = 15 * 1024 * 1024;
    private static readonly HashSet<string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
        { "image/jpeg", "image/png", "image/webp" };
    private static readonly HashSet<string> AudioTypes = new(StringComparer.OrdinalIgnoreCase)
        { "audio/wav", "audio/x-wav", "audio/mpeg", "audio/mp4", "audio/ogg", "audio/flac" };

    public static IEndpointRouteBuilder MapDigitalPresenters(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/projects/{projectId:guid}/digital-presenters");

        group.MapGet("/", async (Guid projectId, V2DbContext db, CancellationToken ct) =>
            Results.Ok(await ListAsync(projectId, db, ct)));

        group.MapGet("/{presenterId:guid}", async (Guid projectId, Guid presenterId, V2DbContext db, CancellationToken ct) =>
        {
            var item = (await ListAsync(projectId, db, ct)).SingleOrDefault(value => value.Id == presenterId);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        group.MapPost("/", async (Guid projectId, HttpRequest request, V2DbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "请使用 multipart/form-data 创建数字人。" });
            if (!await db.Projects.AnyAsync(item => item.Id == projectId, ct)) return Results.NotFound();
            var form = await request.ReadFormAsync(ct);
            var name = form["name"].ToString().Trim();
            var identity = form.Files.GetFile("identity");
            var voice = form.Files.GetFile("voice");
            if (name.Length is < 1 or > 100) return Results.BadRequest(new { error = "数字人名称必须为 1 至 100 个字符。" });
            if (identity is null || voice is null) return Results.BadRequest(new { error = "人物图片和参考声音为必填项。" });

            var background = form.Files.GetFile("background");
            var outfit = form.Files.GetFile("outfit");
            var imageFiles = new[] { identity, background, outfit }.Where(file => file is not null).Cast<IFormFile>().ToArray();
            if (imageFiles.Any(file => file.Length is <= 0 or > MaximumImageBytes || !ImageTypes.Contains(file.ContentType)))
                return Results.BadRequest(new { error = "图片仅支持 JPG、PNG、WEBP，单张不超过 30 MB。" });
            if (voice.Length is <= 0 or > MaximumAudioBytes || !AudioTypes.Contains(voice.ContentType))
                return Results.BadRequest(new { error = "声音仅支持 WAV、MP3、M4A、OGG、FLAC，不超过 15 MB。" });

            var now = clock.GetUtcNow();
            var number = (await db.Assets.Where(item => item.ProjectId == projectId).Select(item => (int?)item.Number).MaxAsync(ct) ?? 0) + 1;
            var identityAsset = await CreateMediaAsync(projectId, identity, "digital-presenter-image", "人物形象", number++, now, db, ct);
            var backgroundAsset = background is null ? null : await CreateMediaAsync(projectId, background, "digital-presenter-image", "默认背景", number++, now, db, ct);
            var outfitAsset = outfit is null ? null : await CreateMediaAsync(projectId, outfit, "digital-presenter-image", "默认服饰", number++, now, db, ct);
            var voiceAsset = await CreateMediaAsync(projectId, voice, "digital-presenter-voice", "音色参考", number, now, db, ct);
            var presenter = new DigitalPresenter
            {
                ProjectId = projectId,
                Name = name,
                IdentityImageAssetId = identityAsset.Id,
                BackgroundImageAssetId = backgroundAsset?.Id,
                OutfitImageAssetId = outfitAsset?.Id,
                VoiceAssetId = voiceAsset.Id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.DigitalPresenters.Add(presenter);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v2/projects/{projectId}/digital-presenters/{presenter.Id}", ToView(presenter, []));
        });

        group.MapPut("/{presenterId:guid}", async (Guid projectId, Guid presenterId, HttpRequest request, V2DbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "请使用 multipart/form-data 更新数字人素材。" });
            var presenter = await db.DigitalPresenters.SingleOrDefaultAsync(item => item.ProjectId == projectId && item.Id == presenterId, ct);
            if (presenter is null) return Results.NotFound();
            var form = await request.ReadFormAsync(ct);
            var files = form.Files.Where(file => file.Name is "identity" or "background" or "outfit" or "voice").ToArray();
            if (files.Any(file => file.Length <= 0 || (file.Name == "voice" ? !AudioTypes.Contains(file.ContentType) || file.Length > MaximumAudioBytes : !ImageTypes.Contains(file.ContentType) || file.Length > MaximumImageBytes)))
                return Results.BadRequest(new { error = "上传的图片或声音格式、大小不符合要求。" });
            var now = clock.GetUtcNow();
            var number = (await db.Assets.Where(item => item.ProjectId == projectId).Select(item => (int?)item.Number).MaxAsync(ct) ?? 0) + 1;
            foreach (var file in files)
            {
                var asset = await CreateMediaAsync(projectId, file, file.Name == "voice" ? "digital-presenter-voice" : "digital-presenter-image", "数字人参考素材", number++, now, db, ct);
                if (file.Name == "identity") presenter.IdentityImageAssetId = asset.Id;
                else if (file.Name == "background") presenter.BackgroundImageAssetId = asset.Id;
                else if (file.Name == "outfit") presenter.OutfitImageAssetId = asset.Id;
                else presenter.VoiceAssetId = asset.Id;
            }
            presenter.UpdatedAtUtc = now;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToView(presenter, []));
        });

        group.MapDelete("/{presenterId:guid}", async (Guid projectId, Guid presenterId, V2DbContext db, CancellationToken ct) =>
        {
            var presenter = await db.DigitalPresenters.SingleOrDefaultAsync(item => item.ProjectId == projectId && item.Id == presenterId, ct);
            if (presenter is null) return Results.NotFound();
            db.DigitalPresenters.Remove(presenter);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapPost("/{presenterId:guid}/episodes", async (Guid projectId, Guid presenterId, SaveDigitalPresenterEpisodeRequest request, V2DbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!await db.DigitalPresenters.AnyAsync(item => item.ProjectId == projectId && item.Id == presenterId, ct)) return Results.NotFound();
            var error = ValidateEpisode(request);
            if (error is not null) return Results.BadRequest(new { error });
            var now = clock.GetUtcNow();
            var episode = new DigitalPresenterEpisode
            {
                ProjectId = projectId,
                PresenterId = presenterId,
                EpisodeNumber = (await db.DigitalPresenterEpisodes.Where(item => item.PresenterId == presenterId).Select(item => (int?)item.EpisodeNumber).MaxAsync(ct) ?? 0) + 1,
                Title = request.Title!.Trim(),
                Dialogue = request.Dialogue!.Trim(),
                BackgroundImageAssetId = request.BackgroundImageAssetId,
                OutfitImageAssetId = request.OutfitImageAssetId,
                Status = "planned",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.DigitalPresenterEpisodes.Add(episode);
            ReplaceShots(episode, db, now);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v2/projects/{projectId}/digital-presenters/{presenterId}/episodes/{episode.Id}", ToEpisodeView(episode, db.DigitalPresenterShots.Local.Where(item => item.EpisodeId == episode.Id)));
        });

        group.MapPut("/{presenterId:guid}/episodes/{episodeId:guid}", async (Guid projectId, Guid presenterId, Guid episodeId, SaveDigitalPresenterEpisodeRequest request, V2DbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var episode = await db.DigitalPresenterEpisodes.SingleOrDefaultAsync(item => item.ProjectId == projectId && item.PresenterId == presenterId && item.Id == episodeId, ct);
            if (episode is null) return Results.NotFound();
            var error = ValidateEpisode(request);
            if (error is not null) return Results.BadRequest(new { error });
            var now = clock.GetUtcNow();
            episode.Title = request.Title!.Trim();
            episode.Dialogue = request.Dialogue!.Trim();
            episode.BackgroundImageAssetId = request.BackgroundImageAssetId;
            episode.OutfitImageAssetId = request.OutfitImageAssetId;
            episode.Status = "planned";
            episode.UpdatedAtUtc = now;
            db.DigitalPresenterShots.RemoveRange(await db.DigitalPresenterShots.Where(item => item.EpisodeId == episode.Id).ToArrayAsync(ct));
            ReplaceShots(episode, db, now);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToEpisodeView(episode, await db.DigitalPresenterShots.Where(item => item.EpisodeId == episode.Id).OrderBy(item => item.SortOrder).ToArrayAsync(ct)));
        });

        group.MapPut("/{presenterId:guid}/episodes/{episodeId:guid}/shots/{shotId:guid}", async (Guid projectId, Guid presenterId, Guid episodeId, Guid shotId, SaveDigitalPresenterShotRequest request, V2DbContext db, CancellationToken ct) =>
        {
            var shot = await db.DigitalPresenterShots.SingleOrDefaultAsync(item => item.Id == shotId && item.EpisodeId == episodeId && item.ProjectId == projectId, ct);
            if (shot is null || !await db.DigitalPresenterEpisodes.AnyAsync(item => item.Id == episodeId && item.PresenterId == presenterId, ct)) return Results.NotFound();
            if (request.ImagePrompt is not null) shot.ImagePrompt = request.ImagePrompt.Trim();
            if (request.VideoPrompt is not null) shot.VideoPrompt = request.VideoPrompt.Trim();
            shot.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { shot.Id, shot.ImagePrompt, shot.VideoPrompt, shot.Status, shot.FirstFrameAssetId });
        });

        group.MapPost("/{presenterId:guid}/episodes/{episodeId:guid}/shots/{shotId:guid}/image-prompt", async (Guid projectId, Guid presenterId, Guid episodeId, Guid shotId, V2DbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var shot = await db.DigitalPresenterShots.SingleOrDefaultAsync(item => item.Id == shotId && item.EpisodeId == episodeId && item.ProjectId == projectId, ct);
            var episode = shot is null ? null : await db.DigitalPresenterEpisodes.SingleOrDefaultAsync(item => item.Id == episodeId && item.PresenterId == presenterId, ct);
            if (shot is null || episode is null) return Results.NotFound();
            shot.ImagePrompt = BuildImagePrompt(episode.Title, shot.Dialogue);
            shot.UpdatedAtUtc = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { shot.Id, shot.ImagePrompt });
        });

        group.MapPost("/{presenterId:guid}/episodes/{episodeId:guid}/shots/{shotId:guid}/video-prompt", async (Guid projectId, Guid presenterId, Guid episodeId, Guid shotId, V2DbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var shot = await db.DigitalPresenterShots.SingleOrDefaultAsync(item => item.Id == shotId && item.EpisodeId == episodeId && item.ProjectId == projectId, ct);
            if (shot is null || !await db.DigitalPresenterEpisodes.AnyAsync(item => item.Id == episodeId && item.PresenterId == presenterId, ct)) return Results.NotFound();
            shot.VideoPrompt = BuildVideoPrompt(shot.Dialogue);
            shot.UpdatedAtUtc = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { shot.Id, shot.VideoPrompt });
        });

        group.MapPost("/{presenterId:guid}/episodes/{episodeId:guid}/shots/{shotId:guid}/video", async (Guid projectId, Guid presenterId, Guid episodeId, Guid shotId, V2DbContext db, IComfyUiVideoClient client, IComfyUiWorkflowProvider workflowProvider, CancellationToken ct) =>
        {
            var shot = await db.DigitalPresenterShots.SingleOrDefaultAsync(item => item.Id == shotId && item.EpisodeId == episodeId && item.ProjectId == projectId, ct);
            var episode = shot is null ? null : await db.DigitalPresenterEpisodes.SingleOrDefaultAsync(item => item.Id == episodeId && item.PresenterId == presenterId, ct);
            var presenter = episode is null ? null : await db.DigitalPresenters.SingleOrDefaultAsync(item => item.Id == presenterId && item.ProjectId == projectId, ct);
            if (shot is null || episode is null || presenter is null) return Results.NotFound();
            if (shot.FirstFrameAssetId is not Guid firstFrameId) return Results.BadRequest(new { error = "请先完成首帧资源。" });
            if (string.IsNullOrWhiteSpace(shot.VideoPrompt)) return Results.BadRequest(new { error = "请先生成或填写 H3 视频提示词。" });
            var assets = await db.Assets.AsNoTracking().Where(item => item.Id == firstFrameId || item.Id == presenter.VoiceAssetId).ToArrayAsync(ct);
            var firstFrame = assets.SingleOrDefault(item => item.Id == firstFrameId);
            var voice = assets.SingleOrDefault(item => item.Id == presenter.VoiceAssetId);
            if (firstFrame?.BlobContent is null || voice?.BlobContent is null) return Results.BadRequest(new { error = "首帧或声音参考素材为空。" });
            var configuration = await db.ComfyUiConfigurations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == 1, ct);
            if (configuration is null || !configuration.IsEnabled) return Results.BadRequest(new { error = "请先启用 ComfyUI。" });
            var workflow = await workflowProvider.ReadAsync(ComfyUiConfigurationView.MinimaxReferenceVideoWorkflow, ct);
            shot.Status = "video-generating";
            shot.Error = null;
            await db.SaveChangesAsync(ct);
            try
            {
                var promptId = await client.SubmitAsync(new(configuration.BaseUrl, workflow, firstFrame.BlobContent, null, shot.VideoPrompt, 768, 1344, shot.DurationSeconds * 24, 24, voice.BlobContent), ct);
                var deadline = DateTimeOffset.UtcNow.AddHours(1);
                ComfyUiJobResult result;
                do
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                    result = await client.GetResultAsync(configuration.BaseUrl, promptId, ct);
                    if (result.IsFailed) throw new InvalidOperationException($"ComfyUI 视频任务失败：{result.Error}");
                } while (!result.IsCompleted && DateTimeOffset.UtcNow < deadline);
                if (!result.IsCompleted || result.Output is null) throw new TimeoutException("等待 ComfyUI 视频生成完成超时。");
                var bytes = await client.DownloadAsync(configuration.BaseUrl, result.Output, ct);
                var now = DateTimeOffset.UtcNow;
                var number = (await db.Assets.Where(item => item.ProjectId == projectId).Select(item => (int?)item.Number).MaxAsync(ct) ?? 0) + 1;
                var output = new Asset { ProjectId = projectId, ResourceId = shot.Id, Version = 1, Number = number, Type = "digital-presenter-video", Name = $"{presenter.Name} · E{episode.EpisodeNumber:00} · S{shot.SortOrder:00} 视频", BlobKey = $"digital-presenters/{projectId:N}/{presenterId:N}/{episodeId:N}/{shot.Id:N}.mp4", BlobContent = bytes, FileName = $"{shot.Id:N}.mp4", ContentType = "video/mp4", SizeBytes = bytes.LongLength, GenerationMetadataJson = JsonSerializer.Serialize(new { workflow = ComfyUiConfigurationView.MinimaxReferenceVideoWorkflow, promptId, prompt = shot.VideoPrompt }), CreatedAtUtc = now, UpdatedAtUtc = now };
                db.Assets.Add(output);
                shot.VideoAssetId = output.Id;
                shot.Status = "video-ready";
                shot.UpdatedAtUtc = now;
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { shot.Id, shot.VideoAssetId, shot.Status });
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                shot.Status = "failed";
                shot.Error = error.Message;
                await db.SaveChangesAsync(CancellationToken.None);
                return Results.BadRequest(new { error = error.Message });
            }
        });

        group.MapPost("/{presenterId:guid}/episodes/{episodeId:guid}/shots/{shotId:guid}/first-frame", async (Guid projectId, Guid presenterId, Guid episodeId, Guid shotId, SaveDigitalPresenterShotRequest request, V2DbContext db, IShotFrameGenerator generator, TimeProvider clock, CancellationToken ct) =>
        {
            var shot = await db.DigitalPresenterShots.SingleOrDefaultAsync(item => item.Id == shotId && item.EpisodeId == episodeId && item.ProjectId == projectId, ct);
            var episode = shot is null ? null : await db.DigitalPresenterEpisodes.SingleOrDefaultAsync(item => item.Id == episodeId && item.PresenterId == presenterId, ct);
            var presenter = episode is null ? null : await db.DigitalPresenters.SingleOrDefaultAsync(item => item.Id == presenterId && item.ProjectId == projectId, ct);
            if (shot is null || episode is null || presenter is null) return Results.NotFound();
            if (!string.IsNullOrWhiteSpace(request.ImagePrompt)) shot.ImagePrompt = request.ImagePrompt.Trim();
            if (string.IsNullOrWhiteSpace(shot.ImagePrompt)) return Results.BadRequest(new { error = "请先生成或填写图片提示词。" });
            var referenceIds = new[] { presenter.IdentityImageAssetId, episode.BackgroundImageAssetId ?? presenter.BackgroundImageAssetId, episode.OutfitImageAssetId ?? presenter.OutfitImageAssetId }.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
            var references = await db.Assets.AsNoTracking().Where(item => referenceIds.Contains(item.Id) && item.BlobContent != null).ToArrayAsync(ct);
            if (references.Length == 0) return Results.BadRequest(new { error = "首帧生成至少需要一张人物参考图。" });
            var generated = await generator.GenerateAsync(shot.ImagePrompt, "768x1344", references.Select(asset => new ShotFrameReference(asset.BlobContent!, asset.ContentType ?? "image/png", asset.FileName ?? "reference.png", asset.Id == presenter.IdentityImageAssetId ? "character" : "scene", asset.Name, asset.Id, asset.ResourceId, asset.Version)).ToArray(), ct);
            var now = clock.GetUtcNow();
            var number = (await db.Assets.Where(item => item.ProjectId == projectId).Select(item => (int?)item.Number).MaxAsync(ct) ?? 0) + 1;
            var output = new Asset { ProjectId = projectId, ResourceId = shot.Id, Version = 1, Number = number, Type = "digital-presenter-first-frame", Name = $"{presenter.Name} · E{episode.EpisodeNumber:00} · S{shot.SortOrder:00} 首帧", BlobKey = $"digital-presenters/{projectId:N}/{presenterId:N}/{episodeId:N}/{shot.Id:N}.png", BlobContent = generated.Bytes, FileName = $"{shot.Id:N}.png", ContentType = generated.ContentType, SizeBytes = generated.Bytes.LongLength, CreatedAtUtc = now, UpdatedAtUtc = now };
            db.Assets.Add(output);
            shot.FirstFrameAssetId = output.Id;
            shot.Status = "first-frame-ready";
            shot.Error = null;
            shot.UpdatedAtUtc = now;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { shot.Id, shot.FirstFrameAssetId, shot.Status, shot.ImagePrompt, shot.VideoPrompt });
        });

        group.MapGet("/media/{assetId:guid}", async (Guid projectId, Guid assetId, V2DbContext db, CancellationToken ct) =>
        {
            var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(item => item.ProjectId == projectId && item.Id == assetId && item.BlobContent != null, ct);
            return asset is null ? Results.NotFound() : Results.File(asset.BlobContent!, asset.ContentType ?? "application/octet-stream", enableRangeProcessing: true);
        });

        return app;
    }

    private static string? ValidateEpisode(SaveDigitalPresenterEpisodeRequest request) =>
        string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 100 ? "剧集标题必须为 1 至 100 个字符。" :
        string.IsNullOrWhiteSpace(request.Dialogue) || request.Dialogue.Trim().Length > 12000 ? "对白必须为 1 至 12000 个字符。" : null;

    private static void ReplaceShots(DigitalPresenterEpisode episode, V2DbContext db, DateTimeOffset now)
    {
        var shots = DigitalPresenterShotSplitter.Split(episode.Dialogue);
        foreach (var (shot, index) in shots.Select((value, index) => (value, index)))
        {
            db.DigitalPresenterShots.Add(new DigitalPresenterShot
            {
                ProjectId = episode.ProjectId,
                EpisodeId = episode.Id,
                SortOrder = index + 1,
                Dialogue = shot.Dialogue,
                ImagePrompt = string.Empty,
                VideoPrompt = string.Empty,
                EffectiveCharacterCount = shot.Characters,
                DurationSeconds = shot.DurationSeconds,
                Status = "planned",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }
    }

    private static async Task<Asset> CreateMediaAsync(Guid projectId, IFormFile file, string type, string label, int number, DateTimeOffset now, V2DbContext db, CancellationToken ct)
    {
        await using var source = file.OpenReadStream();
        await using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, ct);
        var resourceId = Guid.NewGuid();
        var asset = new Asset
        {
            ProjectId = projectId,
            ResourceId = resourceId,
            Version = 1,
            Number = number,
            Type = type,
            Name = $"{label} · {Path.GetFileNameWithoutExtension(file.FileName)}",
            BlobKey = $"digital-presenters/{projectId:N}/{resourceId:N}/{Path.GetFileName(file.FileName)}",
            BlobContent = buffer.ToArray(),
            FileName = Path.GetFileName(file.FileName),
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Assets.Add(asset);
        return asset;
    }

    private static async Task<IReadOnlyList<DigitalPresenterView>> ListAsync(Guid projectId, V2DbContext db, CancellationToken ct)
    {
        var presenters = (await db.DigitalPresenters.AsNoTracking().Where(item => item.ProjectId == projectId).ToArrayAsync(ct))
            .OrderByDescending(item => item.UpdatedAtUtc)
            .ToArray();
        if (presenters.Length == 0)
        {
            return [];
        }

        var ids = presenters.Select(item => item.Id).ToArray();
        var episodes = await db.DigitalPresenterEpisodes.AsNoTracking().Where(item => ids.Contains(item.PresenterId)).OrderBy(item => item.EpisodeNumber).ToArrayAsync(ct);
        if (episodes.Length == 0)
        {
            return presenters.Select(presenter => ToView(presenter, [])).ToArray();
        }

        var episodeIds = episodes.Select(item => item.Id).ToArray();
        var shots = await db.DigitalPresenterShots.AsNoTracking().Where(item => episodeIds.Contains(item.EpisodeId)).OrderBy(item => item.SortOrder).ToArrayAsync(ct);
        return presenters.Select(presenter => ToView(presenter, episodes.Where(item => item.PresenterId == presenter.Id).Select(episode => ToEpisodeView(episode, shots.Where(item => item.EpisodeId == episode.Id))).ToArray())).ToArray();
    }

    private static DigitalPresenterView ToView(DigitalPresenter item, IReadOnlyList<DigitalPresenterEpisodeView> episodes) =>
        new(item.Id, item.Name, item.IdentityImageAssetId, item.BackgroundImageAssetId, item.OutfitImageAssetId, item.VoiceAssetId, episodes, item.UpdatedAtUtc);

    private static DigitalPresenterEpisodeView ToEpisodeView(DigitalPresenterEpisode item, IEnumerable<DigitalPresenterShot> shots) =>
        new(item.Id, item.EpisodeNumber, item.Title, item.Dialogue, item.BackgroundImageAssetId, item.OutfitImageAssetId, item.Status,
            shots.Select(shot => new DigitalPresenterShotView(shot.Id, shot.SortOrder, shot.Dialogue, shot.ImagePrompt, shot.VideoPrompt, shot.EffectiveCharacterCount, shot.DurationSeconds, shot.FirstFrameAssetId, shot.VideoAssetId, shot.Status, shot.Error)).ToArray(), item.UpdatedAtUtc);

    private static string BuildImagePrompt(string title, string dialogue) => $"写实数字人播报首帧，人物身份和服饰保持一致，面对镜头自然表达，9:16 竖屏构图。主题：{title}。对白：{dialogue}。无字幕、无文字、无水印。";
    private static string BuildVideoPrompt(string dialogue) => $"(S1) is the only on-screen speaker. Keep the supplied identity, wardrobe, background, framing, and camera axis consistent. Natural restrained presenter gestures, stable lighting, clear Mandarin voice, and accurate lip synchronization. The speaker says exactly once in Mandarin Chinese: <d>[Chinese] {dialogue}</d>. Start at 0.00 seconds, finish the line before the final 1.0 second, then remain vocally silent. No subtitles, captions, logos, watermarks, cuts, or extra speech.";
}