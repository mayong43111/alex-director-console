using System.Text;
using System.Text.Json;
using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Projects.Generation;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Settings;

public sealed record ProjectSettingsView(
    Guid ProjectId,
    int Version,
    string ProjectName,
    string Description,
    string ContentType,
    string TargetAudience,
    int PlannedEpisodeCount,
    int TargetEpisodeSeconds,
    string AspectRatio,
    int OutputWidth,
    int OutputHeight,
    string VisualStyle,
    string ArtDirection,
    string CharacterDesign,
    string ColorPalette,
    string CameraLanguage,
    string SoundStrategy,
    string ImagePromptPrefix,
    Guid? AssetId,
    int ImpactedAssetCount,
    ProjectCoverView? Cover,
    DateTimeOffset? UpdatedAtUtc);

public sealed record GetProjectSettingsQuery(Guid ProjectId) : IQuery<ProjectSettingsView?>;

public sealed class GetProjectSettingsQueryHandler(V2DbContext dbContext)
    : IQueryHandler<GetProjectSettingsQuery, ProjectSettingsView?>
{
    public async Task<ProjectSettingsView?> HandleAsync(
        GetProjectSettingsQuery query,
        CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == query.ProjectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var cover = await ProjectCoverQueries.GetLatestAsync(
            dbContext,
            project.Id,
            cancellationToken);
        if (project.CurrentCreativeSettingsId is null)
        {
            return ProjectSettingsDefaults.ForProject(project) with { Cover = cover };
        }

        var asset = await dbContext.Assets
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == project.CurrentCreativeSettingsId
                    && item.ProjectId == project.Id
                    && item.Type == ProjectSettingsDefaults.AssetType,
                cancellationToken);
        if (asset?.DocumentJson is null)
        {
            return ProjectSettingsDefaults.ForProject(project);
        }

        var document = JsonSerializer.Deserialize<ProjectSettingsDocument>(
            asset.DocumentJson,
            ProjectSettingsDefaults.JsonOptions);
        var impactedAssetCount = await dbContext.AssetDependencies.AsNoTracking().CountAsync(
            item => item.ProjectId == project.Id && item.SourceAssetId == asset.Id,
            cancellationToken);
        return document is null
            ? ProjectSettingsDefaults.ForProject(project) with { Cover = cover }
            : document.ToView(
                project.Id,
                asset.Version,
                asset.UpdatedAtUtc,
                asset.Id,
                impactedAssetCount,
                cover);
    }
}

public sealed record SaveProjectSettingsCommand(
    Guid ProjectId,
    string? ProjectName,
    string? Description,
    string? ContentType,
    string? TargetAudience,
    int PlannedEpisodeCount,
    int TargetEpisodeSeconds,
    string? AspectRatio,
    int OutputWidth,
    int OutputHeight,
    string? VisualStyle,
    string? ArtDirection,
    string? CharacterDesign,
    string? ColorPalette,
    string? CameraLanguage,
    string? SoundStrategy,
    string? ImagePromptPrefix) : ICommand<SaveProjectSettingsResult>;

public enum SaveProjectSettingsStatus
{
    Success,
    NotFound,
    Invalid
}

public sealed record SaveProjectSettingsResult(
    SaveProjectSettingsStatus Status,
    ProjectSettingsView? Settings,
    Dictionary<string, string[]> Errors);

