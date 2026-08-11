using System.Text.Json;
using AlexDirectorConsole.Api.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class EditImageTool : IDirectorTool
{
    public string Name => "edit_image";

    public bool IsAvailable(DirectorToolContext context) => context.ImageGenerator.IsConfigured;

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, string, string, string, CancellationToken, Task<string>>)(async (
            imagePrompt,
            sourceImageName,
            resourceName,
            imagePurpose,
            cancellationToken) =>
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

            var mediaAssets = await context.DbContext.Assets
                .AsNoTracking()
                .Where(asset => asset.ProjectId == context.ProjectId && asset.Type == "media")
                .ToListAsync(cancellationToken);
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
            await using var sourceImage = await context.BlobStorage.OpenReadAsync(
                sourceAsset.BlobKey,
                cancellationToken)
                ?? throw new InvalidOperationException("原图 Blob 不存在。");
            var editedImage = await context.ImageGenerator.EditAsync(
                prompt,
                sourceImage,
                sourceAsset.ContentType,
                sourceAsset.FileName,
                ImageOutputSize.Resolve(imagePurpose, context.ImageSize),
                context.ImageDeployment,
                cancellationToken);
            var imageAsset = await ImageAssetWriter.SaveAsync(context, name, editedImage, cancellationToken);
            var versionCount = await context.DbContext.Assets.CountAsync(
                asset => asset.ResourceId == imageAsset.ResourceId,
                cancellationToken);
            if (context.RevisedAssets.All(asset => asset.Id != imageAsset.Id))
            {
                context.RevisedAssets.Add(imageAsset);
            }
            context.ImagePrompts.Add(new("修改图片", imageAsset.Name, prompt));
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
        }),
        name: Name,
        description: "修改已有图片时调用。imagePurpose 必须说明用途：人物三视图、人物/场景/道具设定图或其他视觉参考素材使用 asset（固定 1:1）；shot 首帧、关键帧、分镜图及成片画面使用 project-frame（遵循项目画幅）。工具读取 sourceImageName 对应的最新原图并保存为不可变新版本；当前已选图片可写“当前图片”。返回实际提交给图片模型的完整 imagePrompt；系统会将其完整输出到最终回复，不得省略或改写。",
        serializerOptions: context.JsonOptions);
}
