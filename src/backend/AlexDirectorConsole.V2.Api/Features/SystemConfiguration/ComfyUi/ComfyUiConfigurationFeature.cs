using System.Net.Http.Json;
using System.Text.Json;
using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.SystemConfiguration.ComfyUi;

public sealed record ComfyUiConfigurationView(
    string Provider,
    string ConnectionMode,
    string BaseUrl,
    string WorkflowProfile,
    string TextToImageWorkflow,
    string ImageEditWorkflow,
    int MaxConcurrentJobs,
    bool IsEnabled,
    bool IsConfigured,
    DateTimeOffset? UpdatedAtUtc)
{
    public const string ProviderName = "ComfyUI";
    public const string RequiredConnectionMode = "local-http";
    public const string RequiredWorkflowProfile = "minimax-h3-fl2va-turbo-4step";
    public const string RequiredTextToImageWorkflow = "krea-2-text-to-image";
    public const string RequiredImageEditWorkflow = "qwen-image-edit-2511";
    public const string DefaultBaseUrl = "http://127.0.0.1:8188";

    public static ComfyUiConfigurationView Empty { get; } = new(
        ProviderName,
        RequiredConnectionMode,
        DefaultBaseUrl,
        RequiredWorkflowProfile,
        RequiredTextToImageWorkflow,
        RequiredImageEditWorkflow,
        1,
        false,
        false,
        null);

    public static ComfyUiConfigurationView FromEntity(ComfyUiConfiguration configuration) => new(
        ProviderName,
        RequiredConnectionMode,
        configuration.BaseUrl,
        RequiredWorkflowProfile,
        RequiredTextToImageWorkflow,
        RequiredImageEditWorkflow,
        1,
        configuration.IsEnabled,
        Uri.TryCreate(configuration.BaseUrl, UriKind.Absolute, out _),
        configuration.UpdatedAtUtc);
}

public sealed record GetComfyUiConfigurationQuery : IQuery<ComfyUiConfigurationView>;

public sealed class GetComfyUiConfigurationHandler(V2DbContext dbContext)
    : IQueryHandler<GetComfyUiConfigurationQuery, ComfyUiConfigurationView>
{
    public async Task<ComfyUiConfigurationView> HandleAsync(
        GetComfyUiConfigurationQuery query,
        CancellationToken cancellationToken)
    {
        var configuration = await dbContext.ComfyUiConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        return configuration is null
            ? ComfyUiConfigurationView.Empty
            : ComfyUiConfigurationView.FromEntity(configuration);
    }
}

public sealed record UpdateComfyUiConfigurationCommand(
    string? BaseUrl,
    bool IsEnabled) : ICommand<UpdateComfyUiConfigurationResult>;

public sealed record UpdateComfyUiConfigurationResult(
    ComfyUiConfigurationView? Configuration,
    Dictionary<string, string[]> Errors)
{
    public bool IsSuccess => Configuration is not null;

    public static UpdateComfyUiConfigurationResult Invalid(string field, string message) =>
        new(null, new Dictionary<string, string[]> { [field] = [message] });
}

public sealed class UpdateComfyUiConfigurationHandler(
    V2DbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<UpdateComfyUiConfigurationCommand, UpdateComfyUiConfigurationResult>
{
    public async Task<UpdateComfyUiConfigurationResult> HandleAsync(
        UpdateComfyUiConfigurationCommand command,
        CancellationToken cancellationToken)
    {
        var baseUrl = command.BaseUrl?.Trim().TrimEnd('/') ?? string.Empty;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return UpdateComfyUiConfigurationResult.Invalid(
                "baseUrl",
                "请输入有效的 ComfyUI HTTP(S) 地址。");
        }

        var configuration = await dbContext.ComfyUiConfigurations
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (configuration is null)
        {
            configuration = new ComfyUiConfiguration { Id = 1 };
            dbContext.ComfyUiConfigurations.Add(configuration);
        }

        configuration.ConnectionMode = ComfyUiConfigurationView.RequiredConnectionMode;
        configuration.BaseUrl = baseUrl;
        configuration.WorkflowProfile = ComfyUiConfigurationView.RequiredWorkflowProfile;
        configuration.TextToImageWorkflow = ComfyUiConfigurationView.RequiredTextToImageWorkflow;
        configuration.ImageEditWorkflow = ComfyUiConfigurationView.RequiredImageEditWorkflow;
        configuration.MaxConcurrentJobs = 1;
        configuration.IsEnabled = command.IsEnabled;
        configuration.UpdatedAtUtc = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(ComfyUiConfigurationView.FromEntity(configuration), []);
    }
}

