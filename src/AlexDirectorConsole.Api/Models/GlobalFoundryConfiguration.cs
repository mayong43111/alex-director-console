namespace AlexDirectorConsole.Api.Models;

public sealed class GlobalFoundryConfiguration
{
    public int Id { get; set; } = 1;

    public string OpenAiEndpoint { get; set; } = string.Empty;

    public string OpenAiDeployment { get; set; } = "gpt-5.4";

    public string ProtectedOpenAiApiKey { get; set; } = string.Empty;

    public string ImageEndpoint { get; set; } = string.Empty;

    public string ImageDeployment { get; set; } = "gpt-image-2";

    public string ImageApiVersion { get; set; } = "2025-04-01-preview";

    public string ImageQuality { get; set; } = "medium";

    public string ProtectedImageApiKey { get; set; } = string.Empty;

    public string SpeechEndpoint { get; set; } = string.Empty;

    public string SpeechDeployment { get; set; } = "tts";

    public string SpeechApiVersion { get; set; } = "2025-03-01-preview";

    public string ProtectedSpeechApiKey { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; set; }
}