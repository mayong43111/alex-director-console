using AlexDirectorConsole.V2.Api.Features.Generation;
using System.Text;
using System.Text.Json;
using AlexDirectorConsole.V2.Api.Features.Projects.Generation;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.Projects.Storyboard;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Assets;

public sealed record VisualReferenceImageView(
    Guid AssetId,
    Guid SubjectResourceId,
    string SubjectType,
    string SubjectName,
    int Version,
    string ContentType,
    string ContentUrl,
    string Prompt,
    string? RevisedPrompt,
    DateTimeOffset CreatedAtUtc);

public sealed record VisualReferencePromptView(
    Guid AssetId,
    Guid SubjectResourceId,
    string SubjectType,
    string SubjectName,
    int Version,
    string Prompt,
    string? Instruction,
    bool UseCurrentReference,
    DateTimeOffset CreatedAtUtc);

public sealed record BatchVisualReferenceResult(
    int Generated,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Errors);

public sealed record BatchVisualReferenceProgress(
    int Completed,
    int Total,
    string SubjectName,
    string Outcome);

public sealed record GenerateVisualReferenceRequest(
    string? Instruction,
    bool UseCurrentReference = false);

public sealed record BatchVisualReferenceRequest(string Kind);

internal static class VisualReferenceQueries
{
    public static async Task<IReadOnlyDictionary<Guid, VisualReferenceImageView>> GetLatestBySubjectAsync(
        V2DbContext dbContext,
        Guid projectId,
        IReadOnlyList<Guid> subjectResourceIds,
        CancellationToken cancellationToken)
    {
        if (subjectResourceIds.Count == 0)
            return new Dictionary<Guid, VisualReferenceImageView>();
        var rows = await (
            from reference in dbContext.VisualReferences.AsNoTracking()
            join image in dbContext.Assets.AsNoTracking() on reference.ImageAssetId equals image.Id
            where reference.ProjectId == projectId
                && subjectResourceIds.Contains(reference.SubjectResourceId)
                && reference.Purpose == VisualReferenceService.Purpose
                && image.Type == VisualReferenceService.AssetType
                && image.BlobContent != null
            select new { Reference = reference, Image = image })
            .ToListAsync(cancellationToken);
        var resourceIds = rows.Select(item => item.Image.ResourceId).Distinct().ToArray();
        var currentAssetIds = await dbContext.ResourceStates.AsNoTracking()
            .Where(item => item.ProjectId == projectId
                && item.ResourceType == VisualReferenceService.AssetType
                && resourceIds.Contains(item.ResourceId))
            .Select(item => item.CurrentAssetId)
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(item => item.Reference.SubjectResourceId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var latest = group
                        .OrderByDescending(item => currentAssetIds.Contains(item.Image.Id))
                        .ThenByDescending(item => item.Image.Version)
                        .First();
                    return VisualReferenceService.ToView(
                        latest.Image,
                        latest.Reference.SubjectResourceId,
                        latest.Reference.SubjectType,
                        latest.Image.Name.EndsWith("参考图", StringComparison.Ordinal)
                            ? latest.Image.Name[..^3]
                            : latest.Image.Name);
                });
    }

    public static async Task<IReadOnlyDictionary<Guid, VisualReferencePromptView>> GetLatestPromptsBySubjectAsync(
        V2DbContext dbContext,
        Guid projectId,
        IReadOnlyList<Guid> subjectResourceIds,
        CancellationToken cancellationToken)
    {
        if (subjectResourceIds.Count == 0)
            return new Dictionary<Guid, VisualReferencePromptView>();
        var rows = await (
            from reference in dbContext.VisualReferences.AsNoTracking()
            join prompt in dbContext.Assets.AsNoTracking() on reference.ImageAssetId equals prompt.Id
            join state in dbContext.ResourceStates.AsNoTracking() on prompt.ResourceId equals state.ResourceId
            where reference.ProjectId == projectId
                && subjectResourceIds.Contains(reference.SubjectResourceId)
                && reference.Purpose == VisualReferenceService.PromptPurpose
                && prompt.Type == VisualReferenceService.PromptAssetType
                && state.ProjectId == projectId
                && state.ResourceType == VisualReferenceService.PromptAssetType
                && state.CurrentAssetId == prompt.Id
            orderby prompt.Version descending
            select new { Reference = reference, Prompt = prompt })
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(item => item.Reference.SubjectResourceId)
            .ToDictionary(
                group => group.Key,
                group => VisualReferenceService.ToPromptView(
                    group.First().Prompt,
                    group.Key,
                    group.First().Reference.SubjectType,
                    group.First().Prompt.Name.EndsWith("提示词", StringComparison.Ordinal)
                        ? group.First().Prompt.Name[..^3]
                        : group.First().Prompt.Name));
    }
}

