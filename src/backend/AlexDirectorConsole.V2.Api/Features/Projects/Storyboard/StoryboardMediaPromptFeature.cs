using System.Text;
using System.Text.Json;
using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Projects.Generation;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Storyboard;

public sealed record StoryboardMediaPromptView(
    Guid AssetId,
    Guid ShotResourceId,
    string Kind,
    int Version,
    string Prompt,
    string? Instruction,
    string? PreviewHash,
    DateTimeOffset CreatedAtUtc);

public sealed record GenerateStoryboardMediaPromptRequest(string? Instruction);

public sealed record BatchStoryboardMediaResult(
    int Generated,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Errors);

internal sealed record StoryboardMediaPromptDocument(
    string Kind,
    string Prompt,
    string? Instruction,
    string? PreviewHash,
    Guid ShotAssetId,
    Guid SettingsAssetId,
    Guid? FirstFrameAssetId,
    Guid? LastFrameAssetId);

public interface IStoryboardMediaPromptService
{
    Task<StoryboardMediaPromptView> GenerateImagePromptAsync(
        Guid projectId,
        Guid productionEpisodeId,
        Guid shotResourceId,
        string? instruction,
        CancellationToken cancellationToken);

    Task<StoryboardMediaPromptView> GenerateVideoPromptAsync(
        Guid projectId,
        Guid productionEpisodeId,
        Guid shotResourceId,
        string? instruction,
        CancellationToken cancellationToken);

    Task<StoryboardMediaPromptView?> GetCurrentAsync(
        Guid projectId,
        Guid shotResourceId,
        string kind,
        CancellationToken cancellationToken);
}

public interface IStoryboardMediaBatchService
{
    Task<BatchStoryboardMediaResult> GenerateMissingImagePromptsAsync(
        Guid projectId,
        Guid productionEpisodeId,
        CancellationToken cancellationToken);

    Task<BatchStoryboardMediaResult> GenerateMissingImagesAsync(
        Guid projectId,
        Guid productionEpisodeId,
        CancellationToken cancellationToken);

    Task<BatchStoryboardMediaResult> GenerateMissingVideoPromptsAsync(
        Guid projectId,
        Guid productionEpisodeId,
        CancellationToken cancellationToken);

    Task<BatchStoryboardMediaResult> GenerateMissingVideosAsync(
        Guid projectId,
        Guid productionEpisodeId,
        CancellationToken cancellationToken);
}

internal static class StoryboardMediaPromptQueries
{
    public static async Task<IReadOnlyDictionary<Guid, StoryboardMediaPromptView>> GetCurrentByShotAsync(
        V2DbContext dbContext,
        Guid projectId,
        IReadOnlyDictionary<Guid, Guid> shotResourceIdsByAssetId,
        string kind,
        CancellationToken cancellationToken)
    {
        if (shotResourceIdsByAssetId.Count == 0)
            return new Dictionary<Guid, StoryboardMediaPromptView>();
        var assetType = StoryboardMediaPromptService.AssetTypeFor(kind);
        var shotAssetIds = shotResourceIdsByAssetId.Keys.ToArray();
        var rows = await (
            from dependency in dbContext.AssetDependencies.AsNoTracking()
            join prompt in dbContext.Assets.AsNoTracking() on dependency.ConsumerAssetId equals prompt.Id
            join state in dbContext.ResourceStates.AsNoTracking() on prompt.ResourceId equals state.ResourceId
            where dependency.ProjectId == projectId
                && dependency.Role == "prompt-for-shot"
                && shotAssetIds.Contains(dependency.SourceAssetId)
                && prompt.Type == assetType
                && state.ProjectId == projectId
                && state.ResourceType == assetType
                && state.CurrentAssetId == prompt.Id
            select new { dependency.SourceAssetId, Prompt = prompt })
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(item => shotResourceIdsByAssetId[item.SourceAssetId])
            .ToDictionary(
                group => group.Key,
                group => StoryboardMediaPromptService.ToView(
                    group.OrderByDescending(item => item.Prompt.Version).First().Prompt,
                    group.Key,
                    kind));
    }
}

