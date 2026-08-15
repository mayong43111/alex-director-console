using System.Text.Json;
using System.Text.RegularExpressions;
using AlexDirectorConsole.Api.Application.Assets;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Data;
using AlexDirectorConsole.Api.Models;
using AlexDirectorConsole.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed partial class AssembleProjectVideoTool(
    AppDbContext dbContext,
    IAssetReader assetReader,
    IAssetWriter assetWriter,
    ILocalMediaAssemblyService mediaAssemblyService) : IDirectorTool
{
    public string Name => "assemble_project_video";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, int, int, int, bool, string, CancellationToken, Task<string>>)(async (
            resourceName,
            width,
            height,
            fps,
            requireNarration,
            shotNameContains,
            cancellationToken) =>
        {
            var name = resourceName.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
                throw new ArgumentException("资源名称不能为空且不能超过 160 个字符。", nameof(resourceName));
            if (width is < 320 or > 3840 || height is < 180 or > 2160 || width % 2 != 0 || height % 2 != 0)
                throw new ArgumentException("视频宽高必须是有效偶数，且不超过 3840x2160。");
            if (fps is < 12 or > 60)
                throw new ArgumentException("FPS 必须为 12 到 60。", nameof(fps));

            var allAssets = await dbContext.Assets
                .AsNoTracking()
                .Where(asset => asset.ProjectId == context.ProjectId)
                .ToListAsync(cancellationToken);
            var latestAssets = allAssets
                .GroupBy(asset => asset.ResourceId)
                .Select(group => group
                    .OrderByDescending(asset => asset.Version)
                    .ThenByDescending(asset => asset.CreatedAtUtc)
                    .First())
                .ToArray();
            var normalizedShotFilter = shotNameContains.Trim();
            var shots = latestAssets
                .Where(asset => asset.Type == "shot")
                .Where(asset => string.IsNullOrWhiteSpace(normalizedShotFilter)
                    || asset.Name.Contains(normalizedShotFilter, StringComparison.OrdinalIgnoreCase))
                .Select(asset => new { Asset = asset, Match = ShotCodeRegex().Match(asset.Name) })
                .Where(item => item.Match.Success)
                .Select(item => new { item.Asset, ShotCode = item.Match.Value.ToUpperInvariant() })
                .OrderBy(item => item.ShotCode, StringComparer.Ordinal)
                .ToArray();
            if (shots.Length == 0)
                throw new InvalidOperationException("当前项目没有名称包含 Sxx-xx 镜号的镜头资源。");
            var duplicateShotCodes = shots
                .GroupBy(item => item.ShotCode, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            if (duplicateShotCodes.Length > 0)
                throw new InvalidOperationException($"镜号不唯一，无法确定播放顺序：{string.Join("、", duplicateShotCodes)}");

            var shotResourceIds = shots.Select(item => item.Asset.ResourceId).ToArray();
            var shotDefinitions = await dbContext.ShotDefinitions
                .AsNoTracking()
                .Where(definition => definition.ProjectId == context.ProjectId
                    && shotResourceIds.Contains(definition.ShotResourceId))
                .ToArrayAsync(cancellationToken);
            var missingShotDefinitions = shotResourceIds.Except(
                shotDefinitions.Select(definition => definition.ShotResourceId)).ToArray();
            if (missingShotDefinitions.Length > 0)
                throw new InvalidOperationException("部分镜头缺少结构化定义，无法确定成片关联的剧本。");
            var scriptResourceIds = shotDefinitions
                .Select(definition => definition.ScriptResourceId)
                .Distinct()
                .ToArray();
            if (scriptResourceIds.Length != 1)
                throw new InvalidOperationException("所选镜头来自多个剧本，无法组装为一部成片。请使用 shotNameContains 限定同一剧本的镜头。");
            var sourceScript = latestAssets.SingleOrDefault(asset =>
                asset.ResourceId == scriptResourceIds[0] && asset.Type == "script")
                ?? throw new InvalidOperationException("镜头关联的源剧本不存在。");

            var links = await dbContext.ShotAssetLinks
                .AsNoTracking()
                .Where(link => link.ProjectId == context.ProjectId
                    && shotResourceIds.Contains(link.ShotResourceId)
                    && (link.Role == "video" || link.Role == "other"))
                .ToListAsync(cancellationToken);
            var assetsById = allAssets.ToDictionary(asset => asset.Id);
            var audioAssets = latestAssets
                .Where(asset => asset.Type == "media" && asset.ContentType.StartsWith("audio/"))
                .ToArray();
            var sources = new List<AssemblySource>(shots.Length);
            var missingNarration = new List<string>();
            foreach (var shot in shots)
            {
                var video = links
                    .Where(link => link.ShotResourceId == shot.Asset.ResourceId && link.Role == "video")
                    .Where(link => assetsById.TryGetValue(link.AssetId, out var asset)
                        && asset.Type == "media"
                        && asset.ContentType.StartsWith("video/"))
                    .OrderByDescending(link => link.CreatedAtUtc)
                    .Select(link => assetsById[link.AssetId])
                    .FirstOrDefault()
                    ?? throw new InvalidOperationException($"镜头 {shot.ShotCode} 没有有效的 video 绑定。");

                var boundAudio = links
                    .Where(link => link.ShotResourceId == shot.Asset.ResourceId && link.Role == "other")
                    .Where(link => assetsById.TryGetValue(link.AssetId, out var asset)
                        && asset.Type == "media"
                        && asset.ContentType.StartsWith("audio/"))
                    .OrderByDescending(link => link.CreatedAtUtc)
                    .Select(link => assetsById[link.AssetId])
                    .GroupBy(asset => asset.ResourceId)
                    .Select(group => group.First())
                    .ToArray();
                if (boundAudio.Length > 1)
                    throw new InvalidOperationException($"镜头 {shot.ShotCode} 绑定了多个音频，无法自动确定旁白。");

                Asset? audio = boundAudio.SingleOrDefault();
                var audioMatch = audio is null ? "shot-code" : "binding";
                if (audio is null)
                {
                    var namedAudio = audioAssets
                        .Where(asset => ContainsShotCode(asset.Name, shot.ShotCode))
                        .ToArray();
                    if (namedAudio.Length > 1)
                        throw new InvalidOperationException($"镜头 {shot.ShotCode} 匹配到多个旁白音频，请先绑定唯一音频。");
                    audio = namedAudio.SingleOrDefault();
                }
                if (audio is null)
                    missingNarration.Add(shot.ShotCode);
                sources.Add(new AssemblySource(shot.ShotCode, shot.Asset, video, audio, audioMatch));
            }
            if (requireNarration && missingNarration.Count > 0)
                throw new InvalidOperationException($"以下镜头缺少旁白：{string.Join("、", missingNarration)}");

            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "project-video-assembly.started",
                message = $"正在本地组装 {sources.Count} 个镜头，其中 {sources.Count - missingNarration.Count} 个镜头包含配音"
            }, cancellationToken);

            var assemblyClips = new List<MediaAssemblyClip>(sources.Count);
            foreach (var source in sources)
            {
                var videoBytes = await ReadBytesAsync(context.ProjectId, source.Video, cancellationToken);
                var audioBytes = source.Audio is null
                    ? null
                    : await ReadBytesAsync(context.ProjectId, source.Audio, cancellationToken);
                assemblyClips.Add(new MediaAssemblyClip(
                    source.ShotCode,
                    videoBytes,
                    audioBytes,
                    source.Audio is null ? string.Empty : Path.GetExtension(source.Audio.FileName)));
            }

            var result = await mediaAssemblyService.AssembleAsync(
                assemblyClips, width, height, fps, cancellationToken);
            var metadata = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                operation = "assemble-project-video",
                provider = "local-ffmpeg",
                parameters = new
                {
                    width,
                    height,
                    fps,
                    result.DurationSeconds,
                    shotCount = sources.Count,
                    result.AudioClipCount,
                    requireNarration,
                    transition = "fade",
                    result.TransitionDurationSeconds
                },
                sourceScript = new
                {
                    resourceId = sourceScript.ResourceId,
                    assetId = sourceScript.Id,
                    sourceScript.Name,
                    sourceScript.Version
                },
                sources = sources.Select(source => new
                {
                    source.ShotCode,
                    shotAssetId = source.Shot.Id,
                    videoAssetId = source.Video.Id,
                    audioAssetId = source.Audio?.Id,
                    audioMatch = source.Audio is null ? "missing" : source.AudioMatch
                })
            }, JsonSerializerOptions.Web);
            var outputAsset = await assetWriter.WriteVersionAsync(
                new AssetWriteRequest(
                    context.ProjectId,
                    "media",
                    $"{name} · 最终成片",
                    name,
                    ".mp4",
                    "video/mp4",
                    result.Bytes,
                    AssetVersionTarget.ExactName,
                    FileNameFallback: "final-video",
                    GenerationMetadataJson: metadata),
                cancellationToken);
            context.RevisedAssets.Add(outputAsset);
            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "project-video-assembly.completed",
                message = $"最终成片已保存：{outputAsset.Name}（{result.DurationSeconds:0.##} 秒）"
            }, cancellationToken);

            return JsonSerializer.Serialize(new
            {
                asset = AssetResponse.FromAsset(outputAsset),
                result.DurationSeconds,
                result.Width,
                result.Height,
                result.Fps,
                shotCount = sources.Count,
                result.AudioClipCount,
                transition = "fade",
                result.TransitionDurationSeconds,
                missingNarration,
                sourceScript = AssetResponse.FromAsset(sourceScript)
            }, context.JsonOptions);
        }),
        name: Name,
        description: "按 Sxx-xx 镜号顺序读取当前项目镜头，使用每镜最新有效 video 绑定，并优先使用 other 音频绑定、否则按唯一镜号匹配旁白，在本机 FFmpeg 中逐镜对齐后，以统一快速淡入淡出拼接为带配音的最终 MP4。shotNameContains 测试单镜时传镜号，组装全片时传空字符串。旁白长于视频时冻结尾帧，短于视频时补静音；结果和完整来源、转场元数据会持久化为项目媒体资产。",
        serializerOptions: context.JsonOptions);

    private async Task<byte[]> ReadBytesAsync(
        Guid projectId,
        Asset asset,
        CancellationToken cancellationToken)
    {
        await using var stream = await assetReader.OpenReadAsync(projectId, asset, cancellationToken)
            ?? throw new FileNotFoundException($"媒体文件不存在：{asset.FileName}");
        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static bool ContainsShotCode(string value, string shotCode) =>
        Regex.IsMatch(
            value,
            $@"(?<![A-Za-z0-9]){Regex.Escape(shotCode)}(?![A-Za-z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [GeneratedRegex(@"(?<![A-Za-z0-9])S\d{2}-\d{2}(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShotCodeRegex();

    private sealed record AssemblySource(
        string ShotCode,
        Asset Shot,
        Asset Video,
        Asset? Audio,
        string AudioMatch);
}