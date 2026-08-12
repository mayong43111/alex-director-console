using System.Text.Json;
using AlexDirectorConsole.Api.Application.Assets;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Services;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class GenerateImageFromReferencesTool(
    IAssetReader assetReader,
    IAssetWriter assetWriter,
    IAzureFoundryImageGenerator imageGenerator) : IDirectorTool
{
    public string Name => "generate_image_from_references";

    public bool IsAvailable(DirectorToolContext context) => imageGenerator.IsConfigured;

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, string, string, string, CancellationToken, Task<string>>)(async (
            imagePrompt,
            referenceImageAssetIds,
            referenceImageDescriptions,
            resourceName,
            cancellationToken) =>
        {
            await context.ResourceLock.WaitAsync(cancellationToken);
            try
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
            var descriptions = ParseDescriptions(referenceImageDescriptions);
            if (descriptions.Length != ids.Length)
            {
                throw new ArgumentException("referenceImageDescriptions 必须是与参考图 ID 同序、同数量的 JSON 字符串数组。", nameof(referenceImageDescriptions));
            }

            var assets = (await assetReader.ListAsync(
                    context.ProjectId,
                    cancellationToken: cancellationToken))
                .Where(asset => ids.Contains(asset.Id))
                .ToList();
            if (assets.Count != ids.Length || assets.Any(asset => !asset.ContentType.StartsWith("image/")))
            {
                throw new ArgumentException("所有参考 ID 都必须是当前项目中的真实图片资产。", nameof(referenceImageAssetIds));
            }

            var referenceImages = new List<ReferenceImageInput>(assets.Count);
            foreach (var id in ids)
            {
                var asset = assets.Single(item => item.Id == id);
                await using var stream = await assetReader.OpenReadAsync(context.ProjectId, asset, cancellationToken)
                    ?? throw new InvalidOperationException($"参考图 Blob 不存在：{asset.Name}");
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory, cancellationToken);
                referenceImages.Add(new(memory.ToArray(), asset.ContentType, asset.FileName));
            }

            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "tool.started",
                message = $"Agent 正在使用 {assets.Count} 张人物/场景/道具参考图生成首帧（{imageGenerator.Quality}）"
            }, cancellationToken);
            var describedPrompt = $"""
                参考图说明（严格按上传顺序对应）：
                {string.Join(Environment.NewLine, descriptions.Select((description, index) => $"- 参考图 {index + 1}：{description}"))}

                生成要求：
                {prompt}
                """;
            var generatedImage = await imageGenerator.GenerateFromReferencesAsync(
                describedPrompt,
                referenceImages,
                context.ImageSize,
                context.ImageDeployment,
                cancellationToken);
            var imageAsset = await ImageAssetWriter.SaveAsync(
                assetWriter,
                context.ProjectId,
                name,
                generatedImage,
                new ImageGenerationMetadata(
                    1,
                    "generate-from-references",
                    "azure-openai",
                    generatedImage.Deployment,
                    describedPrompt,
                    generatedImage.RevisedPrompt,
                    new(context.ImageSize, generatedImage.Quality, 1, "png", imageGenerator.ApiVersion),
                    ids.Select((id, index) =>
                    {
                        var asset = assets.Single(item => item.Id == id);
                        return new ImageGenerationSource(
                            asset.Id,
                            asset.Name,
                            asset.Version,
                            descriptions[index]);
                    }).ToArray()),
                cancellationToken);
            var versionCount = await assetReader.CountVersionsAsync(
                context.ProjectId,
                imageAsset.ResourceId,
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
                    asset = AssetResponse.FromAsset(imageAsset, versionCount),
                    imagePrompt = describedPrompt
                }
            }, cancellationToken);
            return JsonSerializer.Serialize(new
            {
                asset = AssetResponse.FromAsset(imageAsset, versionCount),
                referenceAssets = assets.Select(AssetResponse.FromAsset),
                imagePrompt = describedPrompt
            }, context.JsonOptions);
            }
            finally
            {
                context.ResourceLock.Release();
            }
        }),
        name: Name,
        description: "使用明确选择的人物、场景、道具图片资产生成并立即保存一张 shot 首帧，按当前项目画幅输出。referenceImageAssetIds 用逗号分隔；referenceImageDescriptions 必须是同序 JSON 字符串数组，逐项明确每张参考图是什么、对应哪个人物/场景/道具以及要继承的视觉内容。工具完成事件会立即事实输出包含全部逐图说明、实际提交给图片模型的最终完整 imagePrompt。批量任务必须严格串行，收到保存和绑定成功回执后才能生成下一张；最终回复不得重复提示词。",
        serializerOptions: context.JsonOptions);

    private static string[] ParseDescriptions(string value)
    {
        try
        {
            var descriptions = JsonSerializer.Deserialize<string[]>(value) ?? [];
            if (descriptions.Any(description => string.IsNullOrWhiteSpace(description) || description.Length > 500))
            {
                throw new ArgumentException("每条参考图说明必须非空且不能超过 500 个字符。", nameof(value));
            }
            return descriptions.Select(description => description.Trim()).ToArray();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("referenceImageDescriptions 必须是 JSON 字符串数组。", nameof(value), exception);
        }
    }
}