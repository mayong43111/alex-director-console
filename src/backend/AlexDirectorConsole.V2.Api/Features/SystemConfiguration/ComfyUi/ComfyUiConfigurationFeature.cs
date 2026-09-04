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
    public const string MinimaxVideoWorkflow = "minimax-h3-fl2va-native";
    public const string MinimaxReferenceVideoWorkflow = "minimax-h3-ref2va-native";
    public const string Ltx23VideoWorkflow = "ltx-2.3-av-i2v";
    public const string RequiredWorkflowProfile = MinimaxVideoWorkflow;
    public const string RequiredTextToImageWorkflow = "krea-2-text-to-image";
    public const string QwenImageEditWorkflow = "qwen-image-edit-2511";
    public const string Flux2DevImageEditWorkflow = "flux2-dev-image-edit-kv-cache";
    public const string DefaultImageEditWorkflow = QwenImageEditWorkflow;
    public const string DefaultBaseUrl = "http://127.0.0.1:8188";

    public static ComfyUiConfigurationView Empty { get; } = new(
        ProviderName,
        RequiredConnectionMode,
        DefaultBaseUrl,
        MinimaxVideoWorkflow,
        RequiredTextToImageWorkflow,
        DefaultImageEditWorkflow,
        1,
        false,
        false,
        null);

    public static ComfyUiConfigurationView FromEntity(ComfyUiConfiguration configuration) => new(
        ProviderName,
        RequiredConnectionMode,
        configuration.BaseUrl,
        NormalizeVideoWorkflow(configuration.WorkflowProfile),
        RequiredTextToImageWorkflow,
        NormalizeImageEditWorkflow(configuration.ImageEditWorkflow),
        1,
        configuration.IsEnabled,
        Uri.TryCreate(configuration.BaseUrl, UriKind.Absolute, out _),
        configuration.UpdatedAtUtc);

    public static string NormalizeImageEditWorkflow(string? workflow) => workflow switch
    {
        Flux2DevImageEditWorkflow => Flux2DevImageEditWorkflow,
        _ => QwenImageEditWorkflow
    };

    public static bool IsSupportedImageEditWorkflow(string? workflow) =>
        workflow is QwenImageEditWorkflow or Flux2DevImageEditWorkflow;

    public static string NormalizeVideoWorkflow(string? workflow) => workflow switch
    {
        Ltx23VideoWorkflow => Ltx23VideoWorkflow,
        MinimaxReferenceVideoWorkflow => MinimaxReferenceVideoWorkflow,
        _ => MinimaxVideoWorkflow
    };

    public static bool IsSupportedVideoWorkflow(string? workflow) =>
        workflow is MinimaxVideoWorkflow or MinimaxReferenceVideoWorkflow or Ltx23VideoWorkflow;

    public static string VideoModel(string? workflow) =>
        NormalizeVideoWorkflow(workflow) == Ltx23VideoWorkflow ? "LTX 2.3" : "MiniMax H3";

    public static string ImageEditModel(string? workflow) =>
        NormalizeImageEditWorkflow(workflow) == Flux2DevImageEditWorkflow
            ? "FLUX.2 dev"
            : "Qwen Image Edit 2511";
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
    string? ImageEditWorkflow,
    string? WorkflowProfile,
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
        if (!string.IsNullOrWhiteSpace(command.ImageEditWorkflow)
            && !ComfyUiConfigurationView.IsSupportedImageEditWorkflow(command.ImageEditWorkflow))
        {
            return UpdateComfyUiConfigurationResult.Invalid(
                "imageEditWorkflow",
                "请选择受支持的 ComfyUI 图片编辑工作流。");
        }
        if (!string.IsNullOrWhiteSpace(command.WorkflowProfile)
            && !ComfyUiConfigurationView.IsSupportedVideoWorkflow(command.WorkflowProfile))
        {
            return UpdateComfyUiConfigurationResult.Invalid(
                "workflowProfile",
                "请选择受支持的 ComfyUI 视频工作流。");
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
        configuration.WorkflowProfile = string.IsNullOrWhiteSpace(command.WorkflowProfile)
            ? ComfyUiConfigurationView.NormalizeVideoWorkflow(configuration.WorkflowProfile)
            : command.WorkflowProfile;
        configuration.TextToImageWorkflow = ComfyUiConfigurationView.RequiredTextToImageWorkflow;
        configuration.ImageEditWorkflow = string.IsNullOrWhiteSpace(command.ImageEditWorkflow)
            ? ComfyUiConfigurationView.NormalizeImageEditWorkflow(configuration.ImageEditWorkflow)
            : command.ImageEditWorkflow;
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
    Task<ComfyUiCapabilities> TestAsync(
        string baseUrl,
        string imageEditWorkflow,
        string workflowProfile,
        CancellationToken cancellationToken);
}

