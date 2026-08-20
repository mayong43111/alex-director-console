using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;

public sealed record FoundryConfigurationView(
    string Provider,
    string LlmProvider,
    string Endpoint,
    string Deployment,
    bool ApiKeyConfigured,
    string VllmBaseUrl,
    string VllmModel,
    bool VllmApiKeyConfigured,
    string ImageProvider,
    string ImageEndpoint,
    string ImageDeployment,
    string ImageQuality,
    bool ImageApiKeyConfigured,
    bool ImageConfigured,
    DateTimeOffset? UpdatedAtUtc)
{
    public const string ProviderName = "Azure AI Foundry";
    public const string AzureProvider = "azure-foundry";
    public const string VllmProvider = "vllm";
    public const string ComfyUiImageProvider = "comfyui";
    public const string RequiredDeployment = "gpt-5.4";
    public const string RequiredVllmModel = "Qwen 3.8 27B";
    public const string DefaultVllmBaseUrl = "http://127.0.0.1:8000/v1";
    public const string RequiredImageDeployment = "gpt-image-2";
    public const string ComfyUiTextToImageModel = "Krea 2 Turbo";
    public const string ComfyUiImageEditModel = "Qwen Image Edit 2511";

    public static FoundryConfigurationView Empty { get; } = new(
        ProviderName,
        AzureProvider,
        string.Empty,
        RequiredDeployment,
        false,
        DefaultVllmBaseUrl,
        RequiredVllmModel,
        false,
        AzureProvider,
        string.Empty,
        RequiredImageDeployment,
        GptImageOptions.DefaultQuality,
        false,
        false,
        null);

    public static FoundryConfigurationView FromEntity(FoundryConfiguration configuration) => new(
        NormalizeLlmProvider(configuration.LlmProvider) == VllmProvider ? "vLLM" : ProviderName,
        NormalizeLlmProvider(configuration.LlmProvider),
        configuration.Endpoint,
        configuration.Deployment,
        !string.IsNullOrWhiteSpace(configuration.ProtectedApiKey),
        configuration.VllmBaseUrl,
        configuration.VllmModel,
        !string.IsNullOrWhiteSpace(configuration.ProtectedVllmApiKey),
        NormalizeImageProvider(configuration.ImageProvider),
        configuration.ImageEndpoint,
        RequiredImageDeployment,
        GptImageOptions.NormalizeQuality(configuration.ImageQuality),
        !string.IsNullOrWhiteSpace(configuration.ProtectedImageApiKey),
        NormalizeImageProvider(configuration.ImageProvider) == ComfyUiImageProvider
            || (Uri.TryCreate(
                    string.IsNullOrWhiteSpace(configuration.ImageEndpoint)
                        ? configuration.Endpoint
                        : configuration.ImageEndpoint,
                    UriKind.Absolute,
                    out _)
                && (!string.IsNullOrWhiteSpace(configuration.ProtectedImageApiKey)
                    || !string.IsNullOrWhiteSpace(configuration.ProtectedApiKey))),
        configuration.UpdatedAtUtc);

    public static string NormalizeLlmProvider(string? provider) =>
        string.Equals(provider, VllmProvider, StringComparison.OrdinalIgnoreCase)
            ? VllmProvider
            : AzureProvider;

    public static string NormalizeImageProvider(string? provider) =>
        string.Equals(provider, ComfyUiImageProvider, StringComparison.OrdinalIgnoreCase)
            ? ComfyUiImageProvider
            : AzureProvider;

    public static string TextToImageModel(FoundryConfiguration? configuration) =>
        NormalizeImageProvider(configuration?.ImageProvider) == ComfyUiImageProvider
            ? ComfyUiTextToImageModel
            : RequiredImageDeployment;

    public static string ImageEditModel(FoundryConfiguration? configuration) =>
        NormalizeImageProvider(configuration?.ImageProvider) == ComfyUiImageProvider
            ? ComfyUiImageEditModel
            : RequiredImageDeployment;
}

public sealed record GetFoundryConfigurationQuery : IQuery<FoundryConfigurationView>;

