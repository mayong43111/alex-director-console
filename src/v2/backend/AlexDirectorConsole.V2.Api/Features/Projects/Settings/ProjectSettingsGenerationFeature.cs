using System.Net.Http.Json;
using System.Text.Json;
using AlexDirectorConsole.V2.Api.Features.Projects.Generation;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Settings;

public sealed record ProjectCoverView(
    Guid AssetId,
    int Version,
    string ContentType,
    string ContentUrl,
    DateTimeOffset CreatedAtUtc);

internal static class ProjectCoverQueries
{
    public const string AssetType = "project-cover";

    public static async Task<ProjectCoverView?> GetLatestAsync(
        V2DbContext dbContext,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var asset = await dbContext.Assets
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId && item.Type == AssetType)
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken);
        return asset is null ? null : ToView(asset);
    }

    public static ProjectCoverView ToView(Asset asset) => new(
        asset.Id,
        asset.Version,
        asset.ContentType ?? "image/png",
        $"/api/v2/projects/{asset.ProjectId}/settings/cover/{asset.Id}/content",
        asset.CreatedAtUtc);
}

public sealed record GeneratedProjectCover(
    byte[] Bytes,
    string ContentType,
    string Extension,
    string Deployment,
    string Quality,
    string? RevisedPrompt);

public interface IProjectCoverGenerator
{
    Task<GeneratedProjectCover> GenerateAsync(
        string prompt,
        string size,
        CancellationToken cancellationToken);
}

public sealed class ProjectGenerationConfigurationException(string message)
    : InvalidOperationException(message);

public sealed class AzureFoundryProjectCoverGenerator(
    HttpClient httpClient,
    V2DbContext dbContext,
    IDataProtectionProvider dataProtectionProvider) : IProjectCoverGenerator
{
    private const string ApiVersion = "2025-04-01-preview";
    public async Task<GeneratedProjectCover> GenerateAsync(
        string prompt,
        string size,
        CancellationToken cancellationToken)
    {
        var configuration = await dbContext.FoundryConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (configuration is null)
        {
            throw new ProjectGenerationConfigurationException("请先在系统设置中配置 Azure AI Foundry。");
        }

        var endpoint = string.IsNullOrWhiteSpace(configuration.ImageEndpoint)
            ? configuration.Endpoint
            : configuration.ImageEndpoint;
        var protectedApiKey = string.IsNullOrWhiteSpace(configuration.ProtectedImageApiKey)
            ? configuration.ProtectedApiKey
            : configuration.ProtectedImageApiKey;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _)
            || string.IsNullOrWhiteSpace(protectedApiKey))
        {
            throw new ProjectGenerationConfigurationException("请先配置 gpt-image-2 的 Endpoint 和 API Key。");
        }

        var protector = dataProtectionProvider.CreateProtector("FoundryApiKeys.v1");
        var apiKey = protector.Unprotect(protectedApiKey);
        var baseEndpoint = endpoint.TrimEnd('/');
        if (baseEndpoint.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
        {
            baseEndpoint = baseEndpoint[..^"/openai/v1".Length];
        }
        var deployment = FoundryConfigurationView.RequiredImageDeployment;
        var quality = GptImageOptions.NormalizeQuality(configuration.ImageQuality);
        var requestUri = $"{baseEndpoint}/openai/deployments/{Uri.EscapeDataString(deployment)}/images/generations?api-version={Uri.EscapeDataString(ApiVersion)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Add("api-key", apiKey);
        request.Content = JsonContent.Create(new
        {
            prompt,
            n = 1,
            size,
            quality,
            output_format = "png"
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"gpt-image-2 生成失败（{(int)response.StatusCode}）：{ReadError(responseBody)}");
        }

        using var document = JsonDocument.Parse(responseBody);
        var image = document.RootElement.GetProperty("data")[0];
        var revisedPrompt = image.TryGetProperty("revised_prompt", out var revisedPromptElement)
            ? revisedPromptElement.GetString()
            : null;
        if (image.TryGetProperty("b64_json", out var base64Element)
            && !string.IsNullOrWhiteSpace(base64Element.GetString()))
        {
            return new(
                Convert.FromBase64String(base64Element.GetString()!),
                "image/png",
                ".png",
                deployment,
                quality,
                revisedPrompt);
        }

        if (image.TryGetProperty("url", out var urlElement)
            && Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var imageUri))
        {
            using var imageResponse = await httpClient.GetAsync(imageUri, cancellationToken);
            imageResponse.EnsureSuccessStatusCode();
            return new(
                await imageResponse.Content.ReadAsByteArrayAsync(cancellationToken),
                imageResponse.Content.Headers.ContentType?.MediaType ?? "image/png",
                ".png",
                deployment,
                quality,
                revisedPrompt);
        }

        throw new InvalidOperationException("gpt-image-2 未返回图片内容。");
    }

    private static string ReadError(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            return document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message)
                ? message.GetString() ?? "未知错误"
                : responseBody;
        }
        catch (JsonException)
        {
            return responseBody;
        }
    }
}

