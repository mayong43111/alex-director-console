using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;

public sealed record FoundryConfigurationView(
    string Provider,
    string Endpoint,
    string Deployment,
    bool ApiKeyConfigured,
    string ImageEndpoint,
    string ImageDeployment,
    bool ImageApiKeyConfigured,
    bool ImageConfigured,
    DateTimeOffset? UpdatedAtUtc)
{
    public const string ProviderName = "Azure AI Foundry";
    public const string RequiredDeployment = "gpt-5.4";
    public const string RequiredImageDeployment = "gpt-image-2";

    public static FoundryConfigurationView Empty { get; } = new(
        ProviderName,
        string.Empty,
        RequiredDeployment,
        false,
        string.Empty,
        RequiredImageDeployment,
        false,
        false,
        null);

    public static FoundryConfigurationView FromEntity(FoundryConfiguration configuration) => new(
        ProviderName,
        configuration.Endpoint,
        configuration.Deployment,
        !string.IsNullOrWhiteSpace(configuration.ProtectedApiKey),
        configuration.ImageEndpoint,
        RequiredImageDeployment,
        !string.IsNullOrWhiteSpace(configuration.ProtectedImageApiKey),
        Uri.TryCreate(
            string.IsNullOrWhiteSpace(configuration.ImageEndpoint)
                ? configuration.Endpoint
                : configuration.ImageEndpoint,
            UriKind.Absolute,
            out _)
        && (!string.IsNullOrWhiteSpace(configuration.ProtectedImageApiKey)
            || !string.IsNullOrWhiteSpace(configuration.ProtectedApiKey)),
        configuration.UpdatedAtUtc);
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
    string? Endpoint,
    string? ApiKey,
    bool ClearApiKey,
    string? ImageEndpoint,
    string? ImageApiKey,
    bool ClearImageApiKey) : ICommand<UpdateFoundryConfigurationResult>;

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
        var endpoint = command.Endpoint?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            || endpointUri.Scheme is not ("http" or "https"))
        {
            return UpdateFoundryConfigurationResult.Invalid(
                "endpoint",
                "请输入有效的 Azure AI Foundry HTTP(S) Endpoint。");
        }
            var imageEndpoint = command.ImageEndpoint?.Trim();
            if (!string.IsNullOrWhiteSpace(imageEndpoint)
                && (!Uri.TryCreate(imageEndpoint, UriKind.Absolute, out var imageEndpointUri)
                || imageEndpointUri.Scheme is not ("http" or "https")))
            {
                return UpdateFoundryConfigurationResult.Invalid(
                "imageEndpoint",
                "请输入有效的图片模型 HTTP(S) Endpoint，或留空复用语言模型 Endpoint。");
            }

        var configuration = await dbContext.FoundryConfigurations
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (configuration is null)
        {
            configuration = new FoundryConfiguration { Id = 1 };
            dbContext.FoundryConfigurations.Add(configuration);
        }

        configuration.Endpoint = endpoint.TrimEnd('/');
        configuration.Deployment = FoundryConfigurationView.RequiredDeployment;
        if (command.ImageEndpoint is not null)
        {
            configuration.ImageEndpoint = imageEndpoint?.TrimEnd('/') ?? string.Empty;
        }
        configuration.ImageDeployment = FoundryConfigurationView.RequiredImageDeployment;
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
        if (configuration is null
            || string.IsNullOrWhiteSpace(configuration.Endpoint)
            || string.IsNullOrWhiteSpace(configuration.ProtectedApiKey))
        {
            return new(false, "请先保存 Endpoint 和 API Key。", FoundryConfigurationView.RequiredDeployment, false);
        }

        var protector = dataProtectionProvider.CreateProtector("FoundryApiKeys.v1");
        var apiKey = protector.Unprotect(configuration.ProtectedApiKey);
        try
        {
            await connectionTester.TestAsync(
                configuration.Endpoint,
                configuration.Deployment,
                apiKey,
                cancellationToken);
            return new(true, "Azure AI Foundry 连接成功。", configuration.Deployment, true);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(
                error,
                "Azure AI Foundry connection test failed for deployment {Deployment}.",
                configuration.Deployment);
            return new(false, "连接失败，请检查 Endpoint、部署名和 API Key。", configuration.Deployment, true);
        }
    }
}