public interface IVisualReferenceService
{
    Task<VisualReferencePromptView> GeneratePromptAsync(
        Guid projectId,
        Guid subjectResourceId,
        string? instruction,
        bool useCurrentReference,
        CancellationToken cancellationToken);

    Task<VisualReferenceImageView> GenerateImageAsync(
        Guid projectId,
        Guid subjectResourceId,
        CancellationToken cancellationToken);

    Task<BatchVisualReferenceResult> GenerateMissingPromptsAsync(
        Guid projectId,
        string kind,
        CancellationToken cancellationToken,
        Func<BatchVisualReferenceProgress, Task>? reportProgress = null);

    Task<BatchVisualReferenceResult> GenerateMissingImagesAsync(
        Guid projectId,
        string kind,
        CancellationToken cancellationToken,
        Func<BatchVisualReferenceProgress, Task>? reportProgress = null);

    Task<VisualReferenceImageView> UploadAsync(
        Guid projectId,
        Guid subjectResourceId,
        string fileName,
        string contentType,
        byte[] bytes,
        CancellationToken cancellationToken);
}

public sealed class VisualReferenceService(
    V2DbContext dbContext,
    IProjectCoverGenerator generator,
    IShotFrameGenerator referenceEditor,
    IVisualReferencePromptWriter promptWriter,
    TimeProvider timeProvider) : IVisualReferenceService
{
    public const string AssetType = "visual-reference-image";
    public const string PromptAssetType = "visual-reference-prompt";
    public const string Purpose = "generation-reference";
    public const string PromptPurpose = "generation-prompt";
    public const long MaxUploadBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> UploadContentTypes =
        ["image/png", "image/jpeg", "image/webp"];

    private sealed record PromptDocument(
        string Prompt,
        string? Instruction,
        bool UseCurrentReference,
        Guid SubjectAssetId,
        Guid SettingsAssetId,
        Guid? BasedOnReferenceAssetId);

    public async Task<VisualReferencePromptView> GeneratePromptAsync(
        Guid projectId,
        Guid subjectResourceId,
        string? instruction,
        bool useCurrentReference,
        CancellationToken cancellationToken)
    {
        instruction = instruction?.Trim();
        if (instruction?.Length > 2000)
            throw new InvalidOperationException("本轮修改意见不能超过 2000 个字符。");
        var subject = await GetSubjectAsync(projectId, subjectResourceId, cancellationToken);
        var document = VisualAssetMapper.ReadDocument(subject);
        if (document.Kind is not ("character" or "scene" or "prop"))
            throw new InvalidOperationException("不支持该类型的设定图生成。");

        var project = await dbContext.Projects.AsNoTracking()
            .SingleAsync(item => item.Id == projectId, cancellationToken);
        if (project.CurrentCreativeSettingsId is not Guid settingsAssetId)
        {
            throw new InvalidOperationException("请先保存项目设定，再生成参考图。");
        }
        var settingsAsset = await dbContext.Assets.AsNoTracking()
            .SingleAsync(item => item.Id == settingsAssetId, cancellationToken);
        var settings = JsonSerializer.Deserialize<ProjectSettingsDocument>(
            settingsAsset.DocumentJson ?? "{}",
            ProjectSettingsDefaults.JsonOptions)
            ?? throw new InvalidOperationException("当前项目设定无法读取。");

        var currentReference = useCurrentReference
            ? (await GetCurrentReferenceAssetAsync(
                projectId,
                subjectResourceId,
                AssetType,
                Purpose,
                cancellationToken)
                ?? throw new InvalidOperationException("当前资产还没有可用于修改的参考图。"))
            : null;
        var previous = await GetLatestReferenceAssetAsync(
            projectId,
            subjectResourceId,
            PromptAssetType,
            PromptPurpose,
            cancellationToken);
        var configuration = await dbContext.FoundryConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        var targetImageModel = useCurrentReference
            ? FoundryConfigurationView.ImageEditModel(configuration)
            : FoundryConfigurationView.TextToImageModel(configuration);
        var promptResult = await promptWriter.WriteAsync(
            new(
                JsonSerializer.SerializeToElement(new
                {
                    project = new
                    {
                        settings.ProjectName,
                        settings.VisualStyle,
                        settings.ArtDirection,
                        settings.CharacterDesign,
                        settings.ColorPalette,
                        settings.ImagePromptPrefix
                    },
                    subject = new
                    {
                        document.Kind,
                        document.Name,
                        document.Summary,
                        document.VisualDescription,
                        document.MustKeep,
                        document.Avoid
                    },
                    requiredLayout = LayoutFor(document.Kind),
                    outputRules = "1024x1024 square production design sheet; pure solid white #FFFFFF outer canvas and gutters; no text, labels, marks, borders, logos, watermarks, or UI"
                }, VisualAssetDefaults.JsonOptions),
                document.Kind,
                targetImageModel,
                "1024x1024",
                useCurrentReference,
                previous is null ? null : ReadPromptDocument(previous).Prompt,
                instruction),
            cancellationToken);
        var prompt = promptResult.Prompt;
        var resourceId = previous?.ResourceId ?? Guid.NewGuid();
        var version = (previous?.Version ?? 0) + 1;
        var number = previous?.Number
            ?? (await dbContext.Assets
                .Where(item => item.ProjectId == projectId)
                .Select(item => (int?)item.Number)
                .MaxAsync(cancellationToken) ?? 0) + 1;
        var now = timeProvider.GetUtcNow();
        var promptDocument = new PromptDocument(
            prompt,
            instruction,
            useCurrentReference,
            subject.Id,
            settingsAsset.Id,
            currentReference?.Id);
        var documentJson = JsonSerializer.Serialize(promptDocument, VisualAssetDefaults.JsonOptions);
        var promptAsset = new Asset
        {
            ProjectId = projectId,
            ResourceId = resourceId,
            Version = version,
            Number = number,
            Type = PromptAssetType,
            Name = $"{document.Name}提示词",
            DocumentJson = documentJson,
            ContentType = "application/json",
            SizeBytes = Encoding.UTF8.GetByteCount(documentJson),
            GenerationMetadataJson = JsonSerializer.Serialize(new
            {
                operation = "generate-visual-reference-prompt",
                source = "model-aware-prompt-agent",
                subjectAssetId = subject.Id,
                subjectResourceId,
                subjectType = document.Kind,
                settingsAssetId,
                prompt,
                instruction,
                useCurrentReference,
                targetImageModel,
                promptWriterModel = promptResult.Model,
                promptWriterRuntime = promptResult.Runtime,
                basedOnReferenceAssetId = currentReference?.Id,
                references = new[]
                    {
                        GenerationProvenance.Reference(subject, "reference-for"),
                        GenerationProvenance.Reference(settingsAsset, "uses-settings")
                    }
                    .Concat(currentReference is null
                        ? []
                        : [GenerationProvenance.Reference(currentReference, "uses-current-reference")]),
                projectStyle = new
                {
                    settings.VisualStyle,
                    settings.ArtDirection,
                    settings.CharacterDesign,
                    settings.ColorPalette,
                    settings.ImagePromptPrefix
                }
            }, VisualAssetDefaults.JsonOptions),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.Assets.Add(promptAsset);
        var referenceState = await dbContext.ResourceStates.SingleOrDefaultAsync(
            item => item.ProjectId == projectId
                && item.ResourceId == resourceId
                && item.ResourceType == PromptAssetType,
            cancellationToken);
        referenceState ??= new ResourceState
        {
            ProjectId = projectId,
            ResourceId = resourceId,
            ResourceType = PromptAssetType
        };
        if (referenceState.CurrentAssetId == Guid.Empty) dbContext.ResourceStates.Add(referenceState);
        referenceState.CurrentAssetId = promptAsset.Id;
        referenceState.LifecycleStatus = "active";
        referenceState.UpdatedAtUtc = now;
        dbContext.VisualReferences.Add(new VisualReference
        {
            ProjectId = projectId,
            ImageAssetId = promptAsset.Id,
            SubjectResourceId = subjectResourceId,
            SubjectType = document.Kind,
            Purpose = PromptPurpose,
            Source = "model-aware-prompt-agent",
            ReviewStatus = "active",
            InheritsFromAssetId = previous?.Id,
            CreatedAtUtc = now
        });
        dbContext.AssetDependencies.Add(new AssetDependency
        {
            ProjectId = projectId,
            ConsumerAssetId = promptAsset.Id,
            SourceAssetId = subject.Id,
            Role = "reference-for",
            IsRequired = true,
            CreatedAtUtc = now
        });
        dbContext.AssetDependencies.Add(new AssetDependency
        {
            ProjectId = projectId,
            ConsumerAssetId = promptAsset.Id,
            SourceAssetId = settingsAsset.Id,
            Role = "uses-settings",
            IsRequired = true,
            CreatedAtUtc = now
        });
        if (currentReference is not null)
        {
            dbContext.AssetDependencies.Add(new AssetDependency
            {
                ProjectId = projectId,
                ConsumerAssetId = promptAsset.Id,
                SourceAssetId = currentReference.Id,
                Role = "uses-current-reference",
                IsRequired = true,
                CreatedAtUtc = now
            });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToPromptView(promptAsset, subjectResourceId, document.Kind, document.Name);
    }

    public async Task<VisualReferenceImageView> GenerateImageAsync(
        Guid projectId,
        Guid subjectResourceId,
        CancellationToken cancellationToken)
    {
        var subject = await GetSubjectAsync(projectId, subjectResourceId, cancellationToken);
        var document = VisualAssetMapper.ReadDocument(subject);
        var promptAsset = await GetCurrentReferenceAssetAsync(
            projectId,
            subjectResourceId,
            PromptAssetType,
            PromptPurpose,
            cancellationToken);
        PromptDocument? promptDocument = promptAsset is null ? null : ReadPromptDocument(promptAsset);
        var previous = await GetLatestReferenceAssetAsync(
            projectId,
            subjectResourceId,
            AssetType,
            Purpose,
            cancellationToken);
        var prompt = promptDocument?.Prompt;
        if (string.IsNullOrWhiteSpace(prompt) && previous is not null)
            prompt = ReadPrompts(previous.GenerationMetadataJson).Prompt;
        if (string.IsNullOrWhiteSpace(prompt))
            throw new InvalidOperationException("请先生成提示词，再生成设定图。");

        const int referenceSize = 1024;
        const string modelSize = "1024x1024";
        Asset? currentReference = null;
        GeneratedProjectCover generated;
        if (promptDocument?.UseCurrentReference == true)
        {
            currentReference = previous?.BlobContent is not null
                ? previous
                : throw new InvalidOperationException("当前资产还没有可用于修改的参考图。");
            var edited = await referenceEditor.GenerateAsync(
                prompt,
                modelSize,
                [new ShotFrameReference(
                    currentReference.BlobContent!,
                    currentReference.ContentType ?? "image/png",
                    currentReference.FileName ?? "current-reference.png",
                    document.Kind,
                    document.Name,
                    currentReference.Id,
                    currentReference.ResourceId,
                    currentReference.Version)],
                cancellationToken);
            generated = new(
                edited.Bytes,
                edited.ContentType,
                edited.Extension,
                edited.Deployment,
                edited.Quality,
                edited.RevisedPrompt);
        }
        else
        {
            generated = await generator.GenerateAsync(prompt, modelSize, cancellationToken);
        }
        if (generated.Bytes.Length == 0)
            throw new InvalidOperationException("图片模型返回了空文件。");
        var output = ProjectImageOutputProcessor.FitToProjectWhenNeeded(
            generated.Bytes,
            referenceSize,
            referenceSize);
        var resourceId = previous?.ResourceId ?? Guid.NewGuid();
        var version = (previous?.Version ?? 0) + 1;
        var number = previous?.Number
            ?? (await dbContext.Assets.Where(item => item.ProjectId == projectId)
                .Select(item => (int?)item.Number).MaxAsync(cancellationToken) ?? 0) + 1;
        var now = timeProvider.GetUtcNow();
        var image = new Asset
        {
            ProjectId = projectId,
            ResourceId = resourceId,
            Version = version,
            Number = number,
            Type = AssetType,
            Name = $"{document.Name}参考图",
            BlobKey = $"visual-references/{projectId:N}/{subjectResourceId:N}/v{version}{generated.Extension}",
            BlobContent = output.Bytes,
            FileName = $"{document.Name}-参考图-v{version}{generated.Extension}",
            ContentType = "image/png",
            SizeBytes = output.Bytes.LongLength,
            GenerationMetadataJson = JsonSerializer.Serialize(new
            {
                operation = "generate-visual-reference-image",
                subjectAssetId = subject.Id,
                subjectResourceId,
                subjectType = document.Kind,
                promptAssetId = promptAsset?.Id,
                prompt,
                instruction = promptDocument?.Instruction,
                useCurrentReference = promptDocument?.UseCurrentReference ?? false,
                basedOnReferenceAssetId = currentReference?.Id,
                parameters = new
                {
                    deployment = generated.Deployment,
                    quality = generated.Quality,
                    size = modelSize,
                    outputFormat = "png",
                    outputWidth = referenceSize,
                    outputHeight = referenceSize
                },
                modelSize,
                sourceWidth = output.SourceWidth,
                sourceHeight = output.SourceHeight,
                outputWidth = output.Width,
                outputHeight = output.Height,
                revisedPrompt = generated.RevisedPrompt
            }, VisualAssetDefaults.JsonOptions),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.Assets.Add(image);
        var state = await dbContext.ResourceStates.SingleOrDefaultAsync(
            item => item.ProjectId == projectId
                && item.ResourceId == resourceId
                && item.ResourceType == AssetType,
            cancellationToken);
        state ??= new ResourceState
        {
            ProjectId = projectId,
            ResourceId = resourceId,
            ResourceType = AssetType
        };
        if (state.CurrentAssetId == Guid.Empty) dbContext.ResourceStates.Add(state);
        state.CurrentAssetId = image.Id;
        state.LifecycleStatus = "active";
        state.UpdatedAtUtc = now;
        dbContext.VisualReferences.Add(new VisualReference
        {
            ProjectId = projectId,
            ImageAssetId = image.Id,
            SubjectResourceId = subjectResourceId,
            SubjectType = document.Kind,
            Purpose = Purpose,
            Source = generated.Deployment,
            ReviewStatus = "active",
            InheritsFromAssetId = previous?.Id,
            CreatedAtUtc = now
        });
        foreach (var dependency in new[]
        {
            (AssetId: subject.Id, Role: "reference-for"),
            (AssetId: promptAsset?.Id, Role: "uses-prompt"),
            (AssetId: currentReference?.Id, Role: "uses-current-reference")
        }.Where(item => item.AssetId is not null))
        {
            dbContext.AssetDependencies.Add(new AssetDependency
            {
                ProjectId = projectId,
                ConsumerAssetId = image.Id,
                SourceAssetId = dependency.AssetId!.Value,
                Role = dependency.Role,
                IsRequired = true,
                CreatedAtUtc = now
            });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToView(image, subjectResourceId, document.Kind, document.Name);
    }

    public Task<BatchVisualReferenceResult> GenerateMissingPromptsAsync(
        Guid projectId,
        string kind,
        CancellationToken cancellationToken,
        Func<BatchVisualReferenceProgress, Task>? reportProgress = null) => RunBatchAsync(
            projectId,
            kind,
            async subjectResourceId =>
            {
                if (await HasCurrentModelPromptAsync(projectId, subjectResourceId, cancellationToken)) return false;
                await GeneratePromptAsync(projectId, subjectResourceId, null, false, cancellationToken);
                return true;
            },
            cancellationToken,
            reportProgress);

    public Task<BatchVisualReferenceResult> GenerateMissingImagesAsync(
        Guid projectId,
        string kind,
        CancellationToken cancellationToken,
        Func<BatchVisualReferenceProgress, Task>? reportProgress = null) => RunBatchAsync(
            projectId,
            kind,
            async subjectResourceId =>
            {
                if (await GetCurrentReferenceAssetAsync(
                    projectId, subjectResourceId, AssetType, Purpose, cancellationToken) is not null)
                    return false;
                await GenerateImageAsync(projectId, subjectResourceId, cancellationToken);
                return true;
            },
            cancellationToken,
            reportProgress);

    public async Task<VisualReferenceImageView> UploadAsync(
        Guid projectId,
        Guid subjectResourceId,
        string fileName,
        string contentType,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        if (bytes.Length == 0) throw new InvalidOperationException("请选择图片文件。");
        if (bytes.LongLength > MaxUploadBytes) throw new InvalidOperationException("参考图不能超过 10 MB。");
        if (!UploadContentTypes.Contains(contentType))
            throw new InvalidOperationException("仅支持 PNG、JPEG 或 WebP 图片。");
        using var bitmap = SKBitmap.Decode(bytes)
            ?? throw new InvalidOperationException("上传文件不是有效图片。");
        var subject = await (
            from resourceState in dbContext.ResourceStates.AsNoTracking()
            join subjectAsset in dbContext.Assets.AsNoTracking() on resourceState.CurrentAssetId equals subjectAsset.Id
            where resourceState.ProjectId == projectId
                && resourceState.ResourceId == subjectResourceId
                && resourceState.ResourceType == VisualAssetDefaults.AssetType
                && resourceState.LifecycleStatus != "retired"
            select subjectAsset)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("视觉资产不存在或已退休。");
        var document = VisualAssetMapper.ReadDocument(subject);
        var previous = await (
            from reference in dbContext.VisualReferences.AsNoTracking()
            join referenceAsset in dbContext.Assets.AsNoTracking() on reference.ImageAssetId equals referenceAsset.Id
            where reference.ProjectId == projectId
                && reference.SubjectResourceId == subjectResourceId
                && reference.Purpose == Purpose
                && referenceAsset.Type == AssetType
            orderby referenceAsset.Version descending
            select referenceAsset)
            .FirstOrDefaultAsync(cancellationToken);
        var resourceId = previous?.ResourceId ?? Guid.NewGuid();
        var version = (previous?.Version ?? 0) + 1;
        var number = previous?.Number
            ?? (await dbContext.Assets.Where(item => item.ProjectId == projectId)
                .Select(item => (int?)item.Number).MaxAsync(cancellationToken) ?? 0) + 1;
        var now = timeProvider.GetUtcNow();
        var extension = contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => ".png"
        };
        var image = new Asset
        {
            ProjectId = projectId,
            ResourceId = resourceId,
            Version = version,
            Number = number,
            Type = AssetType,
            Name = $"{document.Name}参考图",
            BlobKey = $"visual-references/{projectId:N}/{subjectResourceId:N}/v{version}{extension}",
            BlobContent = bytes,
            FileName = $"{document.Name}-上传参考图-v{version}{extension}",
            ContentType = contentType,
            SizeBytes = bytes.LongLength,
            GenerationMetadataJson = JsonSerializer.Serialize(new
            {
                operation = "upload-visual-reference",
                sourceFileName = Path.GetFileName(fileName),
                subjectAssetId = subject.Id,
                subjectResourceId,
                subjectType = document.Kind,
                sourceWidth = bitmap.Width,
                sourceHeight = bitmap.Height,
                references = new[] { GenerationProvenance.Reference(subject, "reference-for") }
            }, VisualAssetDefaults.JsonOptions),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.Assets.Add(image);
        var state = await dbContext.ResourceStates.SingleOrDefaultAsync(
            item => item.ProjectId == projectId
                && item.ResourceId == resourceId
                && item.ResourceType == AssetType,
            cancellationToken);
        state ??= new ResourceState
        {
            ProjectId = projectId,
            ResourceId = resourceId,
            ResourceType = AssetType
        };
        if (state.CurrentAssetId == Guid.Empty) dbContext.ResourceStates.Add(state);
        state.CurrentAssetId = image.Id;
        state.LifecycleStatus = "active";
        state.UpdatedAtUtc = now;
        dbContext.VisualReferences.Add(new VisualReference
        {
            ProjectId = projectId,
            ImageAssetId = image.Id,
            SubjectResourceId = subjectResourceId,
            SubjectType = document.Kind,
            Purpose = Purpose,
            Source = "upload",
            ReviewStatus = "active",
            InheritsFromAssetId = previous?.Id,
            CreatedAtUtc = now
        });
        dbContext.AssetDependencies.Add(new AssetDependency
        {
            ProjectId = projectId,
            ConsumerAssetId = image.Id,
            SourceAssetId = subject.Id,
            Role = "reference-for",
            IsRequired = true,
            CreatedAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToView(image, subjectResourceId, document.Kind, document.Name);
    }

    private async Task<Asset> GetSubjectAsync(
        Guid projectId,
        Guid subjectResourceId,
        CancellationToken cancellationToken) => await (
            from state in dbContext.ResourceStates.AsNoTracking()
            join asset in dbContext.Assets.AsNoTracking() on state.CurrentAssetId equals asset.Id
            where state.ProjectId == projectId
                && state.ResourceId == subjectResourceId
                && state.ResourceType == VisualAssetDefaults.AssetType
                && state.LifecycleStatus != "retired"
            select asset)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("视觉资产不存在或已退休。");

    private async Task<Asset?> GetCurrentReferenceAssetAsync(
        Guid projectId,
        Guid subjectResourceId,
        string assetType,
        string purpose,
        CancellationToken cancellationToken) => await (
            from reference in dbContext.VisualReferences.AsNoTracking()
            join asset in dbContext.Assets.AsNoTracking() on reference.ImageAssetId equals asset.Id
            join state in dbContext.ResourceStates.AsNoTracking() on asset.ResourceId equals state.ResourceId
            where reference.ProjectId == projectId
                && reference.SubjectResourceId == subjectResourceId
                && reference.Purpose == purpose
                && asset.Type == assetType
                && state.ProjectId == projectId
                && state.ResourceType == assetType
                && state.CurrentAssetId == asset.Id
            select asset)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<Asset?> GetLatestReferenceAssetAsync(
        Guid projectId,
        Guid subjectResourceId,
        string assetType,
        string purpose,
        CancellationToken cancellationToken) => await (
            from reference in dbContext.VisualReferences.AsNoTracking()
            join asset in dbContext.Assets.AsNoTracking() on reference.ImageAssetId equals asset.Id
            where reference.ProjectId == projectId
                && reference.SubjectResourceId == subjectResourceId
                && reference.Purpose == purpose
                && asset.Type == assetType
            orderby asset.Version descending
            select asset)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<bool> HasCurrentModelPromptAsync(
        Guid projectId,
        Guid subjectResourceId,
        CancellationToken cancellationToken)
    {
        var prompt = await GetCurrentReferenceAssetAsync(
            projectId,
            subjectResourceId,
            PromptAssetType,
            PromptPurpose,
            cancellationToken);
        if (prompt is null || string.IsNullOrWhiteSpace(prompt.GenerationMetadataJson)) return false;
        try
        {
            using var metadata = JsonDocument.Parse(prompt.GenerationMetadataJson);
            var root = metadata.RootElement;
            if (!root.TryGetProperty("source", out var source)
                || source.GetString() != "model-aware-prompt-agent"
                || !root.TryGetProperty("targetImageModel", out var targetModel))
                return false;
            var configuration = await dbContext.FoundryConfigurations.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
            return string.Equals(
                targetModel.GetString(),
                FoundryConfigurationView.TextToImageModel(configuration),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<BatchVisualReferenceResult> RunBatchAsync(
        Guid projectId,
        string kind,
        Func<Guid, Task<bool>> operation,
        CancellationToken cancellationToken,
        Func<BatchVisualReferenceProgress, Task>? reportProgress)
    {
        kind = kind.Trim().ToLowerInvariant();
        if (kind is not ("character" or "scene" or "prop"))
            throw new InvalidOperationException("批量生成仅支持人物、场景或道具资产。");
        var assets = await (
            from state in dbContext.ResourceStates.AsNoTracking()
            join asset in dbContext.Assets.AsNoTracking() on state.CurrentAssetId equals asset.Id
            where state.ProjectId == projectId
                && state.ResourceType == VisualAssetDefaults.AssetType
                && state.LifecycleStatus != "retired"
                && asset.Type == VisualAssetDefaults.AssetType
            orderby asset.Number
            select asset)
            .ToListAsync(cancellationToken);
        var subjects = assets
            .Select(asset => (Asset: asset, Document: VisualAssetMapper.ReadDocument(asset)))
            .Where(item => item.Document.Kind == kind)
            .ToArray();
        var generated = 0;
        var skipped = 0;
        var errors = new List<string>();
        for (var index = 0; index < subjects.Length; index++)
        {
            var subject = subjects[index];
            var outcome = "已生成";
            try
            {
                if (await operation(subject.Asset.ResourceId)) generated++;
                else
                {
                    skipped++;
                    outcome = "已跳过";
                }
            }
            catch (Exception error) when (error is InvalidOperationException or HttpRequestException)
            {
                errors.Add($"{subject.Document.Name}: {error.Message}");
                outcome = "失败";
            }
            catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
            {
                errors.Add($"{subject.Document.Name}: 图片生成请求超时。{error.Message}");
                outcome = "超时";
            }
            if (reportProgress is not null)
                await reportProgress(new(index + 1, subjects.Length, subject.Document.Name, outcome));
        }
        return new(generated, skipped, errors.Count, errors);
    }

    public static VisualReferencePromptView ToPromptView(
        Asset promptAsset,
        Guid subjectResourceId,
        string subjectType,
        string subjectName)
    {
        var document = ReadPromptDocument(promptAsset);
        return new(
            promptAsset.Id,
            subjectResourceId,
            subjectType,
            subjectName,
            promptAsset.Version,
            document.Prompt,
            document.Instruction,
            document.UseCurrentReference,
            promptAsset.CreatedAtUtc);
    }

    private static PromptDocument ReadPromptDocument(Asset promptAsset) =>
        JsonSerializer.Deserialize<PromptDocument>(
            promptAsset.DocumentJson ?? "{}",
            VisualAssetDefaults.JsonOptions)
        ?? throw new InvalidOperationException("设定图提示词内容无效。");

    public static VisualReferenceImageView ToView(
        Asset image,
        Guid subjectResourceId,
        string subjectType,
        string subjectName)
    {
        var (prompt, revisedPrompt) = ReadPrompts(image.GenerationMetadataJson);
        return new(
            image.Id,
            subjectResourceId,
            subjectType,
            subjectName,
            image.Version,
            image.ContentType ?? "image/png",
            $"/api/v2/projects/{image.ProjectId}/visual-assets/references/{image.Id}/content",
            prompt,
            revisedPrompt,
            image.CreatedAtUtc);
    }

    private static (string Prompt, string? RevisedPrompt) ReadPrompts(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return (string.Empty, null);
        try
        {
            using var metadata = JsonDocument.Parse(metadataJson);
            var root = metadata.RootElement;
            var prompt = root.TryGetProperty("prompt", out var promptElement)
                ? promptElement.GetString() ?? string.Empty
                : string.Empty;
            var revisedPrompt = root.TryGetProperty("revisedPrompt", out var revisedPromptElement)
                ? revisedPromptElement.GetString()
                : null;
            return (prompt, revisedPrompt);
        }
        catch (JsonException)
        {
            return (string.Empty, null);
        }
    }

    private static string LayoutFor(string kind) => kind switch
        {
            "character" => "Use a precise character turnaround layout: the left 55% is one large front-facing full-body view from head to toe; the upper-right contains two smaller full-body views, one back view and one side profile; the lower-right is one large head-and-shoulders close-up. All four views depict exactly the same character, identity, proportions, costume, colors, and accessories. Keep each view completely visible and separated by clean white space.",
            "scene" => "Use a precise environment design layout: the upper 58% is one large front eye-level view of the complete location; the lower-left is the exact reverse view; the lower-right is a clear top-down overhead plan view. Preserve identical architecture, geography, materials, objects, scale, and lighting logic across all three views. Separate views with clean white gutters and include no foreground character or story action.",
            _ => "Show exactly one prop only, centered as one large front-facing orthographic product view. No back view, side view, close-up, inset, duplicate, hand, holder, character, environment, pedestal, or extra object. Leave generous pure white margin around the complete prop."
        };
}

public static class VisualReferenceEndpoints
{
    public static RouteGroupBuilder MapVisualReferenceEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/{resourceId:guid}/reference/prompt/generate", async (
            Guid projectId,
            Guid resourceId,
            GenerateVisualReferenceRequest? request,
            IGenerationTaskScheduler scheduler,
            CancellationToken cancellationToken) => Results.Accepted(value: await scheduler.EnqueueAsync(
                GenerationTaskTypes.VisualReferencePrompt,
                "生成视觉参考提示词",
                new(projectId, ResourceId: resourceId, Instruction: request?.Instruction,
                    UseCurrentReference: request?.UseCurrentReference ?? false),
                cancellationToken)));

        group.MapPost("/{resourceId:guid}/reference/generate", async (
            Guid projectId,
            Guid resourceId,
            IGenerationTaskScheduler scheduler,
            CancellationToken cancellationToken) => Results.Accepted(value: await scheduler.EnqueueAsync(
                GenerationTaskTypes.VisualReferenceImage,
                "生成视觉参考图片",
                new(projectId, ResourceId: resourceId),
                cancellationToken)));

        group.MapPost("/reference/prompts/generate-missing", async (
            Guid projectId,
            BatchVisualReferenceRequest request,
            IGenerationTaskScheduler scheduler,
            CancellationToken cancellationToken) => Results.Accepted(value: await scheduler.EnqueueAsync(
                GenerationTaskTypes.VisualReferencePromptBatch,
                "批量生成缺失的视觉参考提示词",
                new(projectId, Kind: request.Kind),
                cancellationToken)));

        group.MapPost("/reference/images/generate-missing", async (
            Guid projectId,
            BatchVisualReferenceRequest request,
            IGenerationTaskScheduler scheduler,
            CancellationToken cancellationToken) => Results.Accepted(value: await scheduler.EnqueueAsync(
                GenerationTaskTypes.VisualReferenceImageBatch,
                "批量生成缺失的视觉参考图片",
                new(projectId, Kind: request.Kind),
                cancellationToken)));

        group.MapPost("/{resourceId:guid}/reference/upload", async (
            Guid projectId,
            Guid resourceId,
            HttpRequest request,
            IVisualReferenceService service,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { error = "请使用 multipart/form-data 上传参考图。" });
            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            if (file is null) return Results.BadRequest(new { error = "请选择图片文件。" });
            if (file.Length > VisualReferenceService.MaxUploadBytes)
                return Results.BadRequest(new { error = "参考图不能超过 10 MB。" });
            await using var stream = file.OpenReadStream();
            await using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            try
            {
                return Results.Ok(await service.UploadAsync(
                    projectId,
                    resourceId,
                    file.FileName,
                    file.ContentType,
                    buffer.ToArray(),
                    cancellationToken));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
        }).DisableAntiforgery();
        group.MapGet("/references/{assetId:guid}/content", async (
            Guid projectId,
            Guid assetId,
            V2DbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var image = await dbContext.Assets.AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == assetId
                    && item.ProjectId == projectId
                    && item.Type == VisualReferenceService.AssetType,
                cancellationToken);
            return image?.BlobContent is null
                ? Results.NotFound()
                : Results.File(
                    image.BlobContent,
                    image.ContentType ?? "image/png",
                    image.FileName,
                    enableRangeProcessing: false);
        });
        return group;
    }
}