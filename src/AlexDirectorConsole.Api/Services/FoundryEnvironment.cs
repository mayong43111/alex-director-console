using AlexDirectorConsole.Api.Models;
using Microsoft.AspNetCore.DataProtection;

namespace AlexDirectorConsole.Api.Services;

public static class FoundryEnvironment
{
    public static void Apply(GlobalFoundryConfiguration configuration, IDataProtector protector)
    {
        Environment.SetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", NullIfEmpty(configuration.OpenAiEndpoint));
        Environment.SetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT", configuration.OpenAiDeployment);
        Environment.SetEnvironmentVariable("AZURE_OPENAI_API_KEY", UnprotectOrNull(configuration.ProtectedOpenAiApiKey, protector));
        Environment.SetEnvironmentVariable("AZURE_IMAGE_ENDPOINT", NullIfEmpty(configuration.ImageEndpoint));
        Environment.SetEnvironmentVariable("AZURE_IMAGE_DEPLOYMENT", configuration.ImageDeployment);
        Environment.SetEnvironmentVariable("AZURE_IMAGE_API_VERSION", configuration.ImageApiVersion);
        Environment.SetEnvironmentVariable("AZURE_IMAGE_QUALITY", configuration.ImageQuality);
        Environment.SetEnvironmentVariable("AZURE_IMAGE_API_KEY", UnprotectOrNull(configuration.ProtectedImageApiKey, protector));
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? UnprotectOrNull(string protectedValue, IDataProtector protector) =>
        string.IsNullOrWhiteSpace(protectedValue) ? null : protector.Unprotect(protectedValue);
}