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
        (Func<string, string, string, CancellationToken, Task<string>>)(async (
            imagePrompt,
            resourceName,
            imagePurpose,
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
                message = $"Agent 正在使用 {context.ImageDeployment} 生成图片（{context.ImageGenerator.Quality}）"
            }, cancellationToken);
            GeneratedImage generatedImage;
            try
            {
                generatedImage = await context.ImageGenerator.GenerateAsync(
                    prompt,
                    ImageOutputSize.Resolve(imagePurpose, context.ImageSize),
                    context.ImageDeployment,
                    cancellationToken);
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
            context.ImagePrompts.Add(new("生成图片", imageAsset.Name, prompt));
            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "tool.completed",
                message = $"Agent 已生成图片：{imageAsset.Name} v{imageAsset.Version}",
                data = new
                {
                    asset = AssetResponse.FromAsset(imageAsset, versionCount),
                    deployment = generatedImage.Deployment,
                    quality = generatedImage.Quality,
                    imagePrompt = prompt
                }
            }, cancellationToken);
            return JsonSerializer.Serialize(new
            {
                asset = AssetResponse.FromAsset(imageAsset, versionCount),
                imagePrompt = prompt
            }, context.JsonOptions);
        }),
        name: Name,
        description: "使用 Azure Foundry 的 gpt-image-2 部署生成图片并保存。imagePurpose 必须说明用途：人物三视图、人物/场景/道具设定图或其他视觉参考素材使用 asset（固定 1:1）；shot 首帧、关键帧、分镜图及成片画面使用 project-frame（遵循项目画幅）。默认质量为 medium。返回实际提交给图片模型的完整 imagePrompt；系统会将其完整输出到最终回复，不得省略或改写。",
        serializerOptions: context.JsonOptions);
}