public sealed class StoryboardMediaPromptService(
    V2DbContext dbContext,
    IShotFrameService frameService,
    IShotVideoService videoService,
    TimeProvider timeProvider) : IStoryboardMediaPromptService
{
    public const string ImageKind = "image";
    public const string VideoKind = "video";
    public const string ImageAssetType = "storyboard-image-prompt";
    public const string VideoAssetType = "storyboard-video-prompt";

    public async Task<StoryboardMediaPromptView> GenerateImagePromptAsync(
        Guid projectId,
        Guid productionEpisodeId,
        Guid shotResourceId,
        string? instruction,
        CancellationToken cancellationToken)
    {
        instruction = NormalizeInstruction(instruction);
        var preview = await frameService.PreviewFirstFrameAsync(
            projectId,
            productionEpisodeId,
            shotResourceId,
            instruction,
            cancellationToken)
            ?? throw new InvalidOperationException("镜头不存在。");
        var inputs = await LoadInputsAsync(projectId, productionEpisodeId, shotResourceId, cancellationToken);
        return await SaveAsync(
            inputs,
            ImageKind,
            preview.Prompt,
            instruction,
            null,
            null,
            null,
            cancellationToken);
    }

    public async Task<StoryboardMediaPromptView> GenerateVideoPromptAsync(
        Guid projectId,
        Guid productionEpisodeId,
        Guid shotResourceId,
        string? instruction,
        CancellationToken cancellationToken)
    {
        instruction = NormalizeInstruction(instruction);
        var preview = await videoService.PreviewAsync(
            projectId,
            productionEpisodeId,
            shotResourceId,
            instruction,
            cancellationToken)
            ?? throw new InvalidOperationException("镜头不存在。");
        var inputs = await LoadInputsAsync(projectId, productionEpisodeId, shotResourceId, cancellationToken);
        return await SaveAsync(
            inputs,
            VideoKind,
            preview.Prompt,
            instruction,
            preview.PreviewHash,
            preview.FirstFrameAssetId,
            preview.LastFrameAssetId,
            cancellationToken);
    }

    public async Task<StoryboardMediaPromptView?> GetCurrentAsync(
        Guid projectId,
        Guid shotResourceId,
        string kind,
        CancellationToken cancellationToken)
    {
        var shotAssetId = await dbContext.ShotDefinitions.AsNoTracking()
            .Where(item => item.ProjectId == projectId && item.ShotResourceId == shotResourceId)
            .Select(item => (Guid?)item.ShotAssetId)
            .SingleOrDefaultAsync(cancellationToken);
        if (shotAssetId is null) return null;
        var prompts = await StoryboardMediaPromptQueries.GetCurrentByShotAsync(
            dbContext,
            projectId,
            new Dictionary<Guid, Guid> { [shotAssetId.Value] = shotResourceId },
            kind,
            cancellationToken);
        return prompts.GetValueOrDefault(shotResourceId);
    }

    private async Task<StoryboardMediaPromptView> SaveAsync(
        PromptInputs inputs,
        string kind,
        string prompt,
        string? instruction,
        string? previewHash,
        Guid? firstFrameAssetId,
        Guid? lastFrameAssetId,
        CancellationToken cancellationToken)
    {
        var assetType = AssetTypeFor(kind);
        var previous = await GetLatestAsync(inputs.ProjectId, inputs.ShotAsset.ResourceId, assetType, cancellationToken);
        var resourceId = previous?.ResourceId ?? Guid.NewGuid();
        var version = (previous?.Version ?? 0) + 1;
        var number = previous?.Number
            ?? (await dbContext.Assets
                .Where(item => item.ProjectId == inputs.ProjectId)
                .Select(item => (int?)item.Number)
                .MaxAsync(cancellationToken) ?? 0) + 1;
        var document = new StoryboardMediaPromptDocument(
            kind,
            prompt,
            instruction,
            previewHash,
            inputs.ShotAsset.Id,
            inputs.SettingsAssetId,
            firstFrameAssetId,
            lastFrameAssetId);
        var documentJson = JsonSerializer.Serialize(document, StoryboardDefaults.JsonOptions);
        var now = timeProvider.GetUtcNow();
        var asset = new Asset
        {
            ProjectId = inputs.ProjectId,
            ProductionEpisodeId = inputs.ProductionEpisodeId,
            ResourceId = resourceId,
            Version = version,
            Number = number,
            Type = assetType,
            Name = $"{inputs.ShotAsset.Name}{(kind == ImageKind ? "图片" : "视频")}提示词",
            DocumentJson = documentJson,
            ContentType = "application/json",
            SizeBytes = Encoding.UTF8.GetByteCount(documentJson),
            GenerationMetadataJson = JsonSerializer.Serialize(new
            {
                operation = $"generate-storyboard-{kind}-prompt",
                shotAssetId = inputs.ShotAsset.Id,
                settingsAssetId = inputs.SettingsAssetId,
                prompt,
                instruction,
                previewHash,
                firstFrameAssetId,
                lastFrameAssetId,
                references = new[]
                {
                    GenerationProvenance.Reference(inputs.ShotAsset, "prompt-for-shot")
                }
            }, StoryboardDefaults.JsonOptions),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.Assets.Add(asset);
        var state = await dbContext.ResourceStates.SingleOrDefaultAsync(
            item => item.ProjectId == inputs.ProjectId
                && item.ResourceId == resourceId
                && item.ResourceType == assetType,
            cancellationToken);
        state ??= new ResourceState
        {
            ProjectId = inputs.ProjectId,
            ResourceId = resourceId,
            ResourceType = assetType
        };
        if (state.CurrentAssetId == Guid.Empty) dbContext.ResourceStates.Add(state);
        state.CurrentAssetId = asset.Id;
        state.LifecycleStatus = "active";
        state.UpdatedAtUtc = now;
        dbContext.AssetDependencies.Add(new AssetDependency
        {
            ProjectId = inputs.ProjectId,
            ConsumerAssetId = asset.Id,
            SourceAssetId = inputs.ShotAsset.Id,
            Role = "prompt-for-shot",
            IsRequired = true,
            CreatedAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToView(asset, inputs.ShotAsset.ResourceId, kind);
    }

    private async Task<Asset?> GetLatestAsync(
        Guid projectId,
        Guid shotResourceId,
        string assetType,
        CancellationToken cancellationToken) => await (
        from dependency in dbContext.AssetDependencies.AsNoTracking()
        join shot in dbContext.Assets.AsNoTracking() on dependency.SourceAssetId equals shot.Id
        join prompt in dbContext.Assets.AsNoTracking() on dependency.ConsumerAssetId equals prompt.Id
        where dependency.ProjectId == projectId
            && dependency.Role == "prompt-for-shot"
            && shot.ResourceId == shotResourceId
            && prompt.Type == assetType
        orderby prompt.Version descending
        select prompt).FirstOrDefaultAsync(cancellationToken);

    private async Task<PromptInputs> LoadInputsAsync(
        Guid projectId,
        Guid productionEpisodeId,
        Guid shotResourceId,
        CancellationToken cancellationToken)
    {
        var definition = await dbContext.ShotDefinitions.AsNoTracking().SingleOrDefaultAsync(
            item => item.ProjectId == projectId
                && item.ProductionEpisodeId == productionEpisodeId
                && item.ShotResourceId == shotResourceId,
            cancellationToken)
            ?? throw new InvalidOperationException("镜头不存在。");
        var project = await dbContext.Projects.AsNoTracking().SingleAsync(
            item => item.Id == projectId,
            cancellationToken);
        if (project.CurrentCreativeSettingsId is not Guid settingsAssetId)
            throw new InvalidOperationException("生成提示词前必须先保存项目设定。");
        var shotAsset = await dbContext.Assets.AsNoTracking().SingleAsync(
            item => item.Id == definition.ShotAssetId,
            cancellationToken);
        return new(projectId, productionEpisodeId, shotAsset, settingsAssetId);
    }

    private static string? NormalizeInstruction(string? instruction)
    {
        instruction = instruction?.Trim();
        if (instruction?.Length > 2000)
            throw new InvalidOperationException("本轮修改意见不能超过 2000 个字符。");
        return instruction;
    }

    internal static string AssetTypeFor(string kind) => kind switch
    {
        ImageKind => ImageAssetType,
        VideoKind => VideoAssetType,
        _ => throw new InvalidOperationException("不支持的提示词类型。")
    };

    internal static StoryboardMediaPromptView ToView(Asset asset, Guid shotResourceId, string kind)
    {
        var document = JsonSerializer.Deserialize<StoryboardMediaPromptDocument>(
            asset.DocumentJson ?? "{}",
            StoryboardDefaults.JsonOptions)
            ?? throw new InvalidOperationException("当前提示词无法读取。");
        return new(
            asset.Id,
            shotResourceId,
            kind,
            asset.Version,
            document.Prompt,
            document.Instruction,
            document.PreviewHash,
            asset.CreatedAtUtc);
    }

    private sealed record PromptInputs(
        Guid ProjectId,
        Guid ProductionEpisodeId,
        Asset ShotAsset,
        Guid SettingsAssetId);
}

public sealed class StoryboardMediaBatchService(
    V2DbContext dbContext,
    IStoryboardMediaPromptService promptService,
    ICommandDispatcher commandDispatcher,
    IShotVideoService videoService) : IStoryboardMediaBatchService
{
    public Task<BatchStoryboardMediaResult> GenerateMissingImagePromptsAsync(
        Guid projectId,
        Guid productionEpisodeId,
        CancellationToken cancellationToken) => RunAsync(
        projectId,
        productionEpisodeId,
        async (shot, token) =>
        {
            if (await promptService.GetCurrentAsync(projectId, shot.ShotResourceId, StoryboardMediaPromptService.ImageKind, token) is not null)
                return false;
            await promptService.GenerateImagePromptAsync(projectId, productionEpisodeId, shot.ShotResourceId, null, token);
            return true;
        },
        cancellationToken);

    public Task<BatchStoryboardMediaResult> GenerateMissingImagesAsync(
        Guid projectId,
        Guid productionEpisodeId,
        CancellationToken cancellationToken) => RunAsync(
        projectId,
        productionEpisodeId,
        async (shot, token) =>
        {
            if (await HasRequiredFramesAsync(projectId, shot, token)) return false;
            var prompt = await promptService.GetCurrentAsync(
                projectId,
                shot.ShotResourceId,
                StoryboardMediaPromptService.ImageKind,
                token)
                ?? throw new InvalidOperationException("请先生成图片提示词。");
            await commandDispatcher.SendAsync(
                new StartShotProductionCommand(
                    projectId,
                    productionEpisodeId,
                    shot.ShotResourceId,
                    prompt.Prompt,
                    prompt.Instruction),
                token);
            return true;
        },
        cancellationToken);

    public Task<BatchStoryboardMediaResult> GenerateMissingVideoPromptsAsync(
        Guid projectId,
        Guid productionEpisodeId,
        CancellationToken cancellationToken) => RunAsync(
        projectId,
        productionEpisodeId,
        async (shot, token) =>
        {
            if (await promptService.GetCurrentAsync(projectId, shot.ShotResourceId, StoryboardMediaPromptService.VideoKind, token) is not null)
                return false;
            await promptService.GenerateVideoPromptAsync(projectId, productionEpisodeId, shot.ShotResourceId, null, token);
            return true;
        },
        cancellationToken);

    public Task<BatchStoryboardMediaResult> GenerateMissingVideosAsync(
        Guid projectId,
        Guid productionEpisodeId,
        CancellationToken cancellationToken) => RunAsync(
        projectId,
        productionEpisodeId,
        async (shot, token) =>
        {
            var existing = await ShotVideoQueries.GetAsync(
                dbContext,
                projectId,
                productionEpisodeId,
                shot.ShotResourceId,
                token);
            if (existing?.Status is "queued" or "running" or "completed") return false;
            var prompt = await promptService.GetCurrentAsync(
                projectId,
                shot.ShotResourceId,
                StoryboardMediaPromptService.VideoKind,
                token)
                ?? throw new InvalidOperationException("请先生成视频提示词。");
            await videoService.StartAsync(
                projectId,
                productionEpisodeId,
                shot.ShotResourceId,
                prompt.Prompt,
                prompt.PreviewHash ?? throw new InvalidOperationException("当前视频提示词缺少预览校验值，请重新生成。"),
                prompt.Instruction,
                token);
            return true;
        },
        cancellationToken);

    private async Task<BatchStoryboardMediaResult> RunAsync(
        Guid projectId,
        Guid productionEpisodeId,
        Func<ShotDefinition, CancellationToken, Task<bool>> operation,
        CancellationToken cancellationToken)
    {
        var shots = await dbContext.ShotDefinitions.AsNoTracking()
            .Where(item => item.ProjectId == projectId && item.ProductionEpisodeId == productionEpisodeId)
            .OrderBy(item => item.SceneNumber)
            .ThenBy(item => item.ShotNumber)
            .ToListAsync(cancellationToken);
        var generated = 0;
        var skipped = 0;
        var failed = 0;
        var errors = new List<string>();
        foreach (var shot in shots)
        {
            try
            {
                if (await operation(shot, cancellationToken)) generated++;
                else skipped++;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                failed++;
                errors.Add($"S{shot.SceneNumber:D2}-{shot.ShotNumber:D2}: {error.Message}");
            }
        }
        return new(generated, skipped, failed, errors);
    }

    private async Task<bool> HasRequiredFramesAsync(
        Guid projectId,
        ShotDefinition definition,
        CancellationToken cancellationToken)
    {
        var shotAsset = await dbContext.Assets.AsNoTracking().SingleAsync(
            item => item.Id == definition.ShotAssetId,
            cancellationToken);
        var document = JsonSerializer.Deserialize<StoryboardShotDocument>(
            shotAsset.DocumentJson ?? "{}",
            StoryboardDefaults.JsonOptions)
            ?? throw new InvalidOperationException("当前镜头内容无法读取。");
        if (!await HasFrameAsync(projectId, definition.ShotAssetId, "frame-for-shot", cancellationToken))
            return false;
        return ShotProductionModes.ForShot(document) != ShotProductionModes.FirstLastContinuous
            || await HasFrameAsync(projectId, definition.ShotAssetId, "last-frame-for-shot", cancellationToken);
    }

    private Task<bool> HasFrameAsync(
        Guid projectId,
        Guid shotAssetId,
        string role,
        CancellationToken cancellationToken) => (
        from dependency in dbContext.AssetDependencies.AsNoTracking()
        join frame in dbContext.Assets.AsNoTracking() on dependency.ConsumerAssetId equals frame.Id
        join state in dbContext.ResourceStates.AsNoTracking() on frame.ResourceId equals state.ResourceId
        where dependency.ProjectId == projectId
            && dependency.SourceAssetId == shotAssetId
            && dependency.Role == role
            && frame.Type == ShotFrameService.AssetType
            && frame.BlobContent != null
            && state.ProjectId == projectId
            && state.ResourceType == ShotFrameService.AssetType
            && state.CurrentAssetId == frame.Id
        select frame.Id).AnyAsync(cancellationToken);
}

public static class StoryboardMediaEndpoints
{
    public static IEndpointRouteBuilder MapStoryboardMedia(this IEndpointRouteBuilder app)
    {
        var route = "/api/v2/projects/{projectId:guid}/production-episodes/{productionEpisodeId:guid}/storyboard";
        app.MapPost($"{route}/shots/{{shotResourceId:guid}}/image/prompt", async (
            Guid projectId,
            Guid productionEpisodeId,
            Guid shotResourceId,
            GenerateStoryboardMediaPromptRequest request,
            IStoryboardMediaPromptService service,
            CancellationToken cancellationToken) => await ExecuteAsync(async () =>
                Results.Ok(await service.GenerateImagePromptAsync(
                    projectId,
                    productionEpisodeId,
                    shotResourceId,
                    request.Instruction,
                    cancellationToken))));
        app.MapPost($"{route}/shots/{{shotResourceId:guid}}/image/generate", async (
            Guid projectId,
            Guid productionEpisodeId,
            Guid shotResourceId,
            IStoryboardMediaPromptService promptService,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) => await ExecuteAsync(async () =>
        {
            var prompt = await promptService.GetCurrentAsync(projectId, shotResourceId, StoryboardMediaPromptService.ImageKind, cancellationToken)
                ?? throw new InvalidOperationException("请先生成图片提示词。");
            var production = await dispatcher.SendAsync(
                new StartShotProductionCommand(projectId, productionEpisodeId, shotResourceId, prompt.Prompt, prompt.Instruction),
                cancellationToken);
            return production is null ? Results.NotFound() : Results.Ok(production);
        }));
        app.MapPost($"{route}/shots/{{shotResourceId:guid}}/video/prompt", async (
            Guid projectId,
            Guid productionEpisodeId,
            Guid shotResourceId,
            GenerateStoryboardMediaPromptRequest request,
            IStoryboardMediaPromptService service,
            CancellationToken cancellationToken) => await ExecuteAsync(async () =>
                Results.Ok(await service.GenerateVideoPromptAsync(
                    projectId,
                    productionEpisodeId,
                    shotResourceId,
                    request.Instruction,
                    cancellationToken))));
        app.MapPost($"{route}/shots/{{shotResourceId:guid}}/video/generate", async (
            Guid projectId,
            Guid productionEpisodeId,
            Guid shotResourceId,
            IStoryboardMediaPromptService promptService,
            IShotVideoService videoService,
            CancellationToken cancellationToken) => await ExecuteAsync(async () =>
        {
            var prompt = await promptService.GetCurrentAsync(projectId, shotResourceId, StoryboardMediaPromptService.VideoKind, cancellationToken)
                ?? throw new InvalidOperationException("请先生成视频提示词。");
            var production = await videoService.StartAsync(
                projectId,
                productionEpisodeId,
                shotResourceId,
                prompt.Prompt,
                prompt.PreviewHash ?? throw new InvalidOperationException("当前视频提示词缺少预览校验值，请重新生成。"),
                prompt.Instruction,
                cancellationToken);
            return production is null ? Results.NotFound() : Results.Accepted(value: production);
        }));
        MapBatch(app, $"{route}/batch/image-prompts", (service, projectId, episodeId, token) =>
            service.GenerateMissingImagePromptsAsync(projectId, episodeId, token));
        MapBatch(app, $"{route}/batch/images", (service, projectId, episodeId, token) =>
            service.GenerateMissingImagesAsync(projectId, episodeId, token));
        MapBatch(app, $"{route}/batch/video-prompts", (service, projectId, episodeId, token) =>
            service.GenerateMissingVideoPromptsAsync(projectId, episodeId, token));
        MapBatch(app, $"{route}/batch/videos", (service, projectId, episodeId, token) =>
            service.GenerateMissingVideosAsync(projectId, episodeId, token));
        return app;
    }

    private static void MapBatch(
        IEndpointRouteBuilder app,
        string route,
        Func<IStoryboardMediaBatchService, Guid, Guid, CancellationToken, Task<BatchStoryboardMediaResult>> operation) => app.MapPost(
        route,
        async (Guid projectId, Guid productionEpisodeId, IStoryboardMediaBatchService service, CancellationToken cancellationToken) =>
            Results.Ok(await operation(service, projectId, productionEpisodeId, cancellationToken)));

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (InvalidOperationException error)
        {
            return Results.BadRequest(new { error = error.Message });
        }
        catch (HttpRequestException error)
        {
            return Results.Problem(detail: error.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