public interface IProjectCoverService
{
    Task<ImageGenerationPreviewView> PreviewAsync(
        Guid projectId,
        string? instruction,
        CancellationToken cancellationToken);

    Task<ProjectCoverView> GenerateAsync(
        Guid projectId,
        string? instruction,
        CancellationToken cancellationToken);

    Task<ProjectCoverView> GenerateConfirmedAsync(
        Guid projectId,
        string? instruction,
        string confirmedPrompt,
        CancellationToken cancellationToken);
}

public sealed record ProjectCoverPreviewRequest(string? Instruction);

public sealed record ProjectCoverGenerateRequest(string? Instruction, string? ConfirmedPrompt);

public sealed class ProjectCoverService(
    V2DbContext dbContext,
    IProjectCoverGenerator generator,
    TimeProvider timeProvider) : IProjectCoverService
{
    public async Task<ImageGenerationPreviewView> PreviewAsync(
        Guid projectId,
        string? instruction,
        CancellationToken cancellationToken)
    {
        var (_, settingsAsset, settings, prompt, modelSize) = await PrepareAsync(
            projectId,
            instruction,
            cancellationToken);
        var configuration = await dbContext.FoundryConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        return new(
            "generate-project-cover",
            prompt,
            new(
                FoundryConfigurationView.RequiredImageDeployment,
                GptImageOptions.NormalizeQuality(configuration?.ImageQuality ?? "medium"),
                modelSize,
                "png",
                settings.OutputWidth,
                settings.OutputHeight),
            [GenerationProvenance.Reference(settingsAsset, "uses-settings")]);
    }

    public Task<ProjectCoverView> GenerateAsync(
        Guid projectId,
        string? instruction,
        CancellationToken cancellationToken) => GenerateCoreAsync(
            projectId,
            instruction,
            null,
            cancellationToken);

    public Task<ProjectCoverView> GenerateConfirmedAsync(
        Guid projectId,
        string? instruction,
        string confirmedPrompt,
        CancellationToken cancellationToken) => GenerateCoreAsync(
            projectId,
            instruction,
            confirmedPrompt,
            cancellationToken);

    private async Task<ProjectCoverView> GenerateCoreAsync(
        Guid projectId,
        string? instruction,
        string? confirmedPrompt,
        CancellationToken cancellationToken)
    {
        var (project, settingsAsset, settings, prompt, modelSize) = await PrepareAsync(
            projectId,
            instruction,
            cancellationToken);
        if (confirmedPrompt is not null
            && !string.Equals(confirmedPrompt, prompt, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("项目设定或生成意见已变化，请重新预览并确认提示词。");
        }
        var generated = await generator.GenerateAsync(
            prompt,
            modelSize,
            cancellationToken);
        if (generated.Bytes.Length == 0)
        {
            throw new InvalidOperationException("图片模型返回了空文件。");
        }
        var output = ProjectImageOutputProcessor.FitToProjectWhenNeeded(
            generated.Bytes,
            settings.OutputWidth,
            settings.OutputHeight);

        var previous = await dbContext.Assets
            .Where(item => item.ProjectId == projectId && item.Type == ProjectCoverQueries.AssetType)
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken);
        var resourceId = previous?.ResourceId ?? Guid.NewGuid();
        var version = (previous?.Version ?? 0) + 1;
        var number = previous?.Number
            ?? (await dbContext.Assets
                .Where(item => item.ProjectId == projectId)
                .Select(item => (int?)item.Number)
                .MaxAsync(cancellationToken) ?? 0) + 1;
        var now = timeProvider.GetUtcNow();
        var asset = new Asset
        {
            ProjectId = projectId,
            ResourceId = resourceId,
            Version = version,
            Number = number,
            Type = ProjectCoverQueries.AssetType,
            Name = "项目概念封面",
            BlobKey = $"project-covers/{projectId:N}/{resourceId:N}/v{version}{generated.Extension}",
            BlobContent = output.Bytes,
            FileName = $"{project.Name}-概念封面-v{version}{generated.Extension}",
            ContentType = "image/png",
            SizeBytes = output.Bytes.LongLength,
            GenerationMetadataJson = JsonSerializer.Serialize(new
            {
                operation = "generate-project-cover",
                settingsAssetId = settingsAsset.Id,
                deployment = generated.Deployment,
                quality = generated.Quality,
                instruction,
                prompt,
                parameters = new
                {
                    deployment = generated.Deployment,
                    quality = generated.Quality,
                    size = modelSize,
                    outputFormat = "png",
                    outputWidth = settings.OutputWidth,
                    outputHeight = settings.OutputHeight
                },
                references = new[]
                {
                    GenerationProvenance.Reference(settingsAsset, "uses-settings")
                },
                projectStyle = new
                {
                    settings.VisualStyle,
                    settings.ArtDirection,
                    settings.CharacterDesign,
                    settings.ColorPalette,
                    settings.CameraLanguage,
                    settings.ImagePromptPrefix
                },
                modelSize,
                sourceWidth = output.SourceWidth,
                sourceHeight = output.SourceHeight,
                outputWidth = output.Width,
                outputHeight = output.Height,
                revisedPrompt = generated.RevisedPrompt
            }, ProjectSettingsDefaults.JsonOptions),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.Assets.Add(asset);
        dbContext.AssetDependencies.Add(new AssetDependency
        {
            ProjectId = projectId,
            ConsumerAssetId = asset.Id,
            SourceAssetId = settingsAsset.Id,
            Role = "uses-settings",
            IsRequired = true,
            CreatedAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return ProjectCoverQueries.ToView(asset);
    }

    private async Task<(Project Project, Asset SettingsAsset, ProjectSettingsDocument Settings, string Prompt, string ModelSize)> PrepareAsync(
        Guid projectId,
        string? instruction,
        CancellationToken cancellationToken)
    {
        instruction = string.IsNullOrWhiteSpace(instruction) ? null : instruction.Trim();
        if (instruction?.Length > 1000)
        {
            throw new InvalidOperationException("封面生成意见不能超过 1000 个字符。");
        }
        var project = await dbContext.Projects
            .SingleOrDefaultAsync(item => item.Id == projectId, cancellationToken)
            ?? throw new KeyNotFoundException("项目不存在。");
        if (project.CurrentCreativeSettingsId is null)
        {
            throw new InvalidOperationException("请先保存项目设定，再生成概念封面。");
        }
        var settingsAsset = await dbContext.Assets.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == project.CurrentCreativeSettingsId
                && item.ProjectId == projectId
                && item.Type == ProjectSettingsDefaults.AssetType,
            cancellationToken)
            ?? throw new InvalidOperationException("当前项目设定资产不存在。");
        var settings = JsonSerializer.Deserialize<ProjectSettingsDocument>(
            settingsAsset.DocumentJson ?? "{}",
            ProjectSettingsDefaults.JsonOptions)
            ?? throw new InvalidOperationException("当前项目设定无法读取。");
        var prompt = BuildPrompt(settings, instruction);
        var modelSize = ProjectImageOutputProcessor.ModelSizeFor(
            settings.OutputWidth,
            settings.OutputHeight,
            settings.AspectRatio);
        return (project, settingsAsset, settings, prompt, modelSize);
    }

    private static string BuildPrompt(ProjectSettingsDocument settings, string? instruction) => $$"""
        Create a polished concept cover image for the project "{{settings.ProjectName}}".
        Story: {{settings.Description}}
        Visual style: {{settings.VisualStyle}}
        Art direction: {{settings.ArtDirection}}
        Character rules: {{settings.CharacterDesign}}
        Color strategy: {{settings.ColorPalette}}
        Camera language: {{settings.CameraLanguage}}
        Project image constraints: {{settings.ImagePromptPrefix}}
        Director revision request: {{instruction ?? "No additional revision request."}}
        Target composition: {{settings.AspectRatio}}, cinematic key art, clear focal hierarchy, production-ready.
        Do not render titles, captions, logos, watermarks, UI, borders, or readable text.
        """;
}

public sealed record ProjectSettingsAssistRequest(
    string? Field,
    string? CurrentValue,
    string? Instruction,
    JsonElement Context);

public sealed record ProjectSettingsAssistView(
    string Field,
    string Value,
    string Model,
    string Runtime);

public interface IProjectSettingsAssistant
{
    Task<ProjectSettingsAssistView> WriteAsync(
        ProjectSettingsAssistRequest request,
        CancellationToken cancellationToken);
}

#pragma warning disable MAAI001
public sealed class MafProjectSettingsAssistant(
    V2DbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    ILoggerFactory loggerFactory) : IProjectSettingsAssistant
{
    private static readonly IReadOnlyDictionary<string, (string Label, int MaxLength)> Fields =
        new Dictionary<string, (string, int)>(StringComparer.Ordinal)
        {
            ["visualStyle"] = ("视觉风格", 200),
            ["protagonistSpecies"] = ("主角物种", 200),
            ["artDirection"] = ("美术方向", 2000),
            ["characterDesign"] = ("角色造型硬约束", 1000),
            ["colorPalette"] = ("色彩策略", 1000),
            ["cameraLanguage"] = ("摄影语言", 2000),
            ["soundStrategy"] = ("声音策略", 2000),
            ["imagePromptPrefix"] = ("图像生成约束", 4000)
        };

    public async Task<ProjectSettingsAssistView> WriteAsync(
        ProjectSettingsAssistRequest request,
        CancellationToken cancellationToken)
    {
        var field = request.Field?.Trim() ?? string.Empty;
        if (!Fields.TryGetValue(field, out var fieldDefinition))
        {
            throw new ArgumentException("该字段不支持 AI 帮写。", nameof(request));
        }

        var configuration = await dbContext.FoundryConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (configuration is null
            || string.IsNullOrWhiteSpace(configuration.Endpoint)
            || string.IsNullOrWhiteSpace(configuration.ProtectedApiKey))
        {
            throw new ProjectGenerationConfigurationException("请先在系统设置中配置 GPT-5.4。");
        }

        var protector = dataProtectionProvider.CreateProtector("FoundryApiKeys.v1");
        var apiKey = protector.Unprotect(configuration.ProtectedApiKey);
        var agent = AzureFoundryChatClientFactory
            .Create(configuration.Endpoint, configuration.Deployment, apiKey)
            .AsIChatClient()
            .AsHarnessAgent(
                new HarnessAgentOptions
                {
                    Name = "AlexProjectSettingsWriter",
                    MaxContextWindowTokens = 1_050_000,
                    MaxOutputTokens = 4_096,
                    MaximumIterationsPerRequest = 8,
                    DisableFileMemory = true,
                    DisableWebSearch = true,
                    DisableTodoProvider = true,
                    DisableAgentModeProvider = true,
                    DisableAgentSkillsProvider = true,
                    ChatOptions = new ChatOptions
                    {
                        Instructions = $$"""
                            你是影视项目设定编辑。根据完整项目上下文撰写“{{fieldDefinition.Label}}”。
                            当前内容为空时，从上下文生成可直接用于制作的内容；当前内容非空时，保留原意并提升明确性、一致性和可执行性。
                            不新增上下文无法支持的关键剧情事实。只返回字段正文，不要标题、解释、Markdown 围栏或 JSON。
                            字数不得超过 {{fieldDefinition.MaxLength}} 个字符。
                            """,
                        MaxOutputTokens = 4_096
                    }
                },
                loggerFactory);

        var contextJson = request.Context.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? "{}"
            : request.Context.GetRawText();
        var response = await agent.RunAsync(
            $$"""
            项目上下文：{{contextJson}}
            当前字段内容：{{request.CurrentValue?.Trim() ?? string.Empty}}
            导演补充要求：{{request.Instruction?.Trim() ?? "无"}}
            """,
            cancellationToken: cancellationToken);
        var value = response.Text?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            throw new InvalidOperationException("GPT-5.4 未返回字段内容。");
        }
        if (value.Length > fieldDefinition.MaxLength)
        {
            value = value[..fieldDefinition.MaxLength].TrimEnd();
        }
        return new(field, value, configuration.Deployment, "MAF HarnessAgent");
    }
}
#pragma warning restore MAAI001