public sealed record ComfyUiCapabilities(
    bool IsSuccess,
    string Message,
    string WorkflowProfile,
    IReadOnlyList<string> RequiredNodes,
    IReadOnlyList<string> MissingNodes,
    IReadOnlyList<string> RequiredModels,
    IReadOnlyList<string> MissingModels);

public interface IComfyUiConnectionTester
{
    Task<ComfyUiCapabilities> TestAsync(string baseUrl, CancellationToken cancellationToken);
}

public sealed class ComfyUiConnectionTester(IHttpClientFactory httpClientFactory)
    : IComfyUiConnectionTester
{
    private static readonly string[] RequiredNodes =
    [
        "LoadImage",
        "UNETLoader",
        "CLIPLoader",
        "VAELoader",
        "MiniMaxH3ImageToVideo",
        "BasicScheduler",
        "KSamplerSelect",
        "SamplerCustomAdvanced",
        "VAEDecode",
        "VAEDecodeAudio",
        "CreateVideo",
        "SaveVideo",
        "LoraLoaderModelOnly",
        "MiniMaxH3SigmaShift"
    ];
    private static readonly string[] RequiredModels =
    [
        "minimax_h3_fl2va_pruned_int8_convrot.safetensors",
        "qwen3vl_32b_minimax_h3_int8_convrot.safetensors",
        "minimax_h3_video_vae_fp16.safetensors",
        "minimax_h3_audio_vae_fp32.safetensors",
        "minimax_h3_fl2v_turbo_4step_v1.0_768p_comfyui_bf16.safetensors",
        "krea2_turbo_fp8_scaled.safetensors",
        "qwen3vl_4b_fp8_scaled.safetensors",
        "qwen_image_edit_2511_bf16.safetensors",
        "qwen_2.5_vl_7b_fp8_scaled.safetensors",
        "qwen_image_vae.safetensors"
    ];

    public async Task<ComfyUiCapabilities> TestAsync(
        string baseUrl,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("ComfyUi");
        var root = new Uri(baseUrl.TrimEnd('/') + "/");
        using var stats = await client.GetAsync(new Uri(root, "system_stats"), cancellationToken);
        stats.EnsureSuccessStatusCode();
        var objectInfo = await client.GetFromJsonAsync<JsonElement>(
            new Uri(root, "object_info"),
            cancellationToken);
        var missing = RequiredNodes
            .Where(node => !objectInfo.TryGetProperty(node, out _))
            .ToArray();
        var objectInfoJson = objectInfo.GetRawText();
        var missingModels = RequiredModels
            .Where(model => !objectInfoJson.Contains(model, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var isReady = missing.Length == 0 && missingModels.Length == 0;
        return new(
            isReady,
            isReady
                ? "ComfyUI 连接成功，Krea 2、Qwen Image Edit 2511 和 MiniMax H3 所需节点与模型已就绪。"
                : $"ComfyUI 已连接，但缺少 {missing.Length} 个节点和 {missingModels.Length} 个模型文件。",
            ComfyUiConfigurationView.RequiredWorkflowProfile,
            RequiredNodes,
            missing,
            RequiredModels,
            missingModels);
    }
}

public sealed record TestComfyUiConnectionCommand : ICommand<ComfyUiCapabilities>;

public sealed class TestComfyUiConnectionHandler(
    V2DbContext dbContext,
    IComfyUiConnectionTester connectionTester,
    ILogger<TestComfyUiConnectionHandler> logger)
    : ICommandHandler<TestComfyUiConnectionCommand, ComfyUiCapabilities>
{
    public async Task<ComfyUiCapabilities> HandleAsync(
        TestComfyUiConnectionCommand command,
        CancellationToken cancellationToken)
    {
        var configuration = await dbContext.ComfyUiConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (configuration is null || !configuration.IsEnabled)
        {
            return new(
                false,
                "请先保存并启用本地 ComfyUI。",
                ComfyUiConfigurationView.RequiredWorkflowProfile,
                [],
                [],
                [],
                []);
        }

        try
        {
            return await connectionTester.TestAsync(configuration.BaseUrl, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "ComfyUI connection test failed for {BaseUrl}.", configuration.BaseUrl);
            return new(
                false,
                "连接失败，请确认本机 ComfyUI 已启动并可访问。",
                configuration.WorkflowProfile,
                [],
                [],
                [],
                []);
        }
    }
}