using System.Text.Json;
using AlexDirectorConsole.Api.Application.Assets;
using AlexDirectorConsole.Api.Models;
using AlexDirectorConsole.Api.Services;

namespace AlexDirectorConsole.Api.Tools;

internal sealed record SpeechGenerationParameters(
    string Voice,
    string Instructions,
    bool InstructionsApplied,
    double Speed,
    string ResponseFormat,
    string? ApiVersion);

internal sealed record SpeechGenerationMetadata(
    int SchemaVersion,
    string Operation,
    string Provider,
    string Model,
    string Prompt,
    SpeechGenerationParameters Parameters);

internal static class AudioAssetWriter
{
    public static Task<Asset> SaveAsync(
        IAssetWriter assetWriter,
        Guid projectId,
        string resourceName,
        GeneratedSpeech generatedSpeech,
        SpeechGenerationMetadata generationMetadata,
        CancellationToken cancellationToken) =>
        assetWriter.WriteVersionAsync(
            new AssetWriteRequest(
                projectId,
                "media",
                $"{resourceName} · AI 配音",
                resourceName,
                generatedSpeech.Extension,
                generatedSpeech.ContentType,
                generatedSpeech.Bytes,
                AssetVersionTarget.ExactName,
                FileNameFallback: "未命名配音",
                GenerationMetadataJson: JsonSerializer.Serialize(
                    generationMetadata,
                    JsonSerializerOptions.Web)),
            cancellationToken);
}