public sealed class GetFoundryConfigurationHandler(V2DbContext dbContext)
    : IQueryHandler<GetFoundryConfigurationQuery, FoundryConfigurationView>
{
    public async Task<FoundryConfigurationView> HandleAsync(
        GetFoundryConfigurationQuery query,
        CancellationToken cancellationToken)
    {
        var configuration = await dbContext.FoundryConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        return configuration is null
            ? FoundryConfigurationView.Empty
            : FoundryConfigurationView.FromEntity(configuration);
    }
}

public sealed record UpdateFoundryConfigurationCommand(
    string? LlmProvider,
    string? Endpoint,
    string? ApiKey,
    bool ClearApiKey,
    string? VllmBaseUrl,
    string? VllmModel,
    string? VllmApiKey,
    bool ClearVllmApiKey,
    string? ImageProvider,
    string? ImageEndpoint,
    string? ImageApiKey,
    bool ClearImageApiKey,
    string? ImageQuality) : ICommand<UpdateFoundryConfigurationResult>;

public sealed record UpdateFoundryConfigurationResult(
    FoundryConfigurationView? Configuration,
    Dictionary<string, string[]> Errors)
{
    public bool IsSuccess => Configuration is not null;

    public static UpdateFoundryConfigurationResult Success(FoundryConfigurationView configuration) =>
        new(configuration, []);

    public static UpdateFoundryConfigurationResult Invalid(string field, string message) =>
        new(null, new Dictionary<string, string[]> { [field] = [message] });
}

