using System.Text;
using System.Text.Json;
using AlexDirectorConsole.Api.Application.Assets;
using AlexDirectorConsole.Api.Application.Configuration;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Services;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class AssembleVideoClipsTool(
    IAssetReader assetReader,
    IAssetWriter assetWriter,
    IRuntimeConfigurationReader configurationReader,
    IRemoteComfyUiService remoteComfyUiService) : IDirectorTool
{
    public string Name => "assemble_video_clips";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, string, int, int, int, CancellationToken, Task<string>>)(async (
            videoAssetIds,
            resourceName,
            width,
            height,
            fps,
            cancellationToken) =>
        {
            var ids = videoAssetIds
                .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToArray();
            if (ids.Length is < 2 or > 30 || ids.Distinct().Count() != ids.Length)
                throw new ArgumentException("videoAssetIds 必须按播放顺序提供 2 到 30 个不重复的当前项目视频资产 ID。", nameof(videoAssetIds));
            var name = resourceName.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
                throw new ArgumentException("资源名称不能为空且不能超过 160 个字符。", nameof(resourceName));
            if (width is < 320 or > 3840 || height is < 180 or > 2160 || width % 2 != 0 || height % 2 != 0)
                throw new ArgumentException("视频宽高必须是有效偶数，且不超过 3840x2160。");
            if (fps is < 12 or > 60) throw new ArgumentException("FPS 必须为 12 到 60。");

            var assets = (await assetReader.ListAsync(
                    context.ProjectId,
                    cancellationToken: cancellationToken))
                .Where(asset => ids.Contains(asset.Id))
                .ToList();
            if (assets.Count != ids.Length || assets.Any(asset => !asset.ContentType.StartsWith("video/")))
                throw new ArgumentException("所有 ID 都必须是当前项目中真实存在的视频资产。", nameof(videoAssetIds));
            var orderedAssets = ids.Select(id => assets.Single(asset => asset.Id == id)).ToArray();
            var clipBytes = new List<byte[]>(orderedAssets.Length);
            foreach (var asset in orderedAssets)
            {
                await using var stream = await assetReader.OpenReadAsync(context.ProjectId, asset, cancellationToken)
                    ?? throw new FileNotFoundException($"视频文件不存在：{asset.FileName}");
                await using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);
                var bytes = buffer.ToArray();
                if (bytes.Length < 1024 || Encoding.ASCII.GetString(bytes, 4, 4) != "ftyp")
                    throw new InvalidOperationException($"视频文件无效：{asset.FileName}");
                clipBytes.Add(bytes);
            }

            var configuration = await configurationReader.GetAsync(context.ProjectId, cancellationToken)
                ?? throw new InvalidOperationException("尚未配置全局 VM。");
            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "video-assembly.started",
                message = $"Agent 正在拼接 {ids.Length} 个视频片段"
            }, cancellationToken);
            var generatedVideo = await remoteComfyUiService.AssembleVideoClipsAsync(
                configuration, clipBytes, width, height, fps, cancellationToken);
            var videoAsset = await VideoAssetWriter.SaveAsync(
                assetWriter,
                context.ProjectId,
                name,
                generatedVideo,
                cancellationToken);
            context.RevisedAssets.Add(videoAsset);
            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "video-assembly.completed",
                message = $"视频成片已校验并保存：{videoAsset.Name}（{width}x{height}，{fps} FPS）"
            }, cancellationToken);
            return JsonSerializer.Serialize(new
            {
                asset = AssetResponse.FromAsset(videoAsset),
                videoAssetIds = ids,
                width,
                height,
                fps
            }, context.JsonOptions);
        }),
        name: Name,
        description: "将当前项目的多个视频素材按 videoAssetIds 顺序拼接为一个 MP4 成片。工具在项目 VM 上使用 FFmpeg 统一分辨率和 FPS，下载后校验 MP4 签名、分辨率、FPS 和有效时长，再保存为当前项目视频素材。",
        serializerOptions: context.JsonOptions);
}