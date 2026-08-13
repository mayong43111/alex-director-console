using System.Text.Json;
using AlexDirectorConsole.Api.Application.Assets;
using AlexDirectorConsole.Api.Application.Configuration;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Models;
using AlexDirectorConsole.Api.Services;
using Microsoft.Extensions.AI;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace AlexDirectorConsole.Api.Tools;

public sealed class GenerateComfyUiVideoTool(
    IAssetReader assetReader,
    IAssetWriter assetWriter,
    IShotAssetBinder shotAssetBinder,
    IRuntimeConfigurationReader configurationReader,
    IRemoteComfyUiService remoteComfyUiService,
    IComfyUiVideoGenerator comfyUiVideoGenerator) : IDirectorTool
{
    public string Name => "generate_comfyui_video";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, string, string, string, string, string, int, int, int, int, CancellationToken, Task<string>>)(async (
            shotAssetId,
            firstFrameAssetId,
            lastFrameAssetId,
            frameFitMode,
            workflowFileName,
            videoPrompt,
            width,
            height,
            frameCount,
            fps,
            cancellationToken) =>
        {
            if (!Guid.TryParse(shotAssetId, out var parsedShotId) || !Guid.TryParse(firstFrameAssetId, out var parsedFirstFrameId))
                throw new ArgumentException("shotAssetId 和 firstFrameAssetId 必须是有效 UUID。");
            if (!string.IsNullOrWhiteSpace(lastFrameAssetId) && !Guid.TryParse(lastFrameAssetId, out _))
                throw new ArgumentException("lastFrameAssetId 必须为空或有效 UUID。");
            if (width is < 64 or > 4096 || height is < 64 or > 4096 || width % 2 != 0 || height % 2 != 0)
                throw new ArgumentException("视频宽高必须是 64 到 4096 之间的偶数。");
            if (fps is < 1 or > 120 || frameCount < 5 || (frameCount - 5) % 17 != 0)
                throw new ArgumentException("FPS 必须为 1 到 120；MiniMax H3 帧数必须满足 17k+5。");
            var normalizedFrameFitMode = frameFitMode.Trim().ToLowerInvariant();
            if (normalizedFrameFitMode is not ("cover" or "contain"))
                throw new ArgumentException("frameFitMode 仅可为 cover 或 contain。");
            var prompt = videoPrompt.Trim();
            if (prompt.Length is 0 or > 8000) throw new ArgumentException("视频提示词不能为空且不能超过 8,000 字符。");

            var shot = await assetReader.GetAsync(context.ProjectId, parsedShotId, cancellationToken)
                ?? throw new InvalidOperationException("找不到目标 shot。");
            if (shot.Type != "shot") throw new InvalidOperationException("找不到目标 shot。");
            var firstFrame = await PrepareFrameAsync(
                assetReader, context.ProjectId, parsedFirstFrameId, width, height, normalizedFrameFitMode, cancellationToken);
            var lastFrame = string.IsNullOrWhiteSpace(lastFrameAssetId)
                ? null
                : await PrepareFrameAsync(
                    assetReader, context.ProjectId, Guid.Parse(lastFrameAssetId), width, height, normalizedFrameFitMode, cancellationToken);
            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "video.frames",
                message = $"关键帧已由 Agent 等比处理为 {width}x{height}（{normalizedFrameFitMode}）"
            }, cancellationToken);
            var configuration = await configurationReader.GetAsync(context.ProjectId, cancellationToken)
                ?? throw new InvalidOperationException("尚未配置全局 VM 与 ComfyUI。");

            await context.WriteEventAsync(new { type = "process", stage = "video.workflow", message = $"正在读取并提交 workflow：{workflowFileName}" }, cancellationToken);
            var workflowJson = await remoteComfyUiService.ReadWorkflowAsync(configuration, workflowFileName, cancellationToken);
            await remoteComfyUiService.ExecuteActionAsync(configuration, "start-tunnel", cancellationToken);
            var generatedVideo = await comfyUiVideoGenerator.GenerateAsync(new(
                configuration.LocalProxyPort,
                workflowJson,
                firstFrame,
                lastFrame,
                prompt,
                width,
                height,
                frameCount,
                fps,
                shot.Name), cancellationToken);
            var videoAsset = await VideoAssetWriter.SaveAsync(
                assetWriter,
                context.ProjectId,
                shot.Name,
                generatedVideo,
                cancellationToken,
                new(
                    1,
                    "video-generation",
                    "ComfyUI",
                    "MiniMax H3",
                    prompt,
                    new(workflowFileName, width, height, frameCount, fps, normalizedFrameFitMode),
                    string.IsNullOrWhiteSpace(lastFrameAssetId)
                        ? [new(parsedFirstFrameId, "first-frame")]
                        : [
                            new(parsedFirstFrameId, "first-frame"),
                            new(Guid.Parse(lastFrameAssetId), "last-frame")
                        ]));
            await shotAssetBinder.BindAsync(
                context.ProjectId,
                shot.ResourceId,
                videoAsset.Id,
                "video",
                exclusive: true,
                cancellationToken);
            context.RevisedAssets.Add(videoAsset);
            context.VideoPrompts.Add(new(videoAsset.Name, prompt, workflowFileName, width, height, frameCount, fps));
            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "video.completed",
                message = $"已生成并绑定视频：{videoAsset.Name}",
                data = new
                {
                    asset = AssetResponse.FromAsset(videoAsset),
                    videoPrompt = prompt,
                    workflowFileName,
                    width,
                    height,
                    frameCount,
                    fps
                }
            }, cancellationToken);
            return JsonSerializer.Serialize(new
            {
                asset = AssetResponse.FromAsset(videoAsset),
                shot = AssetResponse.FromAsset(shot),
                workflowFileName,
                videoPrompt = prompt,
                frameFitMode = normalizedFrameFitMode,
                width, height, frameCount, fps
            }, context.JsonOptions);
        }),
        name: Name,
        description: "通过项目 VM 上的 ComfyUI API workflow 生成 MiniMax H3 首尾帧视频，下载并验证 MP4 后保存为视频素材，并以 video 角色独占绑定到 shot。shotAssetId 和 firstFrameAssetId 必须传 list_project_resources 返回的 id UUID，严禁传资源名称或镜号；先用 resourceType=shot、nameContains=镜号取得 shotAssetId，再用 resourceType=media、nameContains=镜号与首帧取得 firstFrameAssetId。lastFrameAssetId 同样只能传媒体 id UUID；没有尾帧时传空字符串并复用首帧。workflowFileName 必须是配置目录中的 API prompt JSON，并使用 {{FIRST_FRAME}}、{{LAST_FRAME}}、{{PROMPT}}、{{WIDTH}}、{{HEIGHT}}、{{FRAME_COUNT}}、{{FPS}}、{{OUTPUT_PREFIX}} 占位符。工具会将关键帧等比处理为 width/height：frameFitMode=cover 时居中裁切，contain 时完整保留并补边，禁止非等比拉伸。H3 帧数必须满足 17k+5。",
        serializerOptions: context.JsonOptions);

    private static async Task<byte[]> PrepareFrameAsync(
        IAssetReader assetReader,
        Guid projectId,
        Guid assetId,
        int width,
        int height,
        string frameFitMode,
        CancellationToken cancellationToken)
    {
        var asset = await assetReader.GetAsync(projectId, assetId, cancellationToken)
            ?? throw new InvalidOperationException("找不到项目中的关键帧图片。");
        if (!asset.ContentType.StartsWith("image/"))
            throw new InvalidOperationException("找不到项目中的关键帧图片。");
        await using var stream = await assetReader.OpenReadAsync(projectId, asset, cancellationToken)
            ?? throw new FileNotFoundException("关键帧图片文件不存在。", asset.FileName);
        using var image = await Image.LoadAsync(stream, cancellationToken);
        if (image.Width != width || image.Height != height)
        {
            image.Mutate(operation => operation.Resize(new ResizeOptions
            {
                Size = new Size(width, height),
                Mode = frameFitMode == "cover" ? ResizeMode.Crop : ResizeMode.Pad,
                Position = AnchorPositionMode.Center
            }));
        }
        await using var output = new MemoryStream();
        await image.SaveAsPngAsync(output, cancellationToken);
        return output.ToArray();
    }
}