public sealed class ComfyUiConnectionTester(IHttpClientFactory httpClientFactory)
    : IComfyUiConnectionTester
{
    private static readonly string[] CommonRequiredNodes =
    [
        "LoadImage",
        "ImageScale",
        "UNETLoader",
        "CLIPLoader",
        "VAELoader",
        "VAEEncode",
        "SaveImage",
        "BasicScheduler",
        "KSamplerSelect",
        "SamplerCustomAdvanced",
        "VAEDecode",
        "CreateVideo",
        "SaveVideo"
    ];
    private static readonly string[] QwenRequiredNodes =
    [
        "FluxKontextImageScale",
        "TextEncodeQwenImageEditPlus",
        "FluxKontextMultiReferenceLatentMethod",
        "ModelSamplingAuraFlow",
        "CFGNorm",
        "KSampler"
    ];
    private static readonly string[] FluxRequiredNodes =
    [
        "ImageScaleToTotalPixels",
        "ReferenceLatent",
        "FluxKVCache",
        "EmptyFlux2LatentImage",
        "Flux2Scheduler",
        "RandomNoise",
        "FluxGuidance",
        "BasicGuider"
    ];
    private static readonly string[] MinimaxVideoRequiredNodes =
    [
        "MiniMaxH3ImageToVideo",
        "VAEDecodeAudio",
        "MiniMaxH3SigmaShift"
    ];
    private static readonly string[] MinimaxReferenceVideoRequiredNodes =
    [
        "MiniMaxH3ReferenceToVideo",
        "VAEDecodeAudio"
    ];
    private static readonly string[] Ltx23VideoRequiredNodes =
    [
        "CheckpointLoaderSimple",
        "LTXAVTextEncoderLoader",
        "LTXVAudioVAELoader",
        "CLIPTextEncode",
        "LTXVConditioning",
        "LTXVImgToVideo",
        "LTXVEmptyLatentAudio",
        "LTXVConcatAVLatent",
        "LTXVSeparateAVLatent",
        "ModelSamplingLTXV",
        "KSampler",
        "LTXVAudioVAEDecode"
    ];
    private static readonly string[] CommonRequiredModels =
    [
        "krea2_turbo_fp8_scaled.safetensors",
        "qwen3vl_4b_fp8_scaled.safetensors"
    ];
    private static readonly string[] QwenRequiredModels =
    [
        "qwen_image_edit_2511_bf16.safetensors",
        "qwen_2.5_vl_7b_fp8_scaled.safetensors",
        "qwen_image_vae.safetensors"
    ];
    private static readonly string[] FluxRequiredModels =
    [
        "flux2_dev_fp8mixed.safetensors",
        "mistral_3_small_flux2_fp8.safetensors",
        "flux2-vae.safetensors"
    ];
    private static readonly string[] MinimaxVideoRequiredModels =
    [
        "minimax_h3_fl2va_pruned_int8_convrot.safetensors",
        "qwen3vl_32b_minimax_h3_int8_convrot.safetensors",
        "minimax_h3_video_vae_fp16.safetensors",
        "minimax_h3_audio_vae_fp32.safetensors"
    ];
    private static readonly string[] MinimaxReferenceVideoRequiredModels =
    [
        "minimax_h3_ref2va_pruned_int8_convrot.safetensors",
        "qwen3vl_32b_minimax_h3_int8_convrot.safetensors",
        "minimax_h3_video_vae_fp16.safetensors",
        "minimax_h3_audio_vae_fp32.safetensors"
    ];
    private static readonly string[] Ltx23VideoRequiredModels =
    [
        "ltx-2.3-22b-dev.safetensors",
        "gemma_3_12B_it_fp4_mixed.safetensors"
    ];

    public async Task<ComfyUiCapabilities> TestAsync(
        string baseUrl,
        string imageEditWorkflow,
        string workflowProfile,
        CancellationToken cancellationToken)
    {
        var workflow = ComfyUiConfigurationView.NormalizeImageEditWorkflow(imageEditWorkflow);
        var videoWorkflow = ComfyUiConfigurationView.NormalizeVideoWorkflow(workflowProfile);
        var requiredNodes = CommonRequiredNodes
            .Concat(workflow == ComfyUiConfigurationView.Flux2DevImageEditWorkflow
                ? FluxRequiredNodes
                : QwenRequiredNodes)
            .Concat(videoWorkflow == ComfyUiConfigurationView.Ltx23VideoWorkflow
                ? Ltx23VideoRequiredNodes
                : videoWorkflow == ComfyUiConfigurationView.MinimaxReferenceVideoWorkflow
                    ? MinimaxReferenceVideoRequiredNodes
                    : MinimaxVideoRequiredNodes)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var requiredModels = CommonRequiredModels
            .Concat(workflow == ComfyUiConfigurationView.Flux2DevImageEditWorkflow
                ? FluxRequiredModels
                : QwenRequiredModels)
            .Concat(videoWorkflow == ComfyUiConfigurationView.Ltx23VideoWorkflow
                ? Ltx23VideoRequiredModels
                : videoWorkflow == ComfyUiConfigurationView.MinimaxReferenceVideoWorkflow
                    ? MinimaxReferenceVideoRequiredModels
                    : MinimaxVideoRequiredModels)
            .ToArray();
        var client = httpClientFactory.CreateClient("ComfyUi");
        var root = new Uri(baseUrl.TrimEnd('/') + "/");
        using var stats = await client.GetAsync(new Uri(root, "system_stats"), cancellationToken);
        stats.EnsureSuccessStatusCode();
        var objectInfo = await client.GetFromJsonAsync<JsonElement>(
            new Uri(root, "object_info"),
            cancellationToken);
        var missing = requiredNodes
            .Where(node => !objectInfo.TryGetProperty(node, out _))
            .ToArray();
        var objectInfoJson = objectInfo.GetRawText();
        var missingModels = requiredModels
            .Where(model => !objectInfoJson.Contains(model, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var isReady = missing.Length == 0 && missingModels.Length == 0;
        return new(
            isReady,
            isReady
                ? $"ComfyUI 连接成功，Krea 2、{ComfyUiConfigurationView.ImageEditModel(workflow)} 和 {ComfyUiConfigurationView.VideoModel(videoWorkflow)} 所需节点与模型已就绪。"
                : $"ComfyUI 已连接，但缺少 {missing.Length} 个节点和 {missingModels.Length} 个模型文件。",
            videoWorkflow,
            requiredNodes,
            missing,
            requiredModels,
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
                ComfyUiConfigurationView.MinimaxVideoWorkflow,
                [],
                [],
                [],
                []);
        }

        try
        {
            return await connectionTester.TestAsync(
                configuration.BaseUrl,
                configuration.ImageEditWorkflow,
                configuration.WorkflowProfile,
                cancellationToken);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(error, "ComfyUI connection test timed out for {BaseUrl}.", configuration.BaseUrl);
            return new(
                false,
                "ComfyUI 能力检测超时，请检查远端服务负载或网络连接。",
                configuration.WorkflowProfile,
                [],
                [],
                [],
                []);
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