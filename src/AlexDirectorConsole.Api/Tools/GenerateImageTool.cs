using System.Text.Json;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class GenerateImageTool : IDirectorTool
{
    public string Name => "generate_image";

    public bool IsAvailable(DirectorToolContext context) => context.ImageGenerator.IsConfigured;

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, string, CancellationToken, Task<string>>)(async (
            imagePrompt,
            resourceName,
            cancellationToken) =>
        {
            var prompt = imagePrompt.Trim();
            var name = resourceName.Trim();
            if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 8000)
            {
                throw new ArgumentException("图像提示词不能为空且不能超过 8,000 个字符。", nameof(imagePrompt));
            }
            if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
            {
                throw new ArgumentException("资源名称不能为空且不能超过 160 个字符。", nameof(resourceName));
            }

            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "tool.started",
                message = $"Agent 正在使用 {context.ImageGenerator.Deployment} 生成图片（{context.ImageGenerator.Quality}）"
            }, cancellationToken);
            GeneratedImage generatedImage;
            try
            {
                generatedImage = await context.ImageGenerator.GenerateAsync(prompt, cancellationToken);
            }
            catch (Exception exception)
            {
                await context.WriteEventAsync(new
                {
                    type = "process",
                    stage = "tool.failed",
                    message = $"Azure 图片生成失败：{exception.Message}"
                }, CancellationToken.None);
                throw;
            }

            var imageAsset = await ImageAssetWriter.SaveAsync(context, name, generatedImage, cancellationToken);
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
                message = $"Agent 已生成图片：{imageAsset.Name} v{imageAsset.Version}",
                data = new
                {
                    asset = AssetResponse.FromAsset(imageAsset, versionCount),
                    deployment = generatedImage.Deployment,
                    quality = generatedImage.Quality
                }
            }, cancellationToken);
            return JsonSerializer.Serialize(AssetResponse.FromAsset(imageAsset, versionCount), context.JsonOptions);
        }),
        name: Name,
        description: "使用 Azure Foundry 的 gpt-image-2 部署生成一张 1024x1024 图片并保存为项目素材。默认质量为 medium。导演要求生成、绘制或出图时调用。",
        serializerOptions: context.JsonOptions);
}
