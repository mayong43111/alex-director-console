using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AlexDirectorConsole.V2.Api.Features.Projects.Assets;
using AlexDirectorConsole.V2.Api.Features.Projects.Generation;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.ComfyUi;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Storyboard;

public sealed record ShotFrameReference(
    byte[] Bytes,
    string ContentType,
    string FileName,
    string SubjectType,
    string SubjectName,
    Guid AssetId,
    Guid ResourceId,
    int Version);

public sealed record GeneratedShotFrame(
    byte[] Bytes,
    string ContentType,
    string Extension,
    string Deployment,
    string Quality,
    string? RevisedPrompt);

public interface IShotFrameGenerator
{
    Task<GeneratedShotFrame> GenerateAsync(
        string prompt,
        string size,
        IReadOnlyList<ShotFrameReference> references,
        CancellationToken cancellationToken);
}

public sealed class AzureFoundryShotFrameGenerator(
    HttpClient httpClient,
    V2DbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    IComfyUiImageClient comfyUiImageClient,
    IComfyUiImageWorkflowProvider comfyUiWorkflowProvider) : IShotFrameGenerator
{
    private const string ApiVersion = "2025-04-01-preview";
    public async Task<GeneratedShotFrame> GenerateAsync(
        string prompt,
        string size,
        IReadOnlyList<ShotFrameReference> references,
        CancellationToken cancellationToken)
    {
        if (references.Count == 0)
        {
            throw new InvalidOperationException("首帧生成必须提供人物和场景参考图。");
        }
        var configuration = await dbContext.FoundryConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (configuration is null)
        {
            throw new ProjectGenerationConfigurationException("请先在系统设置中配置图片生成服务。");
        }
        if (FoundryConfigurationView.NormalizeImageProvider(configuration.ImageProvider)
            == FoundryConfigurationView.ComfyUiImageProvider)
        {
            var comfyUi = await dbContext.ComfyUiConfigurations.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
            if (comfyUi is null || !comfyUi.IsEnabled)
            {
                throw new ProjectGenerationConfigurationException("请先在系统设置中启用本地 ComfyUI。");
            }
            var dimensions = size.Split('x', StringSplitOptions.TrimEntries);
            if (dimensions.Length != 2
                || !int.TryParse(dimensions[0], out var width)
                || !int.TryParse(dimensions[1], out var height))
            {
                throw new ArgumentException("图片尺寸格式无效。", nameof(size));
            }
            var generated = await comfyUiImageClient.GenerateAsync(
                new(
                    comfyUi.BaseUrl,
                    await comfyUiWorkflowProvider.ReadImageEditAsync(cancellationToken),
                    prompt,
                    width,
                    height,
                    references.Select(item => new ComfyUiImageReference(item.Bytes, item.ContentType)).ToArray()),
                cancellationToken);
            return new(
                generated.Bytes,
                generated.ContentType,
                ".png",
                FoundryConfigurationView.ComfyUiImageEditModel,
                GptImageOptions.NormalizeQuality(configuration.ImageQuality),
                null);
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

        var apiKey = LlmChatClientFactory.UnprotectApiKey(dataProtectionProvider, protectedApiKey);
        var baseEndpoint = endpoint.TrimEnd('/');
        if (baseEndpoint.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
        {
            baseEndpoint = baseEndpoint[..^"/openai/v1".Length];
        }
        var deployment = FoundryConfigurationView.RequiredImageDeployment;
        var quality = GptImageOptions.NormalizeQuality(configuration.ImageQuality);
        var requestUri = $"{baseEndpoint}/openai/deployments/{Uri.EscapeDataString(deployment)}/images/edits?api-version={Uri.EscapeDataString(ApiVersion)}";
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(prompt, Encoding.UTF8), "prompt");
        content.Add(new StringContent("1"), "n");
        content.Add(new StringContent(size), "size");
        content.Add(new StringContent(quality), "quality");
        content.Add(new StringContent("png"), "output_format");
        foreach (var reference in references)
        {
            var imageContent = new ByteArrayContent(reference.Bytes);
            imageContent.Headers.ContentType = MediaTypeHeaderValue.Parse(reference.ContentType);
            content.Add(imageContent, "image[]", reference.FileName);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Add("api-key", apiKey);
        request.Content = content;
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"gpt-image-2 首帧生成失败（{(int)response.StatusCode}）：{ReadError(responseBody)}");
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
        throw new InvalidOperationException("gpt-image-2 未返回首帧图片内容。");
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

public interface IShotFrameService
{
    Task<ImageGenerationPreviewView?> PreviewFirstFrameAsync(
        Guid projectId,
        Guid productionEpisodeId,
        Guid shotResourceId,
        CancellationToken cancellationToken);

    Task<ImageGenerationPreviewView?> PreviewFirstFrameAsync(
        Guid projectId,
        Guid productionEpisodeId,
        Guid shotResourceId,
        Guid settingsAssetId,
        IReadOnlyList<Guid> referenceImageAssetIds,
        IReadOnlyList<Guid> propAssetIds,
        CancellationToken cancellationToken);

    Task GenerateFirstFrameAsync(
        Guid runId,
        string confirmedPrompt,
        CancellationToken cancellationToken);

    Task GenerateLastFrameAsync(
        Guid runId,
        CancellationToken cancellationToken);
}

public sealed class ShotFrameService(
    V2DbContext dbContext,
    IShotFrameGenerator generator,
    TimeProvider timeProvider) : IShotFrameService
{
    public const string AssetType = "storyboard-first-frame";

    public async Task<ImageGenerationPreviewView?> PreviewFirstFrameAsync(
        Guid projectId,
        Guid productionEpisodeId,
        Guid shotResourceId,
        CancellationToken cancellationToken)
    {
        var definition = await dbContext.ShotDefinitions.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.ProjectId == projectId
                && candidate.ProductionEpisodeId == productionEpisodeId
                && candidate.ShotResourceId == shotResourceId,
            cancellationToken);
        if (definition is null) return null;
        var project = await dbContext.Projects.AsNoTracking().SingleAsync(
            candidate => candidate.Id == projectId,
            cancellationToken);
        if (project.CurrentCreativeSettingsId is not Guid settingsAssetId)
        {
            throw new InvalidOperationException("开始制作前必须先保存项目设定。");
        }
        var inputs = await ShotProductionPreflight.ResolveAsync(dbContext, definition, cancellationToken);
        return await BuildPreviewAsync(
            projectId,
            definition,
            settingsAssetId,
            inputs.ReferenceImageAssetIds,
            inputs.PropAssetIds,
            cancellationToken);
    }

    public async Task<ImageGenerationPreviewView?> PreviewFirstFrameAsync(
        Guid projectId,
        Guid productionEpisodeId,
        Guid shotResourceId,
        Guid settingsAssetId,
        IReadOnlyList<Guid> referenceImageAssetIds,
        IReadOnlyList<Guid> propAssetIds,
        CancellationToken cancellationToken)
    {
        var definition = await dbContext.ShotDefinitions.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.ProjectId == projectId
                && candidate.ProductionEpisodeId == productionEpisodeId
                && candidate.ShotResourceId == shotResourceId,
            cancellationToken);
        return definition is null
            ? null
            : await BuildPreviewAsync(
                projectId,
                definition,
                settingsAssetId,
                referenceImageAssetIds,
                propAssetIds,
                cancellationToken);
    }

    private async Task<ImageGenerationPreviewView> BuildPreviewAsync(
        Guid projectId,
        ShotDefinition definition,
        Guid settingsAssetId,
        IReadOnlyList<Guid> referenceImageAssetIds,
        IReadOnlyList<Guid> propAssetIds,
        CancellationToken cancellationToken)
    {
        var shotAsset = await dbContext.Assets.AsNoTracking().SingleAsync(
            candidate => candidate.Id == definition.ShotAssetId,
            cancellationToken);
        var settingsAsset = await dbContext.Assets.AsNoTracking().SingleAsync(
            candidate => candidate.Id == settingsAssetId && candidate.ProjectId == projectId,
            cancellationToken);
        var settings = JsonSerializer.Deserialize<ProjectSettingsDocument>(
            settingsAsset.DocumentJson ?? "{}",
            ProjectSettingsDefaults.JsonOptions)
            ?? throw new InvalidOperationException("当前项目设定无法读取。");
        var shot = JsonSerializer.Deserialize<StoryboardShotDocument>(
            shotAsset.DocumentJson ?? "{}",
            StoryboardDefaults.JsonOptions)
            ?? throw new InvalidOperationException("当前镜头内容无法读取。");
        var references = await LoadReferencesAsync(projectId, referenceImageAssetIds, cancellationToken);
        var props = await dbContext.Assets.AsNoTracking()
            .Where(candidate => candidate.ProjectId == projectId && propAssetIds.Contains(candidate.Id))
            .ToListAsync(cancellationToken);
        var prompt = BuildPrompt(settings, shot, references, props);
        var modelSize = ProjectImageOutputProcessor.ModelSizeFor(
            settings.OutputWidth,
            settings.OutputHeight,
            settings.AspectRatio);
        var configuration = await dbContext.FoundryConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == 1, cancellationToken);
        var mode = ShotProductionModes.ForShot(shot);
        return new(
            "generate-storyboard-first-frame",
            prompt,
            new(
                FoundryConfigurationView.ImageEditModel(configuration),
                GptImageOptions.NormalizeQuality(configuration?.ImageQuality ?? "medium"),
                modelSize,
                "png",
                settings.OutputWidth,
                settings.OutputHeight,
                mode,
                definition.DurationSeconds,
                ShotProductionModes.Stages(mode)),
            BuildProvenance(projectId, shotAsset, settingsAsset, references, props));
    }

    public Task GenerateFirstFrameAsync(
        Guid runId,
        string confirmedPrompt,
        CancellationToken cancellationToken) =>
        GenerateFrameAsync(runId, "first-frame", confirmedPrompt, cancellationToken);

    public Task GenerateLastFrameAsync(
        Guid runId,
        CancellationToken cancellationToken) =>
        GenerateFrameAsync(runId, "last-frame", null, cancellationToken);

    private async Task GenerateFrameAsync(
        Guid runId,
        string stage,
        string? confirmedPrompt,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.ProductionRuns.SingleAsync(item => item.Id == runId, cancellationToken);
        var item = await dbContext.ProductionRunItems.SingleAsync(
            candidate => candidate.RunId == runId && candidate.Stage == stage,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        run.Status = "running";
        run.CurrentStage = stage;
        run.StartedAtUtc ??= now;
        run.UpdatedAtUtc = now;
        item.Status = "running";
        item.Attempt += 1;
        item.StartedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var inputAssetIds = JsonSerializer.Deserialize<Guid[]>(
                    item.InputAssetIdsJson,
                    StoryboardDefaults.JsonOptions)
                ?? [];
            var inputAssetIdSet = inputAssetIds.ToHashSet();
            if (!inputAssetIdSet.Contains(item.ShotAssetId)
                || !inputAssetIdSet.Contains(run.CreativeSettingsAssetId))
            {
                throw new InvalidOperationException("生产输入快照缺少镜头或项目设定资产。");
            }
            var inputAssets = await dbContext.Assets.AsNoTracking()
                .Where(candidate => candidate.ProjectId == run.ProjectId
                    && inputAssetIdSet.Contains(candidate.Id))
                .ToListAsync(cancellationToken);
            if (inputAssets.Count != inputAssetIdSet.Count)
            {
                throw new InvalidOperationException("生产输入快照包含不存在或不属于当前项目的资产。");
            }
            var shotAsset = inputAssets.Single(candidate => candidate.Id == item.ShotAssetId);
            var settingsAsset = inputAssets.Single(candidate => candidate.Id == run.CreativeSettingsAssetId);
            var referenceImageAssetIds = inputAssets
                .Where(candidate => candidate.Type == VisualReferenceService.AssetType)
                .Select(candidate => candidate.Id)
                .ToArray();
            var props = inputAssets
                .Where(candidate => candidate.Type == VisualAssetDefaults.AssetType)
                .Where(candidate => VisualAssetMapper.ReadDocument(candidate).Kind == "prop")
                .ToArray();
            var settings = JsonSerializer.Deserialize<ProjectSettingsDocument>(
                settingsAsset.DocumentJson ?? "{}",
                ProjectSettingsDefaults.JsonOptions)
                ?? throw new InvalidOperationException("当前项目设定无法读取。");
            var shot = JsonSerializer.Deserialize<StoryboardShotDocument>(
                shotAsset.DocumentJson ?? "{}",
                StoryboardDefaults.JsonOptions)
                ?? throw new InvalidOperationException("当前镜头内容无法读取。");
            var references = await LoadReferencesAsync(
                run.ProjectId,
                referenceImageAssetIds,
                cancellationToken);
            Asset? firstFrame = null;
            IReadOnlyList<ShotFrameReference> generationReferences = references;
            if (stage == "last-frame")
            {
                var firstFrameAssetId = await dbContext.ProductionRunItems.AsNoTracking()
                    .Where(candidate => candidate.RunId == runId && candidate.Stage == "first-frame")
                    .Select(candidate => candidate.OutputAssetId)
                    .SingleAsync(cancellationToken)
                    ?? throw new InvalidOperationException("生成尾帧前必须先完成首帧。");
                firstFrame = await dbContext.Assets.AsNoTracking().SingleAsync(
                    candidate => candidate.Id == firstFrameAssetId,
                    cancellationToken);
                generationReferences =
                [
                    .. references,
                    new ShotFrameReference(
                        firstFrame.BlobContent ?? throw new InvalidOperationException("首帧文件为空。"),
                        firstFrame.ContentType ?? "image/png",
                        firstFrame.FileName ?? "first-frame.png",
                        "first-frame",
                        "已生成首帧",
                        firstFrame.Id,
                        firstFrame.ResourceId,
                        firstFrame.Version)
                ];
            }
            var prompt = stage == "last-frame"
                ? BuildLastFramePrompt(settings, shot, references, props)
                : BuildPrompt(settings, shot, references, props);
            if (stage == "first-frame" && !string.Equals(confirmedPrompt, prompt, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("镜头、项目设定或参考资产已变化，请重新预览并确认提示词。");
            }
            var modelSize = ProjectImageOutputProcessor.ModelSizeFor(
                settings.OutputWidth,
                settings.OutputHeight,
                settings.AspectRatio);
            var generated = await generator.GenerateAsync(
                prompt,
                modelSize,
                generationReferences,
                cancellationToken);
            if (generated.Bytes.Length == 0)
            {
                throw new InvalidOperationException(stage == "last-frame" ? "图片模型返回了空尾帧文件。" : "图片模型返回了空首帧文件。");
            }
            var projectOutput = ProjectImageOutputProcessor.FitToProjectWhenNeeded(
                generated.Bytes,
                settings.OutputWidth,
                settings.OutputHeight);

            var previousFrames = await (
                from dependency in dbContext.AssetDependencies.AsNoTracking()
                join asset in dbContext.Assets.AsNoTracking() on dependency.ConsumerAssetId equals asset.Id
                where dependency.ProjectId == run.ProjectId
                    && dependency.SourceAssetId == shotAsset.Id
                    && dependency.Role == (stage == "last-frame" ? "last-frame-for-shot" : "frame-for-shot")
                    && asset.Type == AssetType
                select asset)
                .ToListAsync(cancellationToken);
            var previous = previousFrames.OrderByDescending(candidate => candidate.Version).FirstOrDefault();
            var resourceId = previous?.ResourceId ?? Guid.NewGuid();
            var version = (previous?.Version ?? 0) + 1;
            var number = previous?.Number
                ?? (await dbContext.Assets
                    .Where(candidate => candidate.ProjectId == run.ProjectId)
                    .Select(candidate => (int?)candidate.Number)
                    .MaxAsync(cancellationToken) ?? 0) + 1;
            now = timeProvider.GetUtcNow();
            var output = new Asset
            {
                ProjectId = run.ProjectId,
                ProductionEpisodeId = run.ProductionEpisodeId,
                ResourceId = resourceId,
                Version = version,
                Number = number,
                Type = AssetType,
                Name = $"{shotAsset.Name}{(stage == "last-frame" ? "尾帧" : "首帧")}",
                BlobKey = $"storyboard-frames/{run.ProjectId:N}/{item.ShotResourceId:N}/{(stage == "last-frame" ? "last" : "first")}/v{version}{generated.Extension}",
                BlobContent = projectOutput.Bytes,
                FileName = $"{shotAsset.Name}-{(stage == "last-frame" ? "尾帧" : "首帧")}-v{version}{generated.Extension}",
                ContentType = "image/png",
                SizeBytes = projectOutput.Bytes.LongLength,
                GenerationMetadataJson = JsonSerializer.Serialize(new
                {
                    operation = stage == "last-frame" ? "generate-storyboard-last-frame" : "generate-storyboard-first-frame",
                    frameStage = stage,
                    runId,
                    itemId = item.Id,
                    item.ShotResourceId,
                    shotAssetId = shotAsset.Id,
                    settingsAssetId = settingsAsset.Id,
                    referenceImageAssetIds,
                    specialPropAssetIds = props.Select(prop => prop.Id),
                    deployment = generated.Deployment,
                    quality = generated.Quality,
                    prompt,
                    parameters = new
                    {
                        deployment = generated.Deployment,
                        quality = generated.Quality,
                        size = modelSize,
                        outputFormat = "png",
                        outputWidth = settings.OutputWidth,
                        outputHeight = settings.OutputHeight,
                        productionMode = ShotProductionModes.ForShot(shot),
                        shot.FrameStrategyReason,
                        durationSeconds = shot.DurationSeconds,
                        stages = ShotProductionModes.Stages(ShotProductionModes.ForShot(shot))
                    },
                    references = BuildProvenance(run.ProjectId, shotAsset, settingsAsset, references, props)
                        .Concat(firstFrame is null
                            ? []
                            : [GenerationProvenance.Reference(firstFrame, "continues-from-first-frame")]),
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
                    sourceWidth = projectOutput.SourceWidth,
                    sourceHeight = projectOutput.SourceHeight,
                    outputWidth = projectOutput.Width,
                    outputHeight = projectOutput.Height,
                    revisedPrompt = generated.RevisedPrompt
                }, StoryboardDefaults.JsonOptions),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            dbContext.Assets.Add(output);
            var state = await dbContext.ResourceStates.SingleOrDefaultAsync(
                candidate => candidate.ProjectId == run.ProjectId
                    && candidate.ResourceId == resourceId
                    && candidate.ResourceType == AssetType,
                cancellationToken);
            state ??= new ResourceState
            {
                ProjectId = run.ProjectId,
                ResourceId = resourceId,
                ResourceType = AssetType
            };
            if (state.CurrentAssetId == Guid.Empty) dbContext.ResourceStates.Add(state);
            state.CurrentAssetId = output.Id;
            state.LifecycleStatus = "active";
            state.UpdatedAtUtc = now;
            AddDependency(
                run.ProjectId,
                output.Id,
                shotAsset.Id,
                stage == "last-frame" ? "last-frame-for-shot" : "frame-for-shot",
                now);
            AddDependency(run.ProjectId, output.Id, settingsAsset.Id, "uses-settings", now);
            foreach (var referenceId in referenceImageAssetIds)
                AddDependency(run.ProjectId, output.Id, referenceId, "uses-reference", now);
            foreach (var prop in props)
                AddDependency(run.ProjectId, output.Id, prop.Id, "uses-special-prop", now);
            if (firstFrame is not null)
                AddDependency(run.ProjectId, output.Id, firstFrame.Id, "continues-from-first-frame", now);
            item.OutputAssetId = output.Id;
            item.Status = "completed";
            item.CompletedAtUtc = now;
            item.ErrorCode = null;
            item.ErrorDetail = null;
            var waitingItem = await dbContext.ProductionRunItems.FirstOrDefaultAsync(
                candidate => candidate.RunId == runId && candidate.Status == "waiting",
                cancellationToken);
            if (waitingItem is null)
            {
                run.Status = "completed";
                run.FinalAssetId = output.Id;
                run.CompletedAtUtc = now;
            }
            else
            {
                run.Status = "running";
                run.CurrentStage = waitingItem.Stage;
            }
            run.UpdatedAtUtc = now;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            now = timeProvider.GetUtcNow();
            item.Status = "failed";
            item.ErrorCode = error.GetType().Name;
            item.ErrorDetail = error.Message;
            item.CompletedAtUtc = now;
            run.Status = "failed";
            run.LastError = error.Message;
            run.CompletedAtUtc = now;
            run.UpdatedAtUtc = now;
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<ShotFrameReference[]> LoadReferencesAsync(
        Guid projectId,
        IReadOnlyList<Guid> imageAssetIds,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from reference in dbContext.VisualReferences.AsNoTracking()
            join image in dbContext.Assets.AsNoTracking() on reference.ImageAssetId equals image.Id
            join dependency in dbContext.AssetDependencies.AsNoTracking()
                on image.Id equals dependency.ConsumerAssetId
            join subject in dbContext.Assets.AsNoTracking() on dependency.SourceAssetId equals subject.Id
            where reference.ProjectId == projectId
                && imageAssetIds.Contains(image.Id)
                && dependency.ProjectId == projectId
                && dependency.Role == "reference-for"
            select new { Reference = reference, Image = image, Subject = subject })
            .ToListAsync(cancellationToken);
        if (rows.Select(row => row.Image.Id).Distinct().Count() != imageAssetIds.Count)
        {
            throw new InvalidOperationException("生产输入快照中的参考图缺少主体版本引用。");
        }
        return rows.Select(row =>
        {
            var document = VisualAssetMapper.ReadDocument(row.Subject);
            return new ShotFrameReference(
                row.Image.BlobContent ?? throw new InvalidOperationException($"参考图文件为空：{document.Name}"),
                row.Image.ContentType ?? "image/png",
                row.Image.FileName ?? $"{document.Name}.png",
                document.Kind,
                document.Name,
                row.Image.Id,
                row.Image.ResourceId,
                row.Image.Version);
        }).ToArray();
    }

    private static GenerationAssetReferenceView[] BuildProvenance(
        Guid projectId,
        Asset shotAsset,
        Asset settingsAsset,
        IReadOnlyList<ShotFrameReference> references,
        IReadOnlyList<Asset> props) =>
        [
            GenerationProvenance.Reference(shotAsset, "frame-for-shot"),
            GenerationProvenance.Reference(settingsAsset, "uses-settings"),
            .. references.Select(reference => new GenerationAssetReferenceView(
                reference.AssetId,
                reference.ResourceId,
                reference.Version,
                reference.SubjectName,
                VisualReferenceService.AssetType,
                "uses-reference",
                $"/api/v2/projects/{projectId}/visual-assets/references/{reference.AssetId}/content")),
            .. props.Select(prop => GenerationProvenance.Reference(prop, "uses-special-prop"))
        ];

    private void AddDependency(
        Guid projectId,
        Guid consumerAssetId,
        Guid sourceAssetId,
        string role,
        DateTimeOffset now) =>
        dbContext.AssetDependencies.Add(new AssetDependency
        {
            ProjectId = projectId,
            ConsumerAssetId = consumerAssetId,
            SourceAssetId = sourceAssetId,
            Role = role,
            IsRequired = true,
            CreatedAtUtc = now
        });

    private static string BuildPrompt(
        ProjectSettingsDocument settings,
        StoryboardShotDocument shot,
        IReadOnlyList<ShotFrameReference> references,
        IReadOnlyList<Asset> propAssets)
    {
        var props = propAssets.Select(VisualAssetMapper.ReadDocument).ToArray();
        return $$"""
            Create the exact first frame of one cinematic storyboard shot using every supplied character and scene image as a strict visual reference.
            Project: {{settings.ProjectName}}
            Visual style: {{settings.VisualStyle}}
            Art direction: {{settings.ArtDirection}}
            Character consistency rules: {{settings.CharacterDesign}}
            Color strategy: {{settings.ColorPalette}}
            Camera language: {{settings.CameraLanguage}}
            Project image constraints: {{settings.ImagePromptPrefix}}
            Shot: scene {{shot.SceneNumber}}, shot {{shot.ShotNumber}}, {{shot.DurationSeconds}} seconds
            Shot size: {{shot.ShotSize}}
            Camera angle: {{shot.CameraAngle}}
            Camera movement intent: {{shot.CameraMovement}}
            Composition: {{shot.Composition}}
            First-frame visual: {{(string.IsNullOrWhiteSpace(shot.FirstFrameDescription) ? shot.VisualDescription : shot.FirstFrameDescription)}}
            Action starting pose: {{shot.Action}}
            Narrative hooks realized by this shot: {{string.Join("; ", (shot.Hooks ?? []).Select(item => $"{item.Type}: {item.Description}"))}}
            Required character references: {{string.Join("; ", references.Where(item => item.SubjectType == "character").Select(item => item.SubjectName))}}
            Required scene references: {{string.Join("; ", references.Where(item => item.SubjectType == "scene").Select(item => item.SubjectName))}}
            Important recurring complex props only: {{string.Join("; ", props.Select(item => $"{item.Name}: {item.VisualDescription}"))}}
            Match each referenced character's identity, species, face, body, costume, and colors. Match the referenced scene's architecture, layout, materials, lighting logic, and scale.
            Do not add ordinary handheld objects, furniture, set dressing, or any prop not listed as an important recurring complex prop.
            Render one coherent frame, not a collage or model sheet. Do not render titles, captions, speech bubbles, logos, watermarks, UI, borders, or readable text.
            """;
    }

    private static string BuildLastFramePrompt(
        ProjectSettingsDocument settings,
        StoryboardShotDocument shot,
        IReadOnlyList<ShotFrameReference> references,
        IReadOnlyList<Asset> propAssets)
    {
        var props = propAssets.Select(VisualAssetMapper.ReadDocument).ToArray();
        return $$"""
            Create the exact final frame of one cinematic storyboard shot. The supplied first-frame image is the strict continuity anchor; the supplied character and scene sheets are strict identity and environment references.
            Project: {{settings.ProjectName}}
            Visual style: {{settings.VisualStyle}}
            Art direction: {{settings.ArtDirection}}
            Character consistency rules: {{settings.CharacterDesign}}
            Color strategy: {{settings.ColorPalette}}
            Camera language: {{settings.CameraLanguage}}
            Project image constraints: {{settings.ImagePromptPrefix}}
            Shot: scene {{shot.SceneNumber}}, shot {{shot.ShotNumber}}, {{shot.DurationSeconds}} seconds
            Shot size: {{shot.ShotSize}}
            Camera angle: {{shot.CameraAngle}}
            Camera movement intent: {{shot.CameraMovement}}
            First-frame continuity anchor: {{shot.FirstFrameDescription}}
            Required final-frame state: {{shot.LastFrameDescription}}
            Full cut execution: {{shot.CutDescription}}
            Required character references: {{string.Join("; ", references.Where(item => item.SubjectType == "character").Select(item => item.SubjectName))}}
            Required scene references: {{string.Join("; ", references.Where(item => item.SubjectType == "scene").Select(item => item.SubjectName))}}
            Important recurring complex props only: {{string.Join("; ", props.Select(item => $"{item.Name}: {item.VisualDescription}"))}}
            Preserve every identity, costume, material, color, light direction, camera axis, and environment detail from the supplied first frame. Change only the positions, poses, expressions, occlusions, prop states, framing, and focus explicitly required by the final-frame state and cut execution.
            Render one coherent final frame, not a collage or model sheet. Do not render titles, captions, speech bubbles, logos, watermarks, UI, borders, or readable text.
            """;
    }
}

public static class ShotFrameEndpoints
{
    public static IEndpointRouteBuilder MapShotFrameContent(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v2/projects/{projectId:guid}/storyboard/frames/{assetId:guid}/content", async (
            Guid projectId,
            Guid assetId,
            V2DbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var image = await dbContext.Assets.AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == assetId
                    && item.ProjectId == projectId
                    && item.Type == ShotFrameService.AssetType,
                cancellationToken);
            return image?.BlobContent is null
                ? Results.NotFound()
                : Results.File(
                    image.BlobContent,
                    image.ContentType ?? "image/png",
                    image.FileName,
                    enableRangeProcessing: false);
        });
        return app;
    }
}