using System.Text.Json;
using AlexDirectorConsole.Api.Application.Assets;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Services;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class GenerateSpeechTool(
    IAssetReader assetReader,
    IAssetWriter assetWriter,
    IAzureFoundrySpeechGenerator speechGenerator) : IDirectorTool
{
    private static readonly HashSet<string> ValidVoices = new(StringComparer.OrdinalIgnoreCase)
    {
        "alloy", "echo", "fable", "nova", "onyx", "shimmer"
    };

    private static readonly HashSet<string> ValidFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp3", "wav", "opus", "aac", "flac", "pcm"
    };

    public string Name => "generate_speech";

    public bool IsAvailable(DirectorToolContext context) => speechGenerator.IsConfigured;

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, string, string, double, string, string, CancellationToken, Task<string>>)(async (
            text,
            voice,
            deliveryInstructions,
            speed,
            responseFormat,
            resourceName,
            cancellationToken) =>
        {
            await context.ResourceLock.WaitAsync(cancellationToken);
            try
            {
                var normalizedText = text.Trim();
                var normalizedVoice = voice.Trim().ToLowerInvariant();
                var normalizedInstructions = deliveryInstructions.Trim();
                var normalizedFormat = responseFormat.Trim().ToLowerInvariant();
                var normalizedName = resourceName.Trim();
                if (normalizedText.Length is 0 or > 4096)
                    throw new ArgumentException("配音文本不能为空且不能超过 4,096 个字符。", nameof(text));
                if (!ValidVoices.Contains(normalizedVoice))
                    throw new ArgumentException("voice 必须是 alloy、echo、fable、nova、onyx 或 shimmer。", nameof(voice));
                if (normalizedInstructions.Length > 2000)
                    throw new ArgumentException("表演指令不能超过 2,000 个字符。", nameof(deliveryInstructions));
                if (speed is < 0.25 or > 4)
                    throw new ArgumentException("speed 必须在 0.25 到 4.0 之间。", nameof(speed));
                if (!ValidFormats.Contains(normalizedFormat))
                    throw new ArgumentException("responseFormat 必须是 mp3、wav、opus、aac、flac 或 pcm。", nameof(responseFormat));
                if (normalizedName.Length is 0 or > 160)
                    throw new ArgumentException("资源名称不能为空且不能超过 160 个字符。", nameof(resourceName));

                await context.WriteEventAsync(new
                {
                    type = "process",
                    stage = "tool.started",
                    message = $"Agent 正在使用 {speechGenerator.Deployment} 生成配音（{normalizedVoice}）"
                }, cancellationToken);

                GeneratedSpeech generatedSpeech;
                try
                {
                    generatedSpeech = await speechGenerator.GenerateAsync(
                        normalizedText,
                        normalizedVoice,
                        normalizedInstructions,
                        normalizedFormat,
                        speed,
                        cancellationToken);
                }
                catch (Exception exception)
                {
                    await context.WriteEventAsync(new
                    {
                        type = "process",
                        stage = "tool.failed",
                        message = $"Azure 配音生成失败：{exception.Message}"
                    }, CancellationToken.None);
                    throw;
                }

                var audioAsset = await AudioAssetWriter.SaveAsync(
                    assetWriter,
                    context.ProjectId,
                    normalizedName,
                    generatedSpeech,
                    new SpeechGenerationMetadata(
                        1,
                        "text-to-speech",
                        "azure-openai",
                        generatedSpeech.Deployment,
                        normalizedText,
                        new(
                            generatedSpeech.Voice,
                            normalizedInstructions,
                            generatedSpeech.InstructionsApplied,
                            speed,
                            generatedSpeech.ResponseFormat,
                            speechGenerator.ApiVersion)),
                    cancellationToken);
                var versionCount = await assetReader.CountVersionsAsync(
                    context.ProjectId,
                    audioAsset.ResourceId,
                    cancellationToken);
                if (context.RevisedAssets.All(asset => asset.Id != audioAsset.Id))
                {
                    context.RevisedAssets.Add(audioAsset);
                }
                await context.WriteEventAsync(new
                {
                    type = "process",
                    stage = "tool.completed",
                    message = $"Agent 已生成配音：{audioAsset.Name} v{audioAsset.Version}",
                    data = new
                    {
                        asset = AssetResponse.FromAsset(audioAsset, versionCount),
                        deployment = generatedSpeech.Deployment,
                        voice = generatedSpeech.Voice,
                        speechText = normalizedText,
                        deliveryInstructions = normalizedInstructions,
                        instructionsApplied = generatedSpeech.InstructionsApplied
                    }
                }, cancellationToken);
                return JsonSerializer.Serialize(new
                {
                    asset = AssetResponse.FromAsset(audioAsset, versionCount),
                    speechText = normalizedText,
                    deliveryInstructions = normalizedInstructions
                }, context.JsonOptions);
            }
            finally
            {
                context.ResourceLock.Release();
            }
        }),
        name: Name,
        description: "使用 Azure AI Foundry 的语音部署把文本生成配音并立即保存为音频素材。text 是实际朗读原文；voice 可选 alloy、echo、fable、nova、onyx、shimmer；deliveryInstructions 写明语言、角色、情绪、节奏、重音和停顿，旧版 tts 部署不支持时会保留记录但不提交给模型；speed 范围 0.25-4.0，通常使用 1.0；responseFormat 推荐 mp3，也可用 wav、opus、aac、flac、pcm；resourceName 使用可识别的角色/场次/用途名称。批量配音必须逐条串行生成。",
        serializerOptions: context.JsonOptions);
}