public sealed class SaveProjectSettingsCommandHandler(
    V2DbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<SaveProjectSettingsCommand, SaveProjectSettingsResult>
{
    public async Task<SaveProjectSettingsResult> HandleAsync(
        SaveProjectSettingsCommand command,
        CancellationToken cancellationToken)
    {
        var errors = Validate(command);
        if (errors.Count > 0)
        {
            return new(SaveProjectSettingsStatus.Invalid, null, errors);
        }

        var project = await dbContext.Projects
            .SingleOrDefaultAsync(item => item.Id == command.ProjectId, cancellationToken);
        if (project is null)
        {
            return new(SaveProjectSettingsStatus.NotFound, null, errors);
        }

        Asset? previousAsset = null;
        if (project.CurrentCreativeSettingsId is not null)
        {
            previousAsset = await dbContext.Assets.SingleOrDefaultAsync(
                item => item.Id == project.CurrentCreativeSettingsId
                    && item.ProjectId == project.Id
                    && item.Type == ProjectSettingsDefaults.AssetType,
                cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var document = ProjectSettingsDocument.FromCommand(command);
        var documentJson = JsonSerializer.Serialize(document, ProjectSettingsDefaults.JsonOptions);
        var version = (previousAsset?.Version ?? 0) + 1;
        var assetNumber = previousAsset?.Number
            ?? (await dbContext.Assets
                .Where(item => item.ProjectId == project.Id)
                .Select(item => (int?)item.Number)
                .MaxAsync(cancellationToken) ?? 0) + 1;
        var asset = new Asset
        {
            ProjectId = project.Id,
            ResourceId = previousAsset?.ResourceId ?? Guid.NewGuid(),
            Version = version,
            Number = assetNumber,
            Type = ProjectSettingsDefaults.AssetType,
            Name = $"项目设定 v{version}",
            SchemaVersion = ProjectSettingsDefaults.SchemaVersion,
            DocumentJson = documentJson,
            ContentType = "application/json",
            SizeBytes = Encoding.UTF8.GetByteCount(documentJson),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.Assets.Add(asset);
        var state = previousAsset is null
            ? null
            : await dbContext.ResourceStates.SingleOrDefaultAsync(
                item => item.ProjectId == project.Id
                    && item.ResourceId == previousAsset.ResourceId
                    && item.ResourceType == ProjectSettingsDefaults.AssetType,
                cancellationToken);
        state ??= new ResourceState
        {
            ProjectId = project.Id,
            ResourceId = asset.ResourceId,
            ResourceType = ProjectSettingsDefaults.AssetType
        };
        if (state.CurrentAssetId == Guid.Empty) dbContext.ResourceStates.Add(state);
        state.CurrentAssetId = asset.Id;
        state.LifecycleStatus = "draft";
        state.IsStale = false;
        state.StaleReason = null;
        state.StaleSinceUtc = null;
        state.UpdatedAtUtc = now;
        project.Name = document.ProjectName;
        project.Description = document.Description;
        project.CurrentCreativeSettingsId = asset.Id;
        project.UpdatedAtUtc = now;
        if (previousAsset is not null)
        {
            await AssetStalenessPropagation.MarkRequiredDependentsStaleAsync(
                dbContext,
                previousAsset,
                asset,
                now,
                cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        var cover = await ProjectCoverQueries.GetLatestAsync(
            dbContext,
            project.Id,
            cancellationToken);

        return new(
            SaveProjectSettingsStatus.Success,
            document.ToView(project.Id, version, now, asset.Id, 0, cover),
            errors);
    }

    private static Dictionary<string, string[]> Validate(SaveProjectSettingsCommand command)
    {
        var errors = new Dictionary<string, string[]>();
        AddTextError(errors, "projectName", command.ProjectName, 200, "请输入项目名称。");
        AddTextError(errors, "contentType", command.ContentType, 80, "请选择片型。");
        AddTextError(errors, "targetAudience", command.TargetAudience, 300, "请输入目标受众。");
        AddTextError(errors, "visualStyle", command.VisualStyle, 200, "请输入视觉风格。");
        AddTextError(errors, "characterDesign", command.CharacterDesign, 1000, "请输入角色造型规则。");
        if ((command.Description?.Trim().Length ?? 0) > 4000) errors["description"] = ["项目简介不能超过 4000 字符。"];
        if ((command.ArtDirection?.Trim().Length ?? 0) > 2000) errors["artDirection"] = ["美术方向不能超过 2000 字符。"];
        if ((command.ColorPalette?.Trim().Length ?? 0) > 1000) errors["colorPalette"] = ["色彩策略不能超过 1000 字符。"];
        if ((command.CameraLanguage?.Trim().Length ?? 0) > 2000) errors["cameraLanguage"] = ["摄影语言不能超过 2000 字符。"];
        if ((command.SoundStrategy?.Trim().Length ?? 0) > 2000) errors["soundStrategy"] = ["声音策略不能超过 2000 字符。"];
        if ((command.ImagePromptPrefix?.Trim().Length ?? 0) > 4000) errors["imagePromptPrefix"] = ["图像提示词前缀不能超过 4000 字符。"];
        if (command.PlannedEpisodeCount != -1 && command.PlannedEpisodeCount is < 1 or > 1000)
            errors["plannedEpisodeCount"] = ["计划集数必须为 -1，或 1 到 1000 之间的整数。"];
        if (command.TargetEpisodeSeconds is < 1 or > 86400) errors["targetEpisodeSeconds"] = ["单集时长必须在 1 到 86400 秒之间。"];
        if (command.AspectRatio is not ("16:9" or "9:16" or "2.39:1")) errors["aspectRatio"] = ["请选择支持的画幅比例。"];
        if (command.OutputWidth is < 64 or > 8192 || command.OutputHeight is < 64 or > 8192) errors["resolution"] = ["输出尺寸必须在 64 到 8192 像素之间。"];
        return errors;
    }

    private static void AddTextError(
        IDictionary<string, string[]> errors,
        string field,
        string? value,
        int maxLength,
        string requiredMessage)
    {
        if (string.IsNullOrWhiteSpace(value)) errors[field] = [requiredMessage];
        else if (value.Trim().Length > maxLength) errors[field] = [$"内容不能超过 {maxLength} 字符。"];
    }
}

public sealed record SaveProjectSettingsRequest(
    string? ProjectName,
    string? Description,
    string? ContentType,
    string? TargetAudience,
    int PlannedEpisodeCount,
    int TargetEpisodeSeconds,
    string? AspectRatio,
    int OutputWidth,
    int OutputHeight,
    string? VisualStyle,
    string? ArtDirection,
    string? CharacterDesign,
    string? ColorPalette,
    string? CameraLanguage,
    string? SoundStrategy,
    string? ImagePromptPrefix);

public static class ProjectSettingsEndpoints
{
    public static IEndpointRouteBuilder MapProjectSettings(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/projects/{projectId:guid}/settings");
        group.MapGet("/", async (
            Guid projectId,
            IQueryDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            var settings = await dispatcher.QueryAsync(
                new GetProjectSettingsQuery(projectId),
                cancellationToken);
            return settings is null ? Results.NotFound() : Results.Ok(settings);
        });
        group.MapPut("/", async (
            Guid projectId,
            SaveProjectSettingsRequest request,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            var result = await dispatcher.SendAsync(
                new SaveProjectSettingsCommand(
                    projectId,
                    request.ProjectName,
                    request.Description,
                    request.ContentType,
                    request.TargetAudience,
                    request.PlannedEpisodeCount,
                    request.TargetEpisodeSeconds,
                    request.AspectRatio,
                    request.OutputWidth,
                    request.OutputHeight,
                    request.VisualStyle,
                    request.ArtDirection,
                    request.CharacterDesign,
                    request.ColorPalette,
                    request.CameraLanguage,
                    request.SoundStrategy,
                    request.ImagePromptPrefix),
                cancellationToken);
            return result.Status switch
            {
                SaveProjectSettingsStatus.Success => Results.Ok(result.Settings),
                SaveProjectSettingsStatus.NotFound => Results.NotFound(),
                _ => Results.ValidationProblem(result.Errors)
            };
        });
        group.MapPost("/cover", async (
            Guid projectId,
            ProjectCoverGenerateRequest request,
            IProjectCoverService coverService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.ConfirmedPrompt))
                {
                    return Results.BadRequest(new { error = "请先预览并确认完整提示词和参数。" });
                }
                return Results.Ok(await coverService.GenerateConfirmedAsync(
                    projectId,
                    request.Instruction,
                    request.ConfirmedPrompt,
                    cancellationToken));
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
                    title: "封面生成失败",
                    detail: error.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });
        group.MapPost("/cover/preview", async (
            Guid projectId,
            ProjectCoverPreviewRequest request,
            IProjectCoverService coverService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await coverService.PreviewAsync(
                    projectId,
                    request.Instruction,
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
        });
        group.MapGet("/cover/{assetId:guid}/content", async (
            Guid projectId,
            Guid assetId,
            V2DbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var asset = await dbContext.Assets
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.Id == assetId
                        && item.ProjectId == projectId
                        && item.Type == ProjectCoverQueries.AssetType,
                    cancellationToken);
            return asset?.BlobContent is null
                ? Results.NotFound()
                : Results.File(
                    asset.BlobContent,
                    asset.ContentType ?? "image/png",
                    asset.FileName,
                    enableRangeProcessing: false);
        });
        group.MapPost("/assist", async (
            Guid projectId,
            ProjectSettingsAssistRequest request,
            V2DbContext dbContext,
            IProjectSettingsAssistant assistant,
            CancellationToken cancellationToken) =>
        {
            if (!await dbContext.Projects.AsNoTracking().AnyAsync(
                item => item.Id == projectId,
                cancellationToken))
            {
                return Results.NotFound();
            }
            try
            {
                return Results.Ok(await assistant.WriteAsync(request, cancellationToken));
            }
            catch (ArgumentException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
            catch (ProjectGenerationConfigurationException error)
            {
                return Results.Conflict(new { error = error.Message });
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return Results.Problem(
                    title: "AI 帮写失败",
                    detail: error.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });
        return app;
    }
}

internal sealed record ProjectSettingsDocument(
    string ProjectName,
    string Description,
    string ContentType,
    string TargetAudience,
    int PlannedEpisodeCount,
    int TargetEpisodeSeconds,
    string AspectRatio,
    int OutputWidth,
    int OutputHeight,
    string VisualStyle,
    string ArtDirection,
    string CharacterDesign,
    string ColorPalette,
    string CameraLanguage,
    string SoundStrategy,
    string ImagePromptPrefix)
{
    public static ProjectSettingsDocument FromCommand(SaveProjectSettingsCommand command) => new(
        command.ProjectName!.Trim(),
        command.Description?.Trim() ?? string.Empty,
        command.ContentType!.Trim(),
        command.TargetAudience!.Trim(),
        command.PlannedEpisodeCount,
        command.TargetEpisodeSeconds,
        command.AspectRatio!,
        command.OutputWidth,
        command.OutputHeight,
        command.VisualStyle!.Trim(),
        command.ArtDirection?.Trim() ?? string.Empty,
        command.CharacterDesign!.Trim(),
        command.ColorPalette?.Trim() ?? string.Empty,
        command.CameraLanguage?.Trim() ?? string.Empty,
        command.SoundStrategy?.Trim() ?? string.Empty,
        command.ImagePromptPrefix?.Trim() ?? string.Empty);

    public ProjectSettingsView ToView(
        Guid projectId,
        int version,
        DateTimeOffset updatedAtUtc,
        Guid? assetId = null,
        int impactedAssetCount = 0,
        ProjectCoverView? cover = null) => new(
        projectId,
        version,
        ProjectName,
        Description,
        ContentType,
        TargetAudience,
        PlannedEpisodeCount,
        TargetEpisodeSeconds,
        AspectRatio,
        OutputWidth,
        OutputHeight,
        VisualStyle,
        ArtDirection,
        CharacterDesign,
        ColorPalette,
        CameraLanguage,
        SoundStrategy,
        ImagePromptPrefix,
        assetId,
        impactedAssetCount,
        cover,
        updatedAtUtc);
}

internal static class ProjectSettingsDefaults
{
    public const string AssetType = "creative-settings";
    public const int SchemaVersion = 2;
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    public static ProjectSettingsView ForProject(Project project) => new(
        project.Id,
        0,
        project.Name,
        project.Description ?? string.Empty,
        "动画短剧",
        "全年龄冒险故事观众",
        -1,
        100,
        "16:9",
        1920,
        1080,
        "电影感漫画",
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        null,
        0,
        null,
        null);
}