using AlexDirectorConsole.Api.Models;

namespace AlexDirectorConsole.Api.Contracts;

public sealed record UpdateGlobalFoundryConfigurationRequest(
    string OpenAiEndpoint,
    string OpenAiDeployment,
    string? OpenAiApiKey,
    bool ClearOpenAiApiKey,
    string ImageEndpoint,
    string ImageDeployment,
    string ImageApiVersion,
    string ImageQuality,
    string? ImageApiKey,
    bool ClearImageApiKey);

public sealed record GlobalFoundryConfigurationResponse(
    string OpenAiEndpoint,
    string OpenAiDeployment,
    bool OpenAiApiKeyConfigured,
    string ImageEndpoint,
    string ImageDeployment,
    string ImageApiVersion,
    string ImageQuality,
    bool ImageApiKeyConfigured,
    DateTimeOffset UpdatedAtUtc)
{
    public static GlobalFoundryConfigurationResponse FromConfiguration(GlobalFoundryConfiguration configuration) => new(
        configuration.OpenAiEndpoint,
        configuration.OpenAiDeployment,
        !string.IsNullOrWhiteSpace(configuration.ProtectedOpenAiApiKey),
        configuration.ImageEndpoint,
        configuration.ImageDeployment,
        configuration.ImageApiVersion,
        configuration.ImageQuality,
        !string.IsNullOrWhiteSpace(configuration.ProtectedImageApiKey),
        configuration.UpdatedAtUtc);
}