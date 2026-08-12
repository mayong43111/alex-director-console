using System.Text.Json;
using AlexDirectorConsole.Api.Application.Assets;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Services;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class EditImageTool(
    IAssetReader assetReader,
    IAssetWriter assetWriter,
    IAzureFoundryImageGenerator imageGenerator) : IDirectorTool
{
    public string Name => "edit_image";

    public bool IsAvailable(DirectorToolContext context) => imageGenerator.IsConfigured;

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, string, string, string, CancellationToken, Task<string>>)(async (
            imagePrompt,
            sourceImageName,
            resourceName,
            imagePurpose,
            cancellationToken) =>
        {
            await context.ResourceLock.WaitAsync(cancellationToken);
            try
            {
            var prompt = imagePrompt.Trim();
            var sourceName = sourceImageName.Trim();
            var name = resourceName.Trim();
            if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 8000)
            {
                throw new ArgumentException("图像提示词不能为空且不能超过 8,000 个字符。", nameof(imagePrompt));
            }
            if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
            {
                throw new ArgumentException("资源名称不能为空且不能超过 160 个字符。", nameof(resourceName));
            }

            var mediaAssets = await assetReader.ListAsync(
                context.ProjectId,
                "media",
                cancellationToken);
            var sourceAsset = context.CurrentAsset?.Type == "media"
                && (string.IsNullOrWhiteSpace(sourceName)
                    || sourceName.Contains("当前", StringComparison.OrdinalIgnoreCase))
                    ? context.CurrentAsset
                    : mediaAssets
                        .Where(asset => asset.ContentType.StartsWith("image/"))
                        .Where(asset => asset.Name.Contains(sourceName, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(asset => asset.Version)
                        .ThenByDescending(asset => asset.CreatedAtUtc)
                        .FirstOrDefault();
            if (sourceAsset is null || !sourceAsset.ContentType.StartsWith("image/"))
            {
                throw new InvalidOperationException($"找不到要修改的原图：{sourceName}");
            }

            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "tool.started",
                message = $"Agent 正在读取原图：{sourceAsset.Name} v{sourceAsset.Version}"
            }, cancellationToken);
            await using var sourceImage = await assetReader.OpenReadAsync(context.ProjectId, sourceAsset, cancellationToken)
                ?? throw new InvalidOperationException("原图 Blob 不存在。");
            var size = ImageOutputSize.Resolve(imagePurpose, context.ImageSize);
            var editedImage = await imageGenerator.EditAsync(
                prompt,
                sourceImage,
                sourceAsset.ContentType,
                sourceAsset.FileName,
                size,
                context.ImageDeployment,
                cancellationToken);
            var imageAsset = await ImageAssetWriter.SaveAsync(
                assetWriter,
                context.ProjectId,
                name,
                editedImage,
                new ImageGenerationMetadata(
                    1,
                    "edit",
                    "azure-openai",
                    editedImage.Deployment,
                    prompt,
                    editedImage.RevisedPrompt,
                    new(size, editedImage.Quality, 1, "png", imageGenerator.ApiVersion),
                    [new(sourceAsset.Id, sourceAsset.Name, sourceAsset.Version, "编辑源图")]),
                cancellationToken);
            var versionCount = await assetReader.CountVersionsAsync(
                context.ProjectId,
                imageAsset.ResourceId,
                cancellationToken);
            if (context.RevisedAssets.All(asset => asset.Id != imageAsset.Id))
            {
                context.RevisedAssets.Add(imageAsset);
            }
            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "tool.completed",
                message = $"Agent 已基于原图生成修改版本：{imageAsset.Name} v{imageAsset.Version}",
                data = new
                {
                    sourceAssetId = sourceAsset.Id,
                    asset = AssetResponse.FromAsset(imageAsset, versionCount),
                    deployment = editedImage.Deployment,
                    quality = editedImage.Quality,
                    imagePrompt = prompt
                }
            }, cancellationToken);
            return JsonSerializer.Serialize(new
            {
                asset = AssetResponse.FromAsset(imageAsset, versionCount),
                sourceAsset = AssetResponse.FromAsset(sourceAsset),
                imagePrompt = prompt
            }, context.JsonOptions);
            }
            finally
            {
                context.ResourceLock.Release();
            }
        }),
        name: Name,
        description: "修改已有图片时调用。imagePurpose 必须说明用途：人物三视图、人物/场景/道具设定图或其他视觉参考素材使用 asset（固定 1:1）；shot 首帧、关键帧、分镜图及成片画面使用 project-frame（遵循项目画幅）。工具读取 sourceImageName 对应的最新原图并立即保存为不可变新版本；当前已选图片可写“当前图片”。工具完成事件会立即事实输出实际提交给图片模型的完整 imagePrompt。批量任务必须严格串行，收到保存成功回执后才能修改下一张；最终回复不得重复提示词。",
        serializerOptions: context.JsonOptions);
}
