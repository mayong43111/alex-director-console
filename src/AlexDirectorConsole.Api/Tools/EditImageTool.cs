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
        (Func<string, string, string, CancellationToken, Task<string>>)(async (
            imagePrompt,
            sourceImageName,
            resourceName,
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
                cancellationToken);
            var imageAsset = await ImageAssetWriter.SaveAsync(context, name, editedImage, cancellationToken);
            var versionCount = await context.DbContext.Assets.CountAsync(
                asset => asset.ResourceId == imageAsset.ResourceId,
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
                    quality = editedImage.Quality
                }
            }, cancellationToken);
            return JsonSerializer.Serialize(AssetResponse.FromAsset(imageAsset, versionCount), context.JsonOptions);
        }),
        name: Name,
        description: "修改已有图片时调用。工具会读取 sourceImageName 对应的最新原图，并把原图连同 imagePrompt 一起传给 Azure 图片编辑接口，再保存为不可变的新版本；当前已选图片可写“当前图片”。",
        serializerOptions: context.JsonOptions);
}