public sealed class UpdateFoundryConfigurationHandler(
    V2DbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider)
    : ICommandHandler<UpdateFoundryConfigurationCommand, UpdateFoundryConfigurationResult>
{
    public async Task<UpdateFoundryConfigurationResult> HandleAsync(
        UpdateFoundryConfigurationCommand command,
        CancellationToken cancellationToken)
    {
        var llmProvider = FoundryConfigurationView.NormalizeLlmProvider(command.LlmProvider);
        var endpoint = command.Endpoint?.Trim().TrimEnd('/') ?? string.Empty;
        if (llmProvider == FoundryConfigurationView.AzureProvider
            && (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
                || endpointUri.Scheme is not ("http" or "https")))
        {
            return UpdateFoundryConfigurationResult.Invalid(
                "endpoint",
                "请输入有效的 Azure AI Foundry HTTP(S) Endpoint。");
        }

        var vllmBaseUrl = command.VllmBaseUrl?.Trim().TrimEnd('/')
            ?? FoundryConfigurationView.DefaultVllmBaseUrl;
        if (!Uri.TryCreate(vllmBaseUrl, UriKind.Absolute, out var vllmUri)
            || vllmUri.Scheme is not ("http" or "https"))
        {
            return UpdateFoundryConfigurationResult.Invalid(
                "vllmBaseUrl",
                "请输入有效的 vLLM HTTP(S) 地址。");
        }
        var vllmModel = string.IsNullOrWhiteSpace(command.VllmModel)
            ? FoundryConfigurationView.RequiredVllmModel
            : command.VllmModel.Trim();
        var imageProvider = FoundryConfigurationView.NormalizeImageProvider(command.ImageProvider);
        var imageEndpoint = command.ImageEndpoint?.Trim();
        if (imageProvider == FoundryConfigurationView.AzureProvider
            && !string.IsNullOrWhiteSpace(imageEndpoint)
            && (!Uri.TryCreate(imageEndpoint, UriKind.Absolute, out var imageEndpointUri)
                || imageEndpointUri.Scheme is not ("http" or "https")))
        {
            return UpdateFoundryConfigurationResult.Invalid(
                "imageEndpoint",
                "请输入有效的图片模型 HTTP(S) Endpoint，或留空复用语言模型 Endpoint。");
        }
        var imageQuality = command.ImageQuality?.Trim().ToLowerInvariant()
            ?? GptImageOptions.DefaultQuality;
        if (!GptImageOptions.SupportedQualities.Contains(imageQuality))
        {
            return UpdateFoundryConfigurationResult.Invalid(
                "imageQuality",
                "图片默认质量必须是 low、medium 或 high。");
        }

        var configuration = await dbContext.FoundryConfigurations
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (configuration is null)
        {
            configuration = new FoundryConfiguration { Id = 1 };
            dbContext.FoundryConfigurations.Add(configuration);
        }

        configuration.LlmProvider = llmProvider;
        configuration.Endpoint = endpoint;
        configuration.Deployment = FoundryConfigurationView.RequiredDeployment;
        configuration.VllmBaseUrl = vllmBaseUrl;
        configuration.VllmModel = vllmModel;
        configuration.ImageProvider = imageProvider;
        if (command.ImageEndpoint is not null)
        {
            configuration.ImageEndpoint = imageEndpoint?.TrimEnd('/') ?? string.Empty;
        }
        configuration.ImageDeployment = FoundryConfigurationView.RequiredImageDeployment;
        configuration.ImageQuality = imageQuality;
        configuration.UpdatedAtUtc = timeProvider.GetUtcNow();
        var protector = dataProtectionProvider.CreateProtector("FoundryApiKeys.v1");
        if (command.ClearApiKey)
        {
            configuration.ProtectedApiKey = string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(command.ApiKey))
        {
            configuration.ProtectedApiKey = protector.Protect(command.ApiKey.Trim());
        }
        if (command.ClearVllmApiKey)
        {
            configuration.ProtectedVllmApiKey = string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(command.VllmApiKey))
        {
            configuration.ProtectedVllmApiKey = protector.Protect(command.VllmApiKey.Trim());
        }
        if (command.ClearImageApiKey)
        {
            configuration.ProtectedImageApiKey = string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(command.ImageApiKey))
        {
            configuration.ProtectedImageApiKey = protector.Protect(command.ImageApiKey.Trim());
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return UpdateFoundryConfigurationResult.Success(
            FoundryConfigurationView.FromEntity(configuration));
    }
}

public sealed record TestFoundryConnectionCommand : ICommand<TestFoundryConnectionResult>;

public sealed record TestFoundryConnectionResult(
    bool IsSuccess,
    string Message,
    string Deployment,
    bool IsConfigured);

public sealed class TestFoundryConnectionHandler(
    V2DbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    IFoundryConnectionTester connectionTester,
    ILogger<TestFoundryConnectionHandler> logger)
    : ICommandHandler<TestFoundryConnectionCommand, TestFoundryConnectionResult>
{
    public async Task<TestFoundryConnectionResult> HandleAsync(
        TestFoundryConnectionCommand command,
        CancellationToken cancellationToken)
    {
        var configuration = await dbContext.FoundryConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (configuration is null)
        {
            return new(false, "请先保存 Endpoint 和 API Key。", FoundryConfigurationView.RequiredDeployment, false);
        }

        var isVllm = FoundryConfigurationView.NormalizeLlmProvider(configuration.LlmProvider)
            == FoundryConfigurationView.VllmProvider;
        var endpoint = isVllm ? configuration.VllmBaseUrl : configuration.Endpoint;
        var deployment = isVllm ? configuration.VllmModel : configuration.Deployment;
        var protectedApiKey = isVllm
            ? configuration.ProtectedVllmApiKey
            : configuration.ProtectedApiKey;
        if (string.IsNullOrWhiteSpace(endpoint)
            || (!isVllm && string.IsNullOrWhiteSpace(protectedApiKey)))
        {
            return new(false, "请先保存服务地址和所需密钥。", deployment, false);
        }

        try
        {
            var apiKey = string.IsNullOrWhiteSpace(protectedApiKey)
                ? "local-vllm"
                : LlmChatClientFactory.UnprotectApiKey(dataProtectionProvider, protectedApiKey);
            await connectionTester.TestAsync(
                endpoint,
                deployment,
                apiKey,
                cancellationToken);
            return new(
                true,
                isVllm ? $"vLLM 连接成功，{deployment} 已可用。" : "Azure AI Foundry 连接成功。",
                deployment,
                true);
        }
            catch (ProjectGenerationConfigurationException error)
            {
                return new(false, error.Message, deployment, false);
            }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(
                error,
                "LLM connection test failed for provider {Provider} and model {Deployment}.",
                configuration.LlmProvider,
                deployment);
            return new(
                false,
                isVllm
                    ? "连接失败，请检查 vLLM Base URL 和模型标识。"
                    : "连接失败，请检查 Endpoint、部署名和 API Key。",
                deployment,
                true);
        }
    }
}