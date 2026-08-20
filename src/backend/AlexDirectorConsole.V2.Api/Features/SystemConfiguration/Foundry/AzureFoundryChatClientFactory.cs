using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Security.Cryptography;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.AspNetCore.DataProtection;

namespace AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;

public static class LlmChatClientFactory
{
    public const string InvalidProtectedApiKeyMessage =
        "已保存的 API Key 无法解密，本机加密密钥可能已更改或丢失。请在“设置 > 服务器连接”中重新输入并保存对应 API Key。";

    public static bool IsConfigured(FoundryConfiguration? configuration)
    {
        if (configuration is null) return false;
        return FoundryConfigurationView.NormalizeLlmProvider(configuration.LlmProvider)
            == FoundryConfigurationView.VllmProvider
            ? Uri.TryCreate(configuration.VllmBaseUrl, UriKind.Absolute, out _)
                && !string.IsNullOrWhiteSpace(configuration.VllmModel)
            : Uri.TryCreate(configuration.Endpoint, UriKind.Absolute, out _)
                && !string.IsNullOrWhiteSpace(configuration.ProtectedApiKey);
    }

    public static string GetModel(FoundryConfiguration configuration) =>
        FoundryConfigurationView.NormalizeLlmProvider(configuration.LlmProvider)
            == FoundryConfigurationView.VllmProvider
            ? configuration.VllmModel
            : configuration.Deployment;

    public static ChatClient Create(
        FoundryConfiguration configuration,
        IDataProtectionProvider dataProtectionProvider)
    {
        var isVllm = FoundryConfigurationView.NormalizeLlmProvider(configuration.LlmProvider)
            == FoundryConfigurationView.VllmProvider;
        var protectedApiKey = isVllm
            ? configuration.ProtectedVllmApiKey
            : configuration.ProtectedApiKey;
        var apiKey = string.IsNullOrWhiteSpace(protectedApiKey)
            ? "local-vllm"
            : UnprotectApiKey(dataProtectionProvider, protectedApiKey);
        return Create(
            isVllm ? configuration.VllmBaseUrl : configuration.Endpoint,
            GetModel(configuration),
            apiKey,
            isVllm);
    }

    public static ChatClient Create(string endpoint, string deployment, string apiKey)
        => Create(endpoint, deployment, apiKey, false);

    public static string UnprotectApiKey(
        IDataProtectionProvider dataProtectionProvider,
        string protectedApiKey)
    {
        try
        {
            return dataProtectionProvider.CreateProtector("FoundryApiKeys.v1")
                .Unprotect(protectedApiKey);
        }
        catch (CryptographicException)
        {
            throw new ProjectGenerationConfigurationException(InvalidProtectedApiKeyMessage);
        }
    }

    private static ChatClient Create(
        string endpoint,
        string deployment,
        string apiKey,
        bool isVllm)
    {
        var baseEndpoint = endpoint.TrimEnd('/');
        var requiredSuffix = isVllm ? "/v1" : "/openai/v1";
        if (!baseEndpoint.EndsWith(requiredSuffix, StringComparison.OrdinalIgnoreCase))
        {
            baseEndpoint += requiredSuffix;
        }

        return new ChatClient(
            deployment,
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(baseEndpoint),
                NetworkTimeout = TimeSpan.FromMinutes(5)
            });
    }
}