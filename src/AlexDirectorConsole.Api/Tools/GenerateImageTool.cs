using System.Text.Json;
using AlexDirectorConsole.Api.Application.Assets;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Services;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class GenerateImageTool(
    IAssetReader assetReader,
    IAssetWriter assetWriter,
    IAzureFoundryImageGenerator imageGenerator) : IDirectorTool
{
    public string Name => "generate_image";

    public bool IsAvailable(DirectorToolContext context) => imageGenerator.IsConfigured;

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, string, string, CancellationToken, Task<string>>)(async (
            imagePrompt,
            resourceName,
            imagePurpose,
            cancellationToken) =>
        {
            await context.ResourceLock.WaitAsync(cancellationToken);
            try
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
                message = $"Agent 正在使用 {context.ImageDeployment} 生成图片（{imageGenerator.Quality}）"
            }, cancellationToken);
            GeneratedImage generatedImage;
            var size = ImageOutputSize.Resolve(imagePurpose, context.ImageSize);
            try
            {
                generatedImage = await imageGenerator.GenerateAsync(
                    prompt,
                    size,
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

            var imageAsset = await ImageAssetWriter.SaveAsync(
                assetWriter,
                context.ProjectId,
                name,
                generatedImage,
                new ImageGenerationMetadata(
                    1,
                    "generate",
                    "azure-openai",
                    generatedImage.Deployment,
                    prompt,
                    generatedImage.RevisedPrompt,
                    new(size, generatedImage.Quality, 1, "png", imageGenerator.ApiVersion),
                    []),
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
            }
            finally
            {
                context.ResourceLock.Release();
            }
        }),
        name: Name,
        description: "使用 Azure Foundry 的 gpt-image-2 部署生成并立即保存一张图片。imagePurpose 必须说明用途：人物三视图、人物/场景/道具设定图或其他视觉参考素材使用 asset（固定 1:1）；shot 首帧、关键帧、分镜图及成片画面使用 project-frame（遵循项目画幅）。默认质量为 medium。工具完成事件会立即事实输出实际提交给图片模型的完整 imagePrompt。批量任务必须严格串行，一次只调用本工具生成一张，收到保存成功回执后才能调用下一张；最终回复不得重复提示词。",
        serializerOptions: context.JsonOptions);
}
