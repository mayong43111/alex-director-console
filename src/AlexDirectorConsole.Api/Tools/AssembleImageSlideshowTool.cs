using System.Text.Json;
using AlexDirectorConsole.Api.Application.Assets;
using AlexDirectorConsole.Api.Application.Configuration;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Services;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class AssembleImageSlideshowTool(
    IAssetReader assetReader,
    IAssetWriter assetWriter,
    IRuntimeConfigurationReader configurationReader,
    IRemoteComfyUiService remoteComfyUiService) : IDirectorTool
{
    public string Name => "assemble_image_slideshow";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, string, int, int, int, int, CancellationToken, Task<string>>)(async (
            imageAssetIds,
            resourceName,
            width,
            height,
            fps,
            durationSeconds,
            cancellationToken) =>
        {
            var ids = imageAssetIds
                .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToArray();
            if (ids.Length is < 2 or > 30 || ids.Distinct().Count() != ids.Length)
                throw new ArgumentException("imageAssetIds 必须按播放顺序提供 2 到 30 个不重复的当前项目图片资产 ID。", nameof(imageAssetIds));
            var name = resourceName.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
                throw new ArgumentException("资源名称不能为空且不能超过 160 个字符。", nameof(resourceName));
            if (width is < 320 or > 3840 || height is < 180 or > 2160 || width % 2 != 0 || height % 2 != 0)
                throw new ArgumentException("视频宽高必须是有效偶数，且不超过 3840x2160。");
            if (fps is < 12 or > 60 || durationSeconds is < 10 or > 600)
                throw new ArgumentException("FPS 必须为 12 到 60，时长必须为 10 到 600 秒。");

            var assets = (await assetReader.ListAsync(
                    context.ProjectId,
                    cancellationToken: cancellationToken))
                .Where(asset => ids.Contains(asset.Id))
                .ToList();
            if (assets.Count != ids.Length || assets.Any(asset => !asset.ContentType.StartsWith("image/")))
                throw new ArgumentException("所有 ID 都必须是当前项目中真实存在的图片资产。", nameof(imageAssetIds));
            var orderedAssets = ids.Select(id => assets.Single(asset => asset.Id == id)).ToArray();
            var imageBytes = new List<byte[]>(orderedAssets.Length);
            foreach (var asset in orderedAssets)
            {
                await using var stream = await assetReader.OpenReadAsync(context.ProjectId, asset, cancellationToken)
                    ?? throw new FileNotFoundException($"图片文件不存在：{asset.FileName}");
                await using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);
                imageBytes.Add(buffer.ToArray());
            }

            var configuration = await configurationReader.GetAsync(context.ProjectId, cancellationToken)
                ?? throw new InvalidOperationException("尚未配置全局 VM。");
            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "slideshow.started",
                message = $"Agent 正在将 {ids.Length} 张图片组装为 {durationSeconds} 秒视频"
            }, cancellationToken);
            var generatedVideo = await remoteComfyUiService.AssembleSlideshowAsync(
                configuration, imageBytes, width, height, fps, durationSeconds, cancellationToken);
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
                stage = "slideshow.completed",
                message = $"静帧成片已校验并保存：{videoAsset.Name}（{width}x{height}，{fps} FPS，{durationSeconds} 秒）"
            }, cancellationToken);
            return JsonSerializer.Serialize(new
            {
                asset = AssetResponse.FromAsset(videoAsset),
                imageAssetIds = ids,
                width,
                height,
                fps,
                durationSeconds
            }, context.JsonOptions);
        }),
        name: Name,
        description: "将当前项目的图片素材按 imageAssetIds 顺序组装为静帧介绍视频。工具在项目 VM 上使用 FFmpeg 等比放大并居中裁切，禁止非等比拉伸；下载后校验 MP4 签名、分辨率、FPS 和精确时长，再保存为当前项目视频素材。适用于文生图驱动的介绍片、概念片和幻灯成片，不调用 H3。",
        serializerOptions: context.JsonOptions);
}