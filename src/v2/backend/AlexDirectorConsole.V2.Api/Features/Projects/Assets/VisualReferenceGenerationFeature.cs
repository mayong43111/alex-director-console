using System.Text.Json;
using AlexDirectorConsole.V2.Api.Features.Projects.Generation;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Assets;

public sealed record VisualReferenceImageView(
    Guid AssetId,
    Guid SubjectResourceId,
    string SubjectType,
    string SubjectName,
    int Version,
    string ContentType,
    string ContentUrl,
    DateTimeOffset CreatedAtUtc);

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
        return rows
            .GroupBy(item => item.Reference.SubjectResourceId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var latest = group.OrderByDescending(item => item.Image.Version).First();
                    return VisualReferenceService.ToView(
                        latest.Image,
                        latest.Reference.SubjectResourceId,
                        latest.Reference.SubjectType,
                        latest.Image.Name.EndsWith("参考图", StringComparison.Ordinal)
                            ? latest.Image.Name[..^3]
                            : latest.Image.Name);
                });
    }
}

public interface IVisualReferenceService
{
    Task<VisualReferenceImageView> GenerateAsync(
        Guid projectId,
        Guid subjectResourceId,
        CancellationToken cancellationToken);
}

public sealed class VisualReferenceService(
    V2DbContext dbContext,
    IProjectCoverGenerator generator,
    TimeProvider timeProvider) : IVisualReferenceService
{
    public const string AssetType = "visual-reference-image";
    public const string Purpose = "generation-reference";

    public async Task<VisualReferenceImageView> GenerateAsync(
        Guid projectId,
        Guid subjectResourceId,
        CancellationToken cancellationToken)
    {
        var subject = await (
            from state in dbContext.ResourceStates.AsNoTracking()
            join asset in dbContext.Assets.AsNoTracking() on state.CurrentAssetId equals asset.Id
            where state.ProjectId == projectId
                && state.ResourceId == subjectResourceId
                && state.ResourceType == VisualAssetDefaults.AssetType
                && state.LifecycleStatus != "retired"
            select asset)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("视觉资产不存在或已退休。");
        var document = VisualAssetMapper.ReadDocument(subject);
        if (document.Kind is not ("character" or "scene"))
        {
            throw new InvalidOperationException("只为人物和场景生成首帧参考图。");
        }

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

        var prompt = BuildPrompt(settings, document);
        var modelSize = ProjectImageOutputProcessor.ModelSizeFor(
            settings.OutputWidth,
            settings.OutputHeight,
            settings.AspectRatio);
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

        var previous = await (
            from reference in dbContext.VisualReferences.AsNoTracking()
            join referenceImage in dbContext.Assets.AsNoTracking() on reference.ImageAssetId equals referenceImage.Id
            where reference.ProjectId == projectId
                && reference.SubjectResourceId == subjectResourceId
                && reference.Purpose == Purpose
                && referenceImage.Type == AssetType
            orderby referenceImage.Version descending
            select referenceImage)
            .FirstOrDefaultAsync(cancellationToken);
        var resourceId = previous?.ResourceId ?? Guid.NewGuid();
        var version = (previous?.Version ?? 0) + 1;
        var number = previous?.Number
            ?? (await dbContext.Assets
                .Where(item => item.ProjectId == projectId)
                .Select(item => (int?)item.Number)
                .MaxAsync(cancellationToken) ?? 0) + 1;
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
                operation = "generate-visual-reference",
                subjectAssetId = subject.Id,
                subjectResourceId,
                subjectType = document.Kind,
                settingsAssetId,
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
                    outputHeight = settings.OutputHeight
                },
                references = new[]
                {
                    GenerationProvenance.Reference(subject, "reference-for"),
                    GenerationProvenance.Reference(settingsAsset, "uses-settings")
                },
                projectStyle = new
                {
                    settings.VisualStyle,
                    settings.ArtDirection,
                    settings.CharacterDesign,
                    settings.ColorPalette,
                    settings.ImagePromptPrefix
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
        dbContext.VisualReferences.Add(new VisualReference
        {
            ProjectId = projectId,
            ImageAssetId = image.Id,
            SubjectResourceId = subjectResourceId,
            SubjectType = document.Kind,
            Purpose = Purpose,
            Source = "gpt-image-2",
            ReviewStatus = "generated",
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
        dbContext.AssetDependencies.Add(new AssetDependency
        {
            ProjectId = projectId,
            ConsumerAssetId = image.Id,
            SourceAssetId = settingsAsset.Id,
            Role = "uses-settings",
            IsRequired = true,
            CreatedAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToView(image, subjectResourceId, document.Kind, document.Name);
    }

    public static VisualReferenceImageView ToView(
        Asset image,
        Guid subjectResourceId,
        string subjectType,
        string subjectName) => new(
            image.Id,
            subjectResourceId,
            subjectType,
            subjectName,
            image.Version,
            image.ContentType ?? "image/png",
            $"/api/v2/projects/{image.ProjectId}/visual-assets/references/{image.Id}/content",
            image.CreatedAtUtc);

    private static string BuildPrompt(
        ProjectSettingsDocument settings,
        VisualAssetDocument document) => $$"""
        Create one production reference image for the {{document.Kind}} "{{document.Name}}".
        Project: {{settings.ProjectName}}
        Visual style: {{settings.VisualStyle}}
        Art direction: {{settings.ArtDirection}}
        Character design rules: {{settings.CharacterDesign}}
        Color strategy: {{settings.ColorPalette}}
        Project image constraints: {{settings.ImagePromptPrefix}}
        Narrative definition: {{document.Summary}}
        Visual definition: {{document.VisualDescription}}
        Mandatory details: {{string.Join("; ", document.MustKeep)}}
        Forbidden details: {{string.Join("; ", document.Avoid)}}
        {{(document.Kind == "character"
            ? "Show the single character clearly from head to toe in a neutral readable pose, with an uncluttered background and no other character."
            : "Show the complete environment clearly as a reusable location reference, with no foreground character and no story action.")}}
        Keep identity, costume, architecture, materials, scale, and colors explicit and reusable across shots.
        Do not render titles, labels, captions, logos, watermarks, UI, borders, model sheets, split panels, or readable text.
        """;
}

public static class VisualReferenceEndpoints
{
    public static RouteGroupBuilder MapVisualReferenceEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/{resourceId:guid}/reference/generate", async (
            Guid projectId,
            Guid resourceId,
            IVisualReferenceService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.GenerateAsync(projectId, resourceId, cancellationToken));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ProjectGenerationConfigurationException error)
            {
                return Results.Conflict(new { error = error.Message });
            }
            catch (InvalidOperationException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
            catch (HttpRequestException error)
            {
                return Results.Problem(
                    title: "参考图生成失败",
                    detail: error.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });
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