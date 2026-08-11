using System.Text.Json;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class GenerateImageFromReferencesTool : IDirectorTool
{
    public string Name => "generate_image_from_references";

    public bool IsAvailable(DirectorToolContext context) => context.ImageGenerator.IsConfigured;

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, string, string, CancellationToken, Task<string>>)(async (
            imagePrompt,
            referenceImageAssetIds,
            resourceName,
            cancellationToken) =>
        {
            var prompt = imagePrompt.Trim();
            var name = resourceName.Trim();
            var ids = referenceImageAssetIds
                .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .Take(10)
                .ToArray();
            if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 8000)
            {
                throw new ArgumentException("图像提示词不能为空且不能超过 8,000 个字符。", nameof(imagePrompt));
            }
            if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
            {
                throw new ArgumentException("资源名称不能为空且不能超过 160 个字符。", nameof(resourceName));
            }
            if (ids.Length == 0)
            {
                throw new ArgumentException("首帧生成必须提供至少一个真实参考图片资产 ID。", nameof(referenceImageAssetIds));
            }

            var assets = await context.DbContext.Assets
                .AsNoTracking()
                .Where(asset => asset.ProjectId == context.ProjectId && ids.Contains(asset.Id))
                .ToListAsync(cancellationToken);
            if (assets.Count != ids.Length || assets.Any(asset => !asset.ContentType.StartsWith("image/")))
            {
                throw new ArgumentException("所有参考 ID 都必须是当前项目中的真实图片资产。", nameof(referenceImageAssetIds));
            }

            var referenceImages = new List<ReferenceImageInput>(assets.Count);
            foreach (var id in ids)
            {
                var asset = assets.Single(item => item.Id == id);
                await using var stream = await context.BlobStorage.OpenReadAsync(asset.BlobKey, cancellationToken)
                    ?? throw new InvalidOperationException($"参考图 Blob 不存在：{asset.Name}");
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory, cancellationToken);
                referenceImages.Add(new(memory.ToArray(), asset.ContentType, asset.FileName));
            }

            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "tool.started",
                message = $"Agent 正在使用 {assets.Count} 张人物/场景/道具参考图生成首帧（{context.ImageGenerator.Quality}）"
            }, cancellationToken);
            var generatedImage = await context.ImageGenerator.GenerateFromReferencesAsync(
                prompt,
                referenceImages,
                cancellationToken);
            var imageAsset = await ImageAssetWriter.SaveAsync(context, name, generatedImage, cancellationToken);
            var versionCount = await context.DbContext.Assets.CountAsync(
                asset => asset.ResourceId == imageAsset.ResourceId,
                cancellationToken);
            context.RevisedAssets.Add(imageAsset);
            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "tool.completed",
                message = $"Agent 已基于 {assets.Count} 张参考图生成首帧：{imageAsset.Name} v{imageAsset.Version}",
                data = new
                {
                    referenceAssets = assets.Select(asset => AssetResponse.FromAsset(asset)),
                    asset = AssetResponse.FromAsset(imageAsset, versionCount)
                }
            }, cancellationToken);
            return JsonSerializer.Serialize(new
            {
                asset = AssetResponse.FromAsset(imageAsset, versionCount),
                referenceAssets = assets.Select(AssetResponse.FromAsset)
            }, context.JsonOptions);
        }),
        name: Name,
        description: "使用明确选择的人物、场景、道具图片资产作为多张真实参考图生成 shot 首帧。不能用纯文本替代参考图；referenceImageAssetIds 用逗号分隔。",
        serializerOptions: context.JsonOptions);
}