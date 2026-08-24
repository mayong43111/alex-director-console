using System.Text;
using System.Text.Json;
using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Generation;
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

public sealed record GenerateStoryboardMediaBatchRequest(IReadOnlyList<Guid>? ShotResourceIds);

public sealed record BatchStoryboardMediaResult(
    int Generated,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Errors);

public sealed record StoryboardMediaBatchProgress(
    int Completed,
    int Total,
    string ShotCode,
    string Outcome,
    string Phase = "processing",
    int? ProgressCompleted = null);

internal sealed record StoryboardMediaPromptDocument(
    string Kind,
    string Prompt,
    string? Instruction,
    string? PreviewHash,
    Guid ShotAssetId,
    Guid SettingsAssetId,
    Guid? FirstFrameAssetId,
    Guid? LastFrameAssetId,
    IReadOnlyList<Guid>? ReferenceImageAssetIds,
    IReadOnlyList<Guid>? PropAssetIds);

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
        IReadOnlyList<Guid>? shotResourceIds,
        CancellationToken cancellationToken,
        Func<StoryboardMediaBatchProgress, Task>? reportProgress = null);

    Task<BatchStoryboardMediaResult> GenerateMissingImagesAsync(
        Guid projectId,
        Guid productionEpisodeId,
        IReadOnlyList<Guid>? shotResourceIds,
        CancellationToken cancellationToken,
        Func<StoryboardMediaBatchProgress, Task>? reportProgress = null);

    Task<BatchStoryboardMediaResult> GenerateMissingVideoPromptsAsync(
        Guid projectId,
        Guid productionEpisodeId,
        IReadOnlyList<Guid>? shotResourceIds,
        CancellationToken cancellationToken,
        Func<StoryboardMediaBatchProgress, Task>? reportProgress = null);

    Task<BatchStoryboardMediaResult> GenerateMissingVideosAsync(
        Guid projectId,
        Guid productionEpisodeId,
        IReadOnlyList<Guid>? shotResourceIds,
        CancellationToken cancellationToken,
        Func<StoryboardMediaBatchProgress, Task>? reportProgress = null);
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
            lastFrameAssetId,
            inputs.ReferenceImageAssetIds,
            inputs.PropAssetIds);
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
        var preflight = await ShotProductionPreflight.EvaluateAsync(dbContext, definition, cancellationToken);
        return new(
            projectId,
            productionEpisodeId,
            shotAsset,
            settingsAssetId,
            preflight.Inputs.ReferenceImageAssetIds,
            preflight.Inputs.PropAssetIds);
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
        Guid SettingsAssetId,
        IReadOnlyList<Guid> ReferenceImageAssetIds,
        IReadOnlyList<Guid> PropAssetIds);
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
        IReadOnlyList<Guid>? shotResourceIds,
        CancellationToken cancellationToken,
        Func<StoryboardMediaBatchProgress, Task>? reportProgress = null) => RunAsync(
        projectId,
        productionEpisodeId,
        async (shot, token) =>
        {
            if (shotResourceIds is null && await promptService.GetCurrentAsync(projectId, shot.ShotResourceId, StoryboardMediaPromptService.ImageKind, token) is not null)
                return false;
            await promptService.GenerateImagePromptAsync(projectId, productionEpisodeId, shot.ShotResourceId, null, token);
            return true;
        },
        shotResourceIds,
        cancellationToken,
        reportProgress);

    public Task<BatchStoryboardMediaResult> GenerateMissingImagesAsync(
        Guid projectId,
        Guid productionEpisodeId,
        IReadOnlyList<Guid>? shotResourceIds,
        CancellationToken cancellationToken,
        Func<StoryboardMediaBatchProgress, Task>? reportProgress = null) => RunAsync(
        projectId,
        productionEpisodeId,
        async (shot, token) =>
        {
            if (shotResourceIds is null && await HasRequiredFramesAsync(projectId, shot, token)) return false;
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
        shotResourceIds,
        cancellationToken,
        reportProgress);

    public Task<BatchStoryboardMediaResult> GenerateMissingVideoPromptsAsync(
        Guid projectId,
        Guid productionEpisodeId,
        IReadOnlyList<Guid>? shotResourceIds,
        CancellationToken cancellationToken,
        Func<StoryboardMediaBatchProgress, Task>? reportProgress = null) => RunAsync(
        projectId,
        productionEpisodeId,
        async (shot, token) =>
        {
            if (shotResourceIds is null && await promptService.GetCurrentAsync(projectId, shot.ShotResourceId, StoryboardMediaPromptService.VideoKind, token) is not null)
                return false;
            await promptService.GenerateVideoPromptAsync(projectId, productionEpisodeId, shot.ShotResourceId, null, token);
            return true;
        },
        shotResourceIds,
        cancellationToken,
        reportProgress);

    public async Task<BatchStoryboardMediaResult> GenerateMissingVideosAsync(
        Guid projectId,
        Guid productionEpisodeId,
        IReadOnlyList<Guid>? shotResourceIds,
        CancellationToken cancellationToken,
        Func<StoryboardMediaBatchProgress, Task>? reportProgress = null)
    {
        var query = dbContext.ShotDefinitions.AsNoTracking()
            .Where(item => item.ProjectId == projectId && item.ProductionEpisodeId == productionEpisodeId);
        if (shotResourceIds is not null)
            query = query.Where(item => shotResourceIds.Contains(item.ShotResourceId));
        var shots = await query
            .OrderBy(item => item.SceneNumber)
            .ThenBy(item => item.ShotNumber)
            .ToListAsync(cancellationToken);
        var generated = 0;
        var skipped = 0;
        var failed = 0;
        var resolved = 0;
        var errors = new List<string>();
        var pending = new List<(ShotDefinition Shot, Guid RunId)>();

        for (var index = 0; index < shots.Count; index++)
        {
            var shot = shots[index];
            var shotCode = $"S{shot.SceneNumber:D2}-{shot.ShotNumber:D2}";
            var existing = await ShotVideoQueries.GetAsync(
                dbContext,
                projectId,
                productionEpisodeId,
                shot.ShotResourceId,
                cancellationToken);
            if (shotResourceIds is null && existing?.Status is "queued" or "running" or "completed")
            {
                skipped++;
                resolved++;
                if (reportProgress is not null)
                    await reportProgress(new(index + 1, shots.Count, shotCode, "已跳过", "submitting", resolved));
                continue;
            }

            try
            {
                var prompt = await promptService.GetCurrentAsync(
                    projectId,
                    shot.ShotResourceId,
                    StoryboardMediaPromptService.VideoKind,
                    cancellationToken)
                    ?? throw new InvalidOperationException("请先生成视频提示词。");
                var started = await videoService.StartAsync(
                    projectId,
                    productionEpisodeId,
                    shot.ShotResourceId,
                    prompt.Prompt,
                    prompt.PreviewHash ?? throw new InvalidOperationException("当前视频提示词缺少预览校验值，请重新生成。"),
                    prompt.Instruction,
                    cancellationToken)
                    ?? throw new InvalidOperationException("镜头不存在。");
                var submitted = await ShotVideoQueries.GetAsync(
                    dbContext, projectId, productionEpisodeId, shot.ShotResourceId, cancellationToken);
                if (submitted?.Status == "failed")
                    throw new InvalidOperationException(submitted.Error ?? "ComfyUI 提交失败。");
                pending.Add((shot, started.RunId));
                if (reportProgress is not null)
                    await reportProgress(new(index + 1, shots.Count, shotCode, "已提交 ComfyUI", "submitting", resolved));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                failed++;
                resolved++;
                errors.Add($"{shotCode}: {error.Message}");
                if (reportProgress is not null)
                    await reportProgress(new(index + 1, shots.Count, shotCode, "提交失败", "submitting", resolved));
            }
        }

        while (pending.Count > 0)
        {
            foreach (var entry in pending.ToArray())
            {
                var current = await ShotVideoQueries.GetAsync(
                    dbContext, projectId, productionEpisodeId, entry.Shot.ShotResourceId, cancellationToken);
                if (current?.Status is not ("completed" or "failed" or "cancelled")) continue;

                pending.Remove(entry);
                resolved++;
                var shotCode = $"S{entry.Shot.SceneNumber:D2}-{entry.Shot.ShotNumber:D2}";
                var outcome = current.Status == "completed" ? "已生成" : current.Status == "cancelled" ? "已取消" : "失败";
                if (current.Status == "completed") generated++;
                else if (current.Status == "failed")
                {
                    failed++;
                    errors.Add($"{shotCode}: {current.Error ?? "视频生成失败。"}");
                }
                if (reportProgress is not null)
                    await reportProgress(new(resolved, shots.Count, shotCode, outcome, "checking", resolved));
            }
            if (pending.Count > 0)
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        return new(generated, skipped, failed, errors);
    }

    private async Task<BatchStoryboardMediaResult> RunAsync(
        Guid projectId,
        Guid productionEpisodeId,
        Func<ShotDefinition, CancellationToken, Task<bool>> operation,
        IReadOnlyList<Guid>? shotResourceIds,
        CancellationToken cancellationToken,
        Func<StoryboardMediaBatchProgress, Task>? reportProgress)
    {
        var query = dbContext.ShotDefinitions.AsNoTracking()
            .Where(item => item.ProjectId == projectId && item.ProductionEpisodeId == productionEpisodeId)
            .AsQueryable();
        if (shotResourceIds is not null)
            query = query.Where(item => shotResourceIds.Contains(item.ShotResourceId));
        var shots = await query
            .OrderBy(item => item.SceneNumber)
            .ThenBy(item => item.ShotNumber)
            .ToListAsync(cancellationToken);
        var generated = 0;
        var skipped = 0;
        var failed = 0;
        var errors = new List<string>();
        for (var index = 0; index < shots.Count; index++)
        {
            var shot = shots[index];
            var outcome = "已跳过";
            try
            {
                if (await operation(shot, cancellationToken))
                {
                    generated++;
                    outcome = "已生成";
                }
                else
                {
                    skipped++;
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                failed++;
                outcome = "失败";
                errors.Add($"S{shot.SceneNumber:D2}-{shot.ShotNumber:D2}: {error.Message}");
            }
            if (reportProgress is not null)
            {
                await reportProgress(new(
                    index + 1,
                    shots.Count,
                    $"S{shot.SceneNumber:D2}-{shot.ShotNumber:D2}",
                    outcome));
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
            IGenerationTaskScheduler scheduler,
            CancellationToken cancellationToken) => Results.Accepted(value: await scheduler.EnqueueAsync(
                GenerationTaskTypes.StoryboardImagePrompt,
                "生成分镜图片提示词",
                new(projectId, productionEpisodeId, shotResourceId, request.Instruction),
                cancellationToken)));
        app.MapPost($"{route}/shots/{{shotResourceId:guid}}/image/generate", async (
            Guid projectId,
            Guid productionEpisodeId,
            Guid shotResourceId,
            IGenerationTaskScheduler scheduler,
            CancellationToken cancellationToken) => Results.Accepted(value: await scheduler.EnqueueAsync(
                GenerationTaskTypes.StoryboardImage,
                "生成分镜图片",
                new(projectId, productionEpisodeId, shotResourceId),
                cancellationToken)));
        app.MapPost($"{route}/shots/{{shotResourceId:guid}}/video/prompt", async (
            Guid projectId,
            Guid productionEpisodeId,
            Guid shotResourceId,
            GenerateStoryboardMediaPromptRequest request,
            IGenerationTaskScheduler scheduler,
            CancellationToken cancellationToken) => Results.Accepted(value: await scheduler.EnqueueAsync(
                GenerationTaskTypes.StoryboardVideoPrompt,
                "生成分镜视频提示词",
                new(projectId, productionEpisodeId, shotResourceId, request.Instruction),
                cancellationToken)));
        app.MapPost($"{route}/shots/{{shotResourceId:guid}}/video/generate", async (
            Guid projectId,
            Guid productionEpisodeId,
            Guid shotResourceId,
            IGenerationTaskScheduler scheduler,
            CancellationToken cancellationToken) => Results.Accepted(value: await scheduler.EnqueueAsync(
                GenerationTaskTypes.StoryboardVideo,
                "创建分镜视频生成任务",
                new(projectId, productionEpisodeId, shotResourceId),
                cancellationToken)));
        MapBatch(app, $"{route}/batch/image-prompts", GenerationTaskTypes.StoryboardImagePromptBatch, "批量生成分镜图片提示词");
        MapBatch(app, $"{route}/batch/images", GenerationTaskTypes.StoryboardImageBatch, "批量生成分镜图片");
        MapBatch(app, $"{route}/batch/video-prompts", GenerationTaskTypes.StoryboardVideoPromptBatch, "批量生成分镜视频提示词");
        MapBatch(app, $"{route}/batch/videos", GenerationTaskTypes.StoryboardVideoBatch, "批量生成分镜视频");
        return app;
    }

    private static void MapBatch(
        IEndpointRouteBuilder app,
        string route,
        string taskType,
        string intent) => app.MapPost(
        route,
        async (Guid projectId, Guid productionEpisodeId, GenerateStoryboardMediaBatchRequest? request, IGenerationTaskScheduler scheduler, CancellationToken cancellationToken) =>
            Results.Accepted(value: await scheduler.EnqueueAsync(
                taskType,
                intent,
            new(projectId, productionEpisodeId, ResourceIds: request?.ShotResourceIds is { Count: > 0 } ? request.ShotResourceIds : null),
                cancellationToken)));

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
