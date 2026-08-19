using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Sources;

public sealed record AdaptationShotPlanDraft(
    int ShotNumber,
    double DurationSeconds,
    string ShotSize,
    string CameraAngle,
    string CameraMovement,
    string Purpose);

public sealed record AdaptationSceneDraft(
    int SceneNumber,
    string Heading,
    string Summary,
    IReadOnlyList<string> Characters,
    IReadOnlyList<string> Props,
    string StoryFunction,
    string DialogueNotes,
    double? TargetSeconds = null,
    string? Rhythm = null,
    string? VisualContrast = null,
    IReadOnlyList<AdaptationShotPlanDraft>? ShotPlan = null);

public sealed record AdaptationEpisodeDraft(
    int ProposalNumber,
    string Title,
    string Logline,
    int TargetSeconds,
    IReadOnlyList<int> SourceChapterNumbers,
    IReadOnlyList<AdaptationSceneDraft> Scenes,
    IReadOnlyList<string>? SmallHooks = null,
    IReadOnlyList<string>? BigHooks = null);

public sealed record AdaptationScriptResult(
    string Title,
    string Approach,
    IReadOnlyList<AdaptationEpisodeDraft> Episodes,
    string Model,
    string Runtime,
    IReadOnlyList<string>? OverallSmallHooks = null,
    IReadOnlyList<string>? OverallBigHooks = null);

public sealed record ScreenplayDialogueDraft(
    string Character,
    string? Parenthetical,
    IReadOnlyList<string> Lines);

public sealed record ProductionScriptSceneDraft(
    int SceneNumber,
    string Heading,
    string Summary,
    string Action,
    IReadOnlyList<ScreenplayDialogueDraft> Dialogues,
    IReadOnlyList<string> Characters,
    IReadOnlyList<string> Props,
    string StoryFunction,
    double TargetSeconds,
    string Rhythm,
    string VisualContrast,
    IReadOnlyList<AdaptationShotPlanDraft> ShotPlan,
    string? DialogueIntent = null);

public sealed record ProductionScriptEpisodeDraft(
    string Title,
    string Logline,
    int TargetSeconds,
    IReadOnlyList<ProductionScriptSceneDraft> Scenes,
    IReadOnlyList<string> SmallHooks,
    IReadOnlyList<string> BigHooks);

public sealed record AdaptationScriptView(
    Guid AssetId,
    Guid ResourceId,
    int Version,
    Guid SourceResourceId,
    Guid SourceAssetId,
    int SourceVersion,
    Guid AnalysisAssetId,
    string Status,
    bool HasNewerSourceVersion,
    string Title,
    string Approach,
    IReadOnlyList<string> OverallSmallHooks,
    IReadOnlyList<string> OverallBigHooks,
    IReadOnlyList<AdaptationEpisodeDraft> Episodes,
    IReadOnlyList<Guid> ProductionEpisodeIds,
    string Model,
    string Runtime,
    DateTimeOffset UpdatedAtUtc);

internal sealed record AdaptationScriptDocument(
    Guid SourceResourceId,
    Guid SourceAssetId,
    int SourceVersion,
    Guid AnalysisAssetId,
    string Status,
    string Title,
    string Approach,
    IReadOnlyList<AdaptationEpisodeDraft> Episodes,
    IReadOnlyList<Guid> ProductionEpisodeIds,
    string Model,
    string Runtime,
    IReadOnlyList<string>? OverallSmallHooks = null,
    IReadOnlyList<string>? OverallBigHooks = null);

internal sealed record ProductionScriptPackageDocument(
    Guid AdaptationScriptAssetId,
    Guid ProductionEpisodeId,
    ProductionScriptEpisodeDraft? Script = null,
    AdaptationEpisodeDraft? Episode = null);

public sealed record ProductionScriptPackageView(
    Guid AssetId,
    Guid ResourceId,
    int Version,
    Guid ProductionEpisodeId,
    int EpisodeNumber,
    string Title,
    double? TargetSeconds,
    string Status,
    Guid AdaptationScriptAssetId,
    bool IsLegacyOutline,
    ProductionScriptEpisodeDraft Episode,
    DateTimeOffset UpdatedAtUtc);

public interface IAdaptationScriptWriter
{
    Task<AdaptationScriptResult> WriteAsync(
        ProjectSettingsView projectSettings,
        StoryMaterialAnalysisView analysis,
        int desiredEpisodeCount,
        string? instruction,
        CancellationToken cancellationToken);

    Task<ProductionScriptEpisodeDraft> WriteProductionScriptAsync(
        ProjectSettingsView projectSettings,
        StoryMaterialAnalysisView analysis,
        AdaptationEpisodeDraft outline,
        ProductionScriptEpisodeDraft? previousScript,
        string? correction,
        CancellationToken cancellationToken);
}

public sealed record GetAdaptationScriptQuery(Guid ProjectId, Guid SourceResourceId)
    : IQuery<AdaptationScriptView?>;

public sealed record GenerateAdaptationScriptCommand(
    Guid ProjectId,
    Guid SourceResourceId,
    int? DesiredEpisodeCount,
    string? Instruction) : ICommand<AdaptationScriptView?>;

public sealed record AppendAdaptationEpisodeCommand(
    Guid ProjectId,
    Guid SourceResourceId,
    string? Instruction) : ICommand<AdaptationScriptView?>;

public sealed record RegenerateAdaptationEpisodeCommand(
    Guid ProjectId,
    Guid SourceResourceId,
    int EpisodeNumber,
    string Instruction) : ICommand<AdaptationScriptView?>;

public sealed record ConfirmAdaptationScriptCommand(Guid ProjectId, Guid SourceResourceId)
    : ICommand<AdaptationScriptView?>;

public sealed record RegenerateProductionScriptCommand(Guid ProjectId, Guid ProductionEpisodeId)
    : ICommand<ProductionScriptPackageView?>;

public sealed record GetProductionScriptPackageQuery(Guid ProjectId, Guid ProductionEpisodeId)
    : IQuery<ProductionScriptPackageView?>;

public sealed class GetAdaptationScriptQueryHandler(V2DbContext dbContext)
    : IQueryHandler<GetAdaptationScriptQuery, AdaptationScriptView?>
{
    public Task<AdaptationScriptView?> HandleAsync(
        GetAdaptationScriptQuery query,
        CancellationToken cancellationToken) =>
        AdaptationScriptQueries.GetCurrentAsync(
            dbContext,
            query.ProjectId,
            query.SourceResourceId,
            cancellationToken);
}

public sealed class GetProductionScriptPackageQueryHandler(V2DbContext dbContext)
    : IQueryHandler<GetProductionScriptPackageQuery, ProductionScriptPackageView?>
{
    public async Task<ProductionScriptPackageView?> HandleAsync(
        GetProductionScriptPackageQuery query,
        CancellationToken cancellationToken)
    {
        var episode = await dbContext.ProductionEpisodes.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == query.ProductionEpisodeId && item.ProjectId == query.ProjectId,
            cancellationToken);
        if (episode is null) return null;

        var asset = await (
            from state in dbContext.ResourceStates.AsNoTracking()
            join candidate in dbContext.Assets.AsNoTracking() on state.CurrentAssetId equals candidate.Id
            where state.ProjectId == query.ProjectId
                && state.ResourceType == "script-package"
                && candidate.ProductionEpisodeId == query.ProductionEpisodeId
                && candidate.Type == "script-package"
            select candidate)
            .FirstOrDefaultAsync(cancellationToken);
        if (asset?.DocumentJson is null) return null;

        var document = JsonSerializer.Deserialize<ProductionScriptPackageDocument>(
            asset.DocumentJson,
            ProjectSourceDefaults.JsonOptions);
        var script = document?.Script ?? ConvertLegacyEpisode(document?.Episode);
        return document is null || script is null
            ? null
            : new(
                asset.Id,
                asset.ResourceId,
                asset.Version,
                episode.Id,
                episode.EpisodeNumber,
                episode.Title,
                episode.TargetSeconds,
                episode.Status,
                document.AdaptationScriptAssetId,
                document.Script is null,
                script,
                asset.UpdatedAtUtc);
    }

    private static ProductionScriptEpisodeDraft? ConvertLegacyEpisode(AdaptationEpisodeDraft? episode)
    {
        if (episode is null) return null;
        return new(
            episode.Title,
            episode.Logline,
            episode.TargetSeconds,
            (episode.Scenes ?? []).Select(scene => new ProductionScriptSceneDraft(
                scene.SceneNumber,
                scene.Heading,
                scene.Summary,
                scene.Summary,
                [],
                scene.Characters ?? [],
                scene.Props ?? [],
                scene.StoryFunction ?? string.Empty,
                scene.TargetSeconds ?? 0,
                scene.Rhythm ?? string.Empty,
                scene.VisualContrast ?? string.Empty,
                scene.ShotPlan ?? [],
                string.IsNullOrWhiteSpace(scene.DialogueNotes) ? null : scene.DialogueNotes.Trim())).ToArray(),
            episode.SmallHooks ?? [],
            episode.BigHooks ?? []);
    }
}

public sealed class GenerateAdaptationScriptCommandHandler(
    V2DbContext dbContext,
    IAdaptationScriptWriter writer,
    TimeProvider timeProvider)
    : ICommandHandler<GenerateAdaptationScriptCommand, AdaptationScriptView?>
{
    public async Task<AdaptationScriptView?> HandleAsync(
        GenerateAdaptationScriptCommand command,
        CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == command.ProjectId,
            cancellationToken);
        if (project is null) return null;
        var projectSettings = await new GetProjectSettingsQueryHandler(dbContext).HandleAsync(
            new GetProjectSettingsQuery(command.ProjectId),
            cancellationToken);
        if (projectSettings is null) return null;
        var desiredEpisodeCount = command.DesiredEpisodeCount ?? projectSettings.PlannedEpisodeCount;
        if (desiredEpisodeCount is < 1 or > 6)
            throw new ArgumentException("单次改写的剧集数量必须为 1 至 6；更长项目请分批规划。", nameof(command));
        var analysis = await StoryMaterialAnalysisQueries.GetCurrentAsync(
            dbContext,
            command.ProjectId,
            command.SourceResourceId,
            cancellationToken);
        if (analysis is null) return null;
        if (analysis.IsStale)
            throw new StoryDevelopmentConflictException("原文已有新版本，请先重新分析素材，再生成新的改编大纲。");

        var result = await writer.WriteAsync(
            projectSettings,
            analysis,
            desiredEpisodeCount,
            command.Instruction,
            cancellationToken);
        if (result.Episodes.Count != desiredEpisodeCount)
            throw new InvalidOperationException($"GPT-5.4 应返回 {desiredEpisodeCount} 集，实际返回 {result.Episodes.Count} 集。");
        result = result with { OverallSmallHooks = [], OverallBigHooks = [] };

        return await SaveDraftAsync(
            dbContext,
            timeProvider,
            command.ProjectId,
            command.SourceResourceId,
            analysis,
            projectSettings.AssetId,
            result,
            desiredEpisodeCount,
            command.Instruction,
            cancellationToken);
    }

    internal static async Task<AdaptationScriptView> SaveDraftAsync(
        V2DbContext dbContext,
        TimeProvider timeProvider,
        Guid projectId,
        Guid sourceResourceId,
        StoryMaterialAnalysisView analysis,
        Guid? projectSettingsAssetId,
        AdaptationScriptResult result,
        int desiredEpisodeCount,
        string? instruction,
        CancellationToken cancellationToken)
    {
        var previous = await AdaptationScriptQueries.FindCurrentAsync(
            dbContext,
            projectId,
            sourceResourceId,
            cancellationToken);
        var normalizedEpisodes = result.Episodes
            .Take(6)
            .Select(NormalizeOutline)
            .ToArray();
        var document = new AdaptationScriptDocument(
            sourceResourceId,
            analysis.SourceAssetId,
            analysis.SourceVersion,
            analysis.AssetId,
            "draft",
            result.Title,
            result.Approach,
            normalizedEpisodes,
            [],
            result.Model,
            result.Runtime,
            result.OverallSmallHooks ?? [],
            result.OverallBigHooks ?? []);
        var documentJson = JsonSerializer.Serialize(document, ProjectSourceDefaults.JsonOptions);
        var now = timeProvider.GetUtcNow();
        var number = previous.Asset?.Number
            ?? (await dbContext.Assets
                .Where(item => item.ProjectId == projectId)
                .Select(item => (int?)item.Number)
                .MaxAsync(cancellationToken) ?? 0) + 1;
        var resourceId = previous.Asset?.ResourceId ?? Guid.NewGuid();
        var version = previous.Asset is null
            ? 1
            : await dbContext.Assets
                .Where(item => item.ProjectId == projectId && item.ResourceId == resourceId)
                .MaxAsync(item => item.Version, cancellationToken) + 1;
        var asset = new Asset
        {
            ProjectId = projectId,
            ProductionEpisodeId = null,
            ResourceId = resourceId,
            Version = version,
            Number = number,
            Type = AdaptationScriptQueries.AssetType,
            Name = result.Title,
            DocumentJson = documentJson,
            ContentType = "application/json",
            SizeBytes = Encoding.UTF8.GetByteCount(documentJson),
            GenerationMetadataJson = JsonSerializer.Serialize(
                new { result.Model, result.Runtime, desiredEpisodeCount, instruction },
                ProjectSourceDefaults.JsonOptions),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.Assets.Add(asset);
        AddDependency(dbContext, projectId, asset.Id, analysis.AssetId, "based-on-analysis", now);
        AddDependency(dbContext, projectId, asset.Id, analysis.SourceAssetId, "reference-source", now);
        if (projectSettingsAssetId.HasValue)
            AddDependency(dbContext, projectId, asset.Id, projectSettingsAssetId.Value, "uses-project-settings", now);

        var state = previous.State ?? new ResourceState
        {
            ProjectId = projectId,
            ResourceId = asset.ResourceId,
            ResourceType = AdaptationScriptQueries.AssetType
        };
        if (previous.State is null) dbContext.ResourceStates.Add(state);
        state.CurrentAssetId = asset.Id;
        state.LifecycleStatus = "draft";
        state.IsStale = false;
        state.StaleReason = null;
        state.StaleSinceUtc = null;
        state.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await AdaptationScriptQueries.ToViewAsync(dbContext, asset, document, cancellationToken);
    }

    private static AdaptationEpisodeDraft NormalizeOutline(AdaptationEpisodeDraft episode)
    {
        if (episode.Scenes.Count == 0)
            throw new InvalidOperationException($"第 {episode.ProposalNumber} 集没有大纲节点。");
        return episode with
        {
            Scenes = episode.Scenes.Select((scene, index) => scene with
            {
                SceneNumber = index + 1,
                TargetSeconds = null,
                Rhythm = null,
                VisualContrast = null,
                ShotPlan = null
            }).ToArray()
        };
    }

    internal static void AddDependency(
        V2DbContext dbContext,
        Guid projectId,
        Guid consumerAssetId,
        Guid sourceAssetId,
        string role,
        DateTimeOffset now) => dbContext.AssetDependencies.Add(new AssetDependency
        {
            ProjectId = projectId,
            ConsumerAssetId = consumerAssetId,
            SourceAssetId = sourceAssetId,
            Role = role,
            IsRequired = true,
            CreatedAtUtc = now
        });
}

public sealed class AppendAdaptationEpisodeCommandHandler(
    V2DbContext dbContext,
    IAdaptationScriptWriter writer,
    TimeProvider timeProvider)
    : ICommandHandler<AppendAdaptationEpisodeCommand, AdaptationScriptView?>
{
    public async Task<AdaptationScriptView?> HandleAsync(
        AppendAdaptationEpisodeCommand command,
        CancellationToken cancellationToken)
    {
        var current = await AdaptationScriptQueries.FindCurrentAsync(
            dbContext,
            command.ProjectId,
            command.SourceResourceId,
            cancellationToken);
        if (current.Asset is null) return null;
        var currentDocument = AdaptationScriptQueries.ReadDocument(current.Asset);
        if (currentDocument.Status != "draft")
            throw new StoryDevelopmentConflictException("已确认的剧本不能直接添加剧集，请新建改编版本。");
        if (currentDocument.Episodes.Count >= 6)
            throw new ArgumentException("单份改编草案最多包含 6 集。", nameof(command));

        var projectSettings = await new GetProjectSettingsQueryHandler(dbContext).HandleAsync(
            new GetProjectSettingsQuery(command.ProjectId),
            cancellationToken);
        if (projectSettings is null) return null;
        var analysis = await StoryMaterialAnalysisQueries.GetCurrentAsync(
            dbContext,
            command.ProjectId,
            command.SourceResourceId,
            cancellationToken);
        if (analysis is null) return null;
        if (analysis.IsStale)
            throw new StoryDevelopmentConflictException("原文已有新版本，请先重新分析素材，再添加剧集。");

        var nextNumber = currentDocument.Episodes.Count + 1;
        var existingOutline = string.Join("；", currentDocument.Episodes.Select(item =>
            $"E{item.ProposalNumber:D2}《{item.Title}》：{item.Logline}"));
        var appendInstruction = $"只生成现有草案之后的第 {nextNumber} 集，不要重写已有剧集。已有分集：{existingOutline}。{command.Instruction}";
        var generated = await writer.WriteAsync(
            projectSettings,
            analysis,
            1,
            appendInstruction,
            cancellationToken);
        if (generated.Episodes.Count != 1)
            throw new InvalidOperationException("GPT-5.4 添加剧集时必须只返回一集。");

        var appendedEpisode = generated.Episodes[0] with { ProposalNumber = nextNumber };
        var result = new AdaptationScriptResult(
            currentDocument.Title,
            currentDocument.Approach,
            [.. currentDocument.Episodes, appendedEpisode],
            generated.Model,
            generated.Runtime,
            [],
            []);
        return await GenerateAdaptationScriptCommandHandler.SaveDraftAsync(
            dbContext,
            timeProvider,
            command.ProjectId,
            command.SourceResourceId,
            analysis,
            projectSettings.AssetId,
            result,
            result.Episodes.Count,
            command.Instruction,
            cancellationToken);
    }
}

public sealed class RegenerateAdaptationEpisodeCommandHandler(
    V2DbContext dbContext,
    IAdaptationScriptWriter writer,
    TimeProvider timeProvider)
    : ICommandHandler<RegenerateAdaptationEpisodeCommand, AdaptationScriptView?>
{
    public async Task<AdaptationScriptView?> HandleAsync(
        RegenerateAdaptationEpisodeCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Instruction))
            throw new ArgumentException("请填写本集的改编要求。", nameof(command));

        var current = await AdaptationScriptQueries.FindCurrentAsync(
            dbContext,
            command.ProjectId,
            command.SourceResourceId,
            cancellationToken);
        if (current.Asset is null) return null;
        var currentDocument = AdaptationScriptQueries.ReadDocument(current.Asset);
        var targetEpisode = currentDocument.Episodes.SingleOrDefault(
            item => item.ProposalNumber == command.EpisodeNumber);
        if (targetEpisode is null)
            throw new ArgumentException($"草案中不存在第 {command.EpisodeNumber} 集。", nameof(command));

        var projectSettings = await new GetProjectSettingsQueryHandler(dbContext).HandleAsync(
            new GetProjectSettingsQuery(command.ProjectId),
            cancellationToken);
        if (projectSettings is null) return null;
        var analysis = await StoryMaterialAnalysisQueries.GetCurrentAsync(
            dbContext,
            command.ProjectId,
            command.SourceResourceId,
            cancellationToken);
        if (analysis is null) return null;
        if (analysis.IsStale)
            throw new StoryDevelopmentConflictException("原文已有新版本，请先分析新增章节，再重新生成剧集。");

        var existingOutline = string.Join("；", currentDocument.Episodes.Select(item =>
            $"E{item.ProposalNumber:D2}《{item.Title}》：{item.Logline}"));
        var rewriteInstruction = $"""
            只重新生成现有草案中的第 {command.EpisodeNumber} 集，并且只返回这一集，不得改写其他剧集。
            原著仅作为人物、世界和事件素材，不得逐段照搬；必须按项目设定和用户要求重排、删减、合并并补充原创连接，写成适合目标受众与单集时长的剧本。
            保持与前后集的连续性。已有分集：{existingOutline}
            当前第 {command.EpisodeNumber} 集：{JsonSerializer.Serialize(targetEpisode, ProjectSourceDefaults.JsonOptions)}
            用户改编要求：{command.Instruction.Trim()}
            本集 smallHooks 和 bigHooks 只能描述本集内实际发生的事件，不得包含其他集的爆点。
            """;
        var generated = await writer.WriteAsync(
            projectSettings,
            analysis,
            1,
            rewriteInstruction,
            cancellationToken);
        if (generated.Episodes.Count != 1)
            throw new InvalidOperationException("GPT-5.4 重新生成剧集时必须只返回一集。");

        var replacement = generated.Episodes[0] with { ProposalNumber = command.EpisodeNumber };
        var result = new AdaptationScriptResult(
            currentDocument.Title,
            currentDocument.Approach,
            currentDocument.Episodes
                .Select(item => item.ProposalNumber == command.EpisodeNumber ? replacement : item)
                .ToArray(),
            generated.Model,
            generated.Runtime,
            [],
            []);
        return await GenerateAdaptationScriptCommandHandler.SaveDraftAsync(
            dbContext,
            timeProvider,
            command.ProjectId,
            command.SourceResourceId,
            analysis,
            projectSettings.AssetId,
            result,
            result.Episodes.Count,
            command.Instruction.Trim(),
            cancellationToken);
    }
}

public sealed class ConfirmAdaptationScriptCommandHandler(
    V2DbContext dbContext,
    IAdaptationScriptWriter writer,
    TimeProvider timeProvider)
    : ICommandHandler<ConfirmAdaptationScriptCommand, AdaptationScriptView?>
{
    private static readonly ConcurrentDictionary<(Guid ProjectId, Guid SourceResourceId), SemaphoreSlim>
        ConfirmationLocks = new();

    public async Task<AdaptationScriptView?> HandleAsync(
        ConfirmAdaptationScriptCommand command,
        CancellationToken cancellationToken)
    {
        var confirmationLock = ConfirmationLocks.GetOrAdd(
            (command.ProjectId, command.SourceResourceId),
            _ => new SemaphoreSlim(1, 1));
        await confirmationLock.WaitAsync(cancellationToken);
        try
        {
            return await ConfirmAsync(command, cancellationToken);
        }
        finally
        {
            confirmationLock.Release();
        }
    }

    private async Task<AdaptationScriptView?> ConfirmAsync(
        ConfirmAdaptationScriptCommand command,
        CancellationToken cancellationToken)
    {
        var current = await AdaptationScriptQueries.FindCurrentAsync(
            dbContext,
            command.ProjectId,
            command.SourceResourceId,
            cancellationToken);
        if (current.Asset is null || current.State is null) return null;
        var currentDocument = AdaptationScriptQueries.ReadDocument(current.Asset);
        if (currentDocument.Status == "confirmed")
            return await AdaptationScriptQueries.ToViewAsync(
                dbContext,
                current.Asset,
                currentDocument,
                cancellationToken);

        var projectSettings = await new GetProjectSettingsQueryHandler(dbContext).HandleAsync(
            new GetProjectSettingsQuery(command.ProjectId),
            cancellationToken);
        if (projectSettings is null) return null;
        var analysis = await StoryMaterialAnalysisQueries.GetCurrentAsync(
            dbContext,
            command.ProjectId,
            command.SourceResourceId,
            cancellationToken);
        if (analysis is null) return null;
        var productionScripts = new List<ProductionScriptEpisodeDraft>(currentDocument.Episodes.Count);
        foreach (var outline in currentDocument.Episodes)
        {
            productionScripts.Add(await WriteValidatedProductionScriptAsync(
                writer,
                projectSettings,
                analysis,
                outline,
                cancellationToken));
        }

        var now = timeProvider.GetUtcNow();
        var nextEpisodeNumber = (await dbContext.ProductionEpisodes
            .Where(item => item.ProjectId == command.ProjectId)
            .Select(item => (int?)item.EpisodeNumber)
            .MaxAsync(cancellationToken) ?? 0) + 1;
        var episodes = currentDocument.Episodes.Select((draft, index) => new ProductionEpisode
        {
            ProjectId = command.ProjectId,
            EpisodeNumber = nextEpisodeNumber + index,
            Title = draft.Title,
            TargetSeconds = draft.TargetSeconds,
            Status = "draft",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }).ToArray();
        dbContext.ProductionEpisodes.AddRange(episodes);

        var confirmedDocument = currentDocument with
        {
            Status = "confirmed",
            ProductionEpisodeIds = episodes.Select(item => item.Id).ToArray()
        };
        var documentJson = JsonSerializer.Serialize(confirmedDocument, ProjectSourceDefaults.JsonOptions);
        var confirmedAsset = new Asset
        {
            ProjectId = command.ProjectId,
            ProductionEpisodeId = null,
            ResourceId = current.Asset.ResourceId,
            Version = current.Asset.Version + 1,
            Number = current.Asset.Number,
            Type = AdaptationScriptQueries.AssetType,
            Name = current.Asset.Name,
            DocumentJson = documentJson,
            ContentType = "application/json",
            SizeBytes = Encoding.UTF8.GetByteCount(documentJson),
            GenerationMetadataJson = current.Asset.GenerationMetadataJson,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.Assets.Add(confirmedAsset);
        GenerateAdaptationScriptCommandHandler.AddDependency(
            dbContext,
            command.ProjectId,
            confirmedAsset.Id,
            currentDocument.AnalysisAssetId,
            "based-on-analysis",
            now);
        GenerateAdaptationScriptCommandHandler.AddDependency(
            dbContext,
            command.ProjectId,
            confirmedAsset.Id,
            currentDocument.SourceAssetId,
            "reference-source",
            now);
        current.State.CurrentAssetId = confirmedAsset.Id;
        current.State.LifecycleStatus = "confirmed";
        current.State.UpdatedAtUtc = now;

        var nextAssetNumber = (await dbContext.Assets
            .Where(item => item.ProjectId == command.ProjectId)
            .Select(item => (int?)item.Number)
            .MaxAsync(cancellationToken) ?? 0) + 1;
        for (var index = 0; index < episodes.Length; index++)
        {
            var episodeJson = JsonSerializer.Serialize(
                new ProductionScriptPackageDocument(
                    confirmedAsset.Id,
                    episodes[index].Id,
                    productionScripts[index]),
                ProjectSourceDefaults.JsonOptions);
            var scriptAsset = new Asset
            {
                ProjectId = command.ProjectId,
                ProductionEpisodeId = episodes[index].Id,
                ResourceId = Guid.NewGuid(),
                Version = 1,
                Number = nextAssetNumber + index,
                Type = "script-package",
                Name = $"E{episodes[index].EpisodeNumber:D2} · {episodes[index].Title}",
                DocumentJson = episodeJson,
                ContentType = "application/json",
                SizeBytes = Encoding.UTF8.GetByteCount(episodeJson),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            dbContext.Assets.Add(scriptAsset);
            dbContext.ResourceStates.Add(new ResourceState
            {
                ProjectId = command.ProjectId,
                ResourceId = scriptAsset.ResourceId,
                ResourceType = "script-package",
                CurrentAssetId = scriptAsset.Id,
                LifecycleStatus = "active",
                UpdatedAtUtc = now
            });
            GenerateAdaptationScriptCommandHandler.AddDependency(
                dbContext,
                command.ProjectId,
                scriptAsset.Id,
                confirmedAsset.Id,
                "derived-from",
                now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await AdaptationScriptQueries.ToViewAsync(
            dbContext,
            confirmedAsset,
            confirmedDocument,
            cancellationToken);
    }

    internal static async Task<ProductionScriptEpisodeDraft> WriteValidatedProductionScriptAsync(
        IAdaptationScriptWriter writer,
        ProjectSettingsView projectSettings,
        StoryMaterialAnalysisView analysis,
        AdaptationEpisodeDraft outline,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 5;
        ProductionScriptEpisodeDraft? script = null;
        string? correction = null;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            script = await writer.WriteProductionScriptAsync(
                projectSettings,
                analysis,
                outline,
                script,
                correction,
                cancellationToken);
            try
            {
                return NormalizeProductionScript(outline, script);
            }
            catch (InvalidOperationException exception) when (attempt < maximumAttempts)
            {
                correction = exception.Message;
            }
        }

        throw new InvalidOperationException($"第 {outline.ProposalNumber} 集正式剧本生成失败。");
    }

    private static ProductionScriptEpisodeDraft NormalizeProductionScript(
        AdaptationEpisodeDraft outline,
        ProductionScriptEpisodeDraft script)
    {
        if (script.Scenes is not { Count: > 0 })
            throw new InvalidOperationException($"第 {outline.ProposalNumber} 集正式剧本没有场次。");
        if (script.Scenes.Any(scene => string.IsNullOrWhiteSpace(scene.Heading)
            || string.IsNullOrWhiteSpace(scene.Action)
            || scene.TargetSeconds <= 0
            || string.IsNullOrWhiteSpace(scene.Rhythm)
            || string.IsNullOrWhiteSpace(scene.VisualContrast)
            || scene.ShotPlan is not { Count: > 0 }))
            throw new InvalidOperationException($"第 {outline.ProposalNumber} 集正式剧本字段不完整。");

        var shots = script.Scenes.SelectMany(scene => scene.ShotPlan!).ToArray();
        if (shots.Any(shot => shot.DurationSeconds <= 0
            || string.IsNullOrWhiteSpace(shot.ShotSize)
            || string.IsNullOrWhiteSpace(shot.CameraAngle)
            || string.IsNullOrWhiteSpace(shot.CameraMovement)
            || string.IsNullOrWhiteSpace(shot.Purpose)))
            throw new InvalidOperationException($"第 {outline.ProposalNumber} 集正式剧本的镜头计划字段不完整。");

        var durations = NormalizeShotDurations(shots, outline.TargetSeconds, outline.ProposalNumber);
        var durationIndex = 0;
        var normalizedScenes = script.Scenes.Select((scene, sceneIndex) =>
        {
            var normalizedShots = scene.ShotPlan!.Select((shot, shotIndex) => shot with
            {
                ShotNumber = shotIndex + 1,
                DurationSeconds = durations[durationIndex++]
            }).ToArray();
            var normalizedDialogues = (scene.Dialogues ?? [])
                .Where(dialogue => !string.IsNullOrWhiteSpace(dialogue.Character))
                .Select(dialogue => dialogue with
                {
                    Character = dialogue.Character.Trim(),
                    Parenthetical = string.IsNullOrWhiteSpace(dialogue.Parenthetical)
                        ? null
                        : dialogue.Parenthetical.Trim(),
                    Lines = (dialogue.Lines ?? [])
                        .Select(line => line.Trim())
                        .Where(line => line.Length > 0)
                        .ToArray(),
                })
                .Where(dialogue => dialogue.Lines.Count > 0)
                .ToArray();
            return scene with
            {
                SceneNumber = sceneIndex + 1,
                Summary = scene.Summary?.Trim() ?? string.Empty,
                Action = scene.Action.Trim(),
                Dialogues = normalizedDialogues,
                Characters = (scene.Characters ?? [])
                    .Select(item => item.Trim())
                    .Where(item => item.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Props = (scene.Props ?? [])
                    .Select(item => item.Trim())
                    .Where(item => item.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                TargetSeconds = Math.Round(normalizedShots.Sum(shot => shot.DurationSeconds), 1),
                ShotPlan = normalizedShots
            };
        }).ToArray();
        ValidateDialogueTiming(normalizedScenes, outline.ProposalNumber);
        return script with
        {
            Title = outline.Title,
            Logline = outline.Logline,
            TargetSeconds = outline.TargetSeconds,
            SmallHooks = outline.SmallHooks ?? [],
            BigHooks = outline.BigHooks ?? [],
            Scenes = normalizedScenes
        };
    }

    private static void ValidateDialogueTiming(
        IReadOnlyList<ProductionScriptSceneDraft> scenes,
        int episodeNumber)
    {
        const int maximumLineCharacters = 32;
        const double maximumDialogueCharactersPerSecond = 3.2;
        var failures = new List<string>();
        foreach (var scene in scenes)
        {
            var lines = scene.Dialogues.SelectMany(dialogue => dialogue.Lines).ToArray();
            var overlongLineCount = lines.Count(
                line => CountSpokenCharacters(line) > maximumLineCharacters);
            if (overlongLineCount > 0)
                failures.Add($"第 {scene.SceneNumber} 场有 {overlongLineCount} 句超过 {maximumLineCharacters} 字");

            var characterCount = lines.Sum(CountSpokenCharacters);
            var maximumCharacters = (int)Math.Floor(
                scene.TargetSeconds * maximumDialogueCharactersPerSecond);
            if (characterCount > maximumCharacters)
                failures.Add(
                    $"第 {scene.SceneNumber} 场对白 {characterCount} 字，{scene.TargetSeconds:0.#} 秒最多 {maximumCharacters} 字");
        }

        if (failures.Count > 0)
            throw new InvalidOperationException(
                $"第 {episodeNumber} 集对白未通过：{string.Join("；", failures)}。请一次修正全部场次。");
    }

    private static int CountSpokenCharacters(string line) =>
        line.Count(character => !char.IsWhiteSpace(character));

    private static double[] NormalizeShotDurations(
        IReadOnlyList<AdaptationShotPlanDraft> shots,
        int targetSeconds,
        int episodeNumber)
    {
        const int minimumUnits = 5;
        var targetUnits = targetSeconds * 10;
        var roundedUnits = shots
            .Select(shot => (int)Math.Round(shot.DurationSeconds * 10, MidpointRounding.AwayFromZero))
            .ToArray();
        if (roundedUnits.All(unit => unit >= minimumUnits) && roundedUnits.Sum() == targetUnits)
            return roundedUnits.Select(unit => unit / 10d).ToArray();

        if (targetUnits < shots.Count * minimumUnits)
            throw new InvalidOperationException(
                $"第 {episodeNumber} 集镜头数量过多，无法在 {targetSeconds} 秒内保证每镜至少 0.5 秒。");

        var remainingUnits = targetUnits - shots.Count * minimumUnits;
        var totalWeight = shots.Sum(shot => shot.DurationSeconds);
        var shares = shots.Select(shot => remainingUnits * shot.DurationSeconds / totalWeight).ToArray();
        var units = shares.Select(share => minimumUnits + (int)Math.Floor(share)).ToArray();
        var undistributed = targetUnits - units.Sum();
        foreach (var index in shares
            .Select((share, index) => new { index, fraction = share - Math.Floor(share) })
            .OrderByDescending(item => item.fraction)
            .ThenBy(item => item.index)
            .Take(undistributed)
            .Select(item => item.index))
        {
            units[index]++;
        }
        return units.Select(unit => unit / 10d).ToArray();
    }
}

public sealed class RegenerateProductionScriptCommandHandler(
    V2DbContext dbContext,
    IAdaptationScriptWriter writer,
    TimeProvider timeProvider)
    : ICommandHandler<RegenerateProductionScriptCommand, ProductionScriptPackageView?>
{
    public async Task<ProductionScriptPackageView?> HandleAsync(
        RegenerateProductionScriptCommand command,
        CancellationToken cancellationToken)
    {
        var productionEpisode = await dbContext.ProductionEpisodes.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == command.ProductionEpisodeId && item.ProjectId == command.ProjectId,
            cancellationToken);
        if (productionEpisode is null) return null;
        var current = await (
            from state in dbContext.ResourceStates
            join asset in dbContext.Assets on state.CurrentAssetId equals asset.Id
            where state.ProjectId == command.ProjectId
                && state.ResourceType == "script-package"
                && asset.ProductionEpisodeId == command.ProductionEpisodeId
                && asset.Type == "script-package"
            select new { Asset = asset, State = state })
            .SingleOrDefaultAsync(cancellationToken);
        if (current?.Asset.DocumentJson is null) return null;

        var packageDocument = JsonSerializer.Deserialize<ProductionScriptPackageDocument>(
            current.Asset.DocumentJson,
            ProjectSourceDefaults.JsonOptions)
            ?? throw new InvalidOperationException("正式剧本包内容无效。");
        var adaptationAsset = await dbContext.Assets.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == packageDocument.AdaptationScriptAssetId
                && item.ProjectId == command.ProjectId
                && item.Type == AdaptationScriptQueries.AssetType,
            cancellationToken)
            ?? throw new InvalidOperationException("正式剧本关联的改编方案不存在。");
        var adaptation = AdaptationScriptQueries.ReadDocument(adaptationAsset);
        var episodeIndex = adaptation.ProductionEpisodeIds.ToList().IndexOf(command.ProductionEpisodeId);
        var outline = packageDocument.Episode
            ?? (episodeIndex >= 0 && episodeIndex < adaptation.Episodes.Count
                ? adaptation.Episodes[episodeIndex]
                : null)
            ?? throw new InvalidOperationException("无法确定当前生产集对应的改编大纲。");

        var projectSettings = await new GetProjectSettingsQueryHandler(dbContext).HandleAsync(
            new GetProjectSettingsQuery(command.ProjectId),
            cancellationToken)
            ?? throw new InvalidOperationException("项目设定不存在。");
        var analysisAsset = await dbContext.Assets.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == adaptation.AnalysisAssetId
                && item.ProjectId == command.ProjectId
                && item.Type == StoryMaterialAnalysisQueries.AssetType,
            cancellationToken)
            ?? throw new InvalidOperationException("改编方案关联的素材分析不存在。");
        var analysisState = await dbContext.ResourceStates.AsNoTracking().SingleAsync(
            item => item.ProjectId == command.ProjectId
                && item.ResourceId == analysisAsset.ResourceId,
            cancellationToken);
        var analysis = StoryMaterialAnalysisQueries.ToView(
            analysisAsset,
            analysisState,
            StoryMaterialAnalysisQueries.ReadDocument(analysisAsset));
        var script = await ConfirmAdaptationScriptCommandHandler.WriteValidatedProductionScriptAsync(
            writer,
            projectSettings,
            analysis,
            outline,
            cancellationToken);

        var now = timeProvider.GetUtcNow();
        var documentJson = JsonSerializer.Serialize(
            new ProductionScriptPackageDocument(
                adaptationAsset.Id,
                command.ProductionEpisodeId,
                script),
            ProjectSourceDefaults.JsonOptions);
        var regeneratedAsset = new Asset
        {
            ProjectId = command.ProjectId,
            ProductionEpisodeId = command.ProductionEpisodeId,
            ResourceId = current.Asset.ResourceId,
            Version = current.Asset.Version + 1,
            Number = current.Asset.Number,
            Type = "script-package",
            Name = current.Asset.Name,
            DocumentJson = documentJson,
            ContentType = "application/json",
            SizeBytes = Encoding.UTF8.GetByteCount(documentJson),
            GenerationMetadataJson = current.Asset.GenerationMetadataJson,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.Assets.Add(regeneratedAsset);
        GenerateAdaptationScriptCommandHandler.AddDependency(
            dbContext,
            command.ProjectId,
            regeneratedAsset.Id,
            adaptationAsset.Id,
            "derived-from",
            now);
        current.State.CurrentAssetId = regeneratedAsset.Id;
        current.State.LifecycleStatus = "active";
        current.State.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ProductionScriptPackageView(
            regeneratedAsset.Id,
            regeneratedAsset.ResourceId,
            regeneratedAsset.Version,
            command.ProductionEpisodeId,
            productionEpisode.EpisodeNumber,
            productionEpisode.Title,
            productionEpisode.TargetSeconds,
            productionEpisode.Status,
            adaptationAsset.Id,
            false,
            script,
            now);
    }
}

internal static class AdaptationScriptQueries
{
    public const string AssetType = "adaptation-script-draft";

    public static async Task<AdaptationScriptView?> GetCurrentAsync(
        V2DbContext dbContext,
        Guid projectId,
        Guid sourceResourceId,
        CancellationToken cancellationToken)
    {
        var current = await FindCurrentAsync(dbContext, projectId, sourceResourceId, cancellationToken);
        if (current.Asset is null) return null;
        return await ToViewAsync(
            dbContext,
            current.Asset,
            ReadDocument(current.Asset),
            cancellationToken);
    }

    public static async Task<(Asset? Asset, ResourceState? State)> FindCurrentAsync(
        V2DbContext dbContext,
        Guid projectId,
        Guid sourceResourceId,
        CancellationToken cancellationToken)
    {
        var candidates = await (
            from state in dbContext.ResourceStates
            join asset in dbContext.Assets on state.CurrentAssetId equals asset.Id
            where state.ProjectId == projectId
                && state.ResourceType == AssetType
                && asset.Type == AssetType
            select new { Asset = asset, State = state })
            .ToListAsync(cancellationToken);
        var match = candidates.FirstOrDefault(item =>
            ReadDocument(item.Asset).SourceResourceId == sourceResourceId);
        return match is null ? (null, null) : (match.Asset, match.State);
    }

    public static AdaptationScriptDocument ReadDocument(Asset asset)
    {
        var document = JsonSerializer.Deserialize<AdaptationScriptDocument>(
            asset.DocumentJson ?? throw new InvalidOperationException("剧本草案缺少文档内容。"),
            ProjectSourceDefaults.JsonOptions)
            ?? throw new InvalidOperationException("剧本草案内容无效。");
        return document with
        {
            OverallSmallHooks = document.OverallSmallHooks ?? [],
            OverallBigHooks = document.OverallBigHooks ?? [],
            Episodes = document.Episodes.Select(item => item with
            {
                SmallHooks = item.SmallHooks ?? [],
                BigHooks = item.BigHooks ?? []
            }).ToArray()
        };
    }

    public static async Task<AdaptationScriptView> ToViewAsync(
        V2DbContext dbContext,
        Asset asset,
        AdaptationScriptDocument document,
        CancellationToken cancellationToken)
    {
        var sourceState = await dbContext.ResourceStates.AsNoTracking().SingleAsync(
            item => item.ProjectId == asset.ProjectId
                && item.ResourceId == document.SourceResourceId,
            cancellationToken);
        return new(
            asset.Id,
            asset.ResourceId,
            asset.Version,
            document.SourceResourceId,
            document.SourceAssetId,
            document.SourceVersion,
            document.AnalysisAssetId,
            document.Status,
            sourceState.CurrentAssetId != document.SourceAssetId,
            document.Title,
            document.Approach,
            document.OverallSmallHooks ?? [],
            document.OverallBigHooks ?? [],
            document.Episodes,
            document.ProductionEpisodeIds,
            document.Model,
            document.Runtime,
            asset.UpdatedAtUtc);
    }
}

public sealed class StoryDevelopmentConflictException(string message) : InvalidOperationException(message);

#pragma warning disable MAAI001
public sealed class MafAdaptationScriptWriter(
    V2DbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    ILoggerFactory loggerFactory) : IAdaptationScriptWriter
{
    public async Task<AdaptationScriptResult> WriteAsync(
        ProjectSettingsView projectSettings,
        StoryMaterialAnalysisView analysis,
        int desiredEpisodeCount,
        string? instruction,
        CancellationToken cancellationToken)
    {
        var configuration = await dbContext.FoundryConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (configuration is null
            || string.IsNullOrWhiteSpace(configuration.Endpoint)
            || string.IsNullOrWhiteSpace(configuration.ProtectedApiKey))
            throw new ProjectGenerationConfigurationException("请先在系统设置中配置 GPT-5.4。");

        var apiKey = dataProtectionProvider.CreateProtector("FoundryApiKeys.v1")
            .Unprotect(configuration.ProtectedApiKey);
        var agent = AzureFoundryChatClientFactory
            .Create(configuration.Endpoint, configuration.Deployment, apiKey)
            .AsIChatClient()
            .AsHarnessAgent(
                new HarnessAgentOptions
                {
                    Name = "AlexAdaptationScriptWriter",
                    MaxContextWindowTokens = 1_050_000,
                    MaxOutputTokens = 16_384,
                    MaximumIterationsPerRequest = 6,
                    DisableFileMemory = true,
                    DisableWebSearch = true,
                    DisableTodoProvider = true,
                    DisableAgentModeProvider = true,
                    DisableAgentSkillsProvider = true,
                    ChatOptions = new ChatOptions
                    {
                        Instructions = """
                            你是网剧改编策划。根据素材图谱识别原故事主线，再结合网剧的受众、单集时长和追看节奏，整理成新的改编主线与分集大纲。
                            原文只是人物、世界和事件素材，不是待照搬的剧本。不要求一章对应一集；应跨章节重排、合并和删减不必要的支线，也可以补充维持因果、冲突和人物动机所需的原创连接，但不得改变项目核心人物身份。
                            当前阶段只输出大纲：每集列出按顺序发生的剧情节点、节点功能、涉及人物与关键道具。不写正式对白、动作剧本、摄影参数或镜头计划。
                            必须遵守项目设定的内容类型、受众、单集时长和创作方向。
                            同时分析节奏爆点：smallHooks 是推动继续观看的局部悬念、反转或情绪点；bigHooks 是改变局势、揭示核心秘密或形成集尾追看动力的大爆点。每个爆点只属于一个剧集，必须是该集内实际发生的具体事件，不得跨剧集复用或概括。
                            全部正文使用简体中文，专有名称使用通行中文译名。
                            只返回 JSON，不要 Markdown 围栏。结构必须为：
                            {"title":"...","approach":"原故事主线、删减/合并/补充原则与新主线说明","overallSmallHooks":[],"overallBigHooks":[],"episodes":[{"proposalNumber":1,"title":"...","logline":"本集大纲主线","targetSeconds":100,"sourceChapterNumbers":[1,2],"smallHooks":["..."],"bigHooks":["..."],"scenes":[{"sceneNumber":1,"heading":"大纲节点标题","summary":"按因果描述本节点发生的剧情","characters":["..."],"props":["..."],"storyFunction":"本节点在新主线中的作用","dialogueNotes":""}]}]}
                            """,
                        MaxOutputTokens = 16_384
                    }
                },
                loggerFactory);
        var input = JsonSerializer.Serialize(
            new
            {
                projectSettings = new
                {
                    projectSettings.ProjectName,
                    projectSettings.Description,
                    projectSettings.ContentType,
                    projectSettings.TargetAudience,
                    projectSettings.PlannedEpisodeCount,
                    projectSettings.TargetEpisodeSeconds,
                    projectSettings.VisualStyle,
                    projectSettings.ArtDirection,
                    projectSettings.ProtagonistSpecies,
                    projectSettings.CharacterDesign,
                    projectSettings.CameraLanguage,
                    projectSettings.SoundStrategy
                },
                desiredEpisodeCount,
                instruction = instruction?.Trim(),
                analysis = new
                {
                    analysis.Summary,
                    analysis.Characters,
                    analysis.Locations,
                    analysis.PlotBeats,
                    analysis.Relations
                }
            },
            ProjectSourceDefaults.JsonOptions);
        var response = await agent.RunAsync(
            $"生成恰好 {desiredEpisodeCount} 集改编大纲：\n{input}",
            cancellationToken: cancellationToken);
        var json = ExtractJson(response.Text);
        var payload = JsonSerializer.Deserialize<AdaptationScriptPayload>(
            json,
            ProjectSourceDefaults.JsonOptions)
            ?? throw new InvalidOperationException("GPT-5.4 未返回有效的改编大纲。");
        if (payload.Episodes.Count == 0)
            throw new InvalidOperationException("GPT-5.4 返回的改编大纲没有剧集。");
        return new(
            payload.Title,
            payload.Approach,
            payload.Episodes,
            configuration.Deployment,
            "MAF HarnessAgent",
            payload.OverallSmallHooks,
            payload.OverallBigHooks);
    }

    public async Task<ProductionScriptEpisodeDraft> WriteProductionScriptAsync(
        ProjectSettingsView projectSettings,
        StoryMaterialAnalysisView analysis,
        AdaptationEpisodeDraft outline,
        ProductionScriptEpisodeDraft? previousScript,
        string? correction,
        CancellationToken cancellationToken)
    {
        var configuration = await dbContext.FoundryConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (configuration is null
            || string.IsNullOrWhiteSpace(configuration.Endpoint)
            || string.IsNullOrWhiteSpace(configuration.ProtectedApiKey))
            throw new ProjectGenerationConfigurationException("请先在系统设置中配置 GPT-5.4。");

        var apiKey = dataProtectionProvider.CreateProtector("FoundryApiKeys.v1")
            .Unprotect(configuration.ProtectedApiKey);
        var agent = AzureFoundryChatClientFactory
            .Create(configuration.Endpoint, configuration.Deployment, apiKey)
            .AsIChatClient()
            .AsHarnessAgent(
                new HarnessAgentOptions
                {
                    Name = "AlexProductionScriptWriter",
                    MaxContextWindowTokens = 1_050_000,
                    MaxOutputTokens = 24_576,
                    MaximumIterationsPerRequest = 6,
                    DisableFileMemory = true,
                    DisableWebSearch = true,
                    DisableTodoProvider = true,
                    DisableAgentModeProvider = true,
                    DisableAgentSkillsProvider = true,
                    ChatOptions = new ChatOptions
                    {
                        Instructions = """
                            你是专业影视剧编剧。根据已经确定的单集改编大纲，重新编写可交付的正式剧本，不得把大纲摘要原样当作剧本。
                            使用标准影视剧表达：每场有“内/外景 地点 时间”场景标题、可拍摄的动作描述，以及按角色分组的对白；对白包含角色名、可选表演提示和逐句台词。用动作和对白实际演出大纲中的冲突、转折和人物选择。
                            dialogues 必须严格按实际说话顺序排列，一个 item 只表示一次连续发言；人物再次开口时必须创建新的 item，不得把同一角色在整场中的台词集中到一起。character 必须是场内真实说话者，禁止使用“对白说明”“台词提示”等占位角色。
                            台词要短、自然、可说出口，服务人物当下目的和对手反应。不得复述画面、解释观众已经知道的设定、代替作者总结剧情或堆砌空泛金句；语言应符合人物身份、时代和处境，不使用网络梗。parenthetical 只写无法从台词推断的简短表演提示，不写动作段落。
                            按正常中文对白每秒约 4 个字符估算，每场至少保留 35% 时长给动作、反应和停顿。每场 lines 全部字符数不得超过 scene.targetSeconds * 2.6；每句优选 8 到 20 字，绝对不得超过 32 字，不得靠机械拆句规避限制。
                            返回前必须逐场统计 lines 的全部字符数并自行删改到预算以内。如果输入包含 correction，表示上一版未通过硬校验；错误消息中每个“最多 N 字”都是硬上限，必须把对应场次压到不超过 N-8 字，不能只删一两字或原样重交。
                            如果输入包含 previousScript，必须进入定向返修模式：完整保留上一版的 title、logline、场次数量、heading、summary、action、characters、props、storyFunction、targetSeconds、rhythm、visualContrast、shotPlan、smallHooks 和 bigHooks，只按 correction 重写 dialogues。仍须返回完整 JSON，禁止借返修改动剧情、动作、时长或摄影骨架。
                            严格遵循大纲主线与爆点，不重新引入已删支线；可以补充完成场景连接和人物动机所需的动作与对白，但不能改变核心事件结果。
                            同时为下游分镜给出最小摄影骨架：每场 targetSeconds、rhythm、visualContrast 和 shotPlan。镜号连续，镜头总时长等于单集目标时长；分镜只在此基础上细化构图、画面、动作、对白和声音。
                            全部正文使用简体中文。只返回 JSON，不要 Markdown 围栏。结构必须为：
                            {"title":"...","logline":"...","targetSeconds":100,"smallHooks":["..."],"bigHooks":["..."],"scenes":[{"sceneNumber":1,"heading":"外景 巴黎街道 日","summary":"本场剧情摘要","action":"现在时、可拍摄的连续动作描述","dialogues":[{"character":"达达尼昂","parenthetical":"压低声音","lines":["第一句台词。","第二句台词。"]}],"characters":["达达尼昂"],"props":["推荐信"],"storyFunction":"推进冲突","targetSeconds":20,"rhythm":"...","visualContrast":"...","shotPlan":[{"shotNumber":1,"durationSeconds":5,"shotSize":"全景","cameraAngle":"平视","cameraMovement":"固定","purpose":"建立空间"}]}]}
                            """,
                        MaxOutputTokens = 24_576
                    }
                },
                loggerFactory);
        var input = JsonSerializer.Serialize(new
        {
            projectSettings = new
            {
                projectSettings.ContentType,
                projectSettings.TargetAudience,
                projectSettings.TargetEpisodeSeconds,
                projectSettings.VisualStyle,
                projectSettings.ArtDirection,
                projectSettings.CameraLanguage,
                projectSettings.SoundStrategy
            },
            storyGraph = new { analysis.Summary, analysis.Characters, analysis.Relations },
            deliveryConstraints = new
            {
                EstimatedChineseCharactersPerSecond = 4,
                MaximumDialogueShare = .65,
                MaximumEpisodeDialogueCharacters = (int)Math.Floor(outline.TargetSeconds * 2.6),
                PreferredLineCharacters = "8-20",
                AbsoluteMaximumLineCharacters = 32
            },
            correction = string.IsNullOrWhiteSpace(correction)
                ? null
                : new
                {
                    Failure = correction,
                    RequiredAction = "只重写上一版的dialogues；错误中每个最多N字的场次都必须压到N-8字以内。"
                },
            previousScript = string.IsNullOrWhiteSpace(correction) ? null : previousScript,
            outline
        }, ProjectSourceDefaults.JsonOptions);
        var response = await agent.RunAsync(
            $"把以下第 {outline.ProposalNumber} 集大纲写成正式影视剧本：\n{input}",
            cancellationToken: cancellationToken);
        var json = ExtractJson(response.Text);
        return JsonSerializer.Deserialize<ProductionScriptEpisodeDraft>(json, ProjectSourceDefaults.JsonOptions)
            ?? throw new InvalidOperationException("GPT-5.4 未返回有效的正式剧本。");
    }

    private static string ExtractJson(string? response)
    {
        var text = response?.Trim() ?? string.Empty;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidOperationException("GPT-5.4 未返回 JSON 内容。");
        return text[start..(end + 1)];
    }

    private sealed class AdaptationScriptPayload
    {
        public string Title { get; set; } = string.Empty;
        public string Approach { get; set; } = string.Empty;
        public List<string> OverallSmallHooks { get; set; } = [];
        public List<string> OverallBigHooks { get; set; } = [];
        public List<AdaptationEpisodeDraft> Episodes { get; set; } = [];
    }
}
#pragma warning restore MAAI001

public static class AdaptationScriptEndpoints
{
    public static IEndpointRouteBuilder MapAdaptationScripts(this IEndpointRouteBuilder app)
    {
        var route = "/api/v2/projects/{projectId:guid}/sources/{sourceResourceId:guid}/script-draft";
        app.MapGet(
            "/api/v2/projects/{projectId:guid}/production-episodes/{productionEpisodeId:guid}/script-package",
            async (
                Guid projectId,
                Guid productionEpisodeId,
                IQueryDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                var package = await dispatcher.QueryAsync(
                    new GetProductionScriptPackageQuery(projectId, productionEpisodeId),
                    cancellationToken);
                return package is null ? Results.NotFound() : Results.Ok(package);
            });
        app.MapPost(
            "/api/v2/projects/{projectId:guid}/production-episodes/{productionEpisodeId:guid}/script-package/regenerate",
            async (
                Guid projectId,
                Guid productionEpisodeId,
                ICommandDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var package = await dispatcher.SendAsync(
                        new RegenerateProductionScriptCommand(projectId, productionEpisodeId),
                        cancellationToken);
                    return package is null ? Results.NotFound() : Results.Ok(package);
                }
                catch (ProjectGenerationConfigurationException error)
                {
                    return Results.Conflict(new { error = error.Message });
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    return Results.Problem(
                        title: "正式剧本重新生成失败",
                        detail: error.Message,
                        statusCode: StatusCodes.Status502BadGateway);
                }
            });
        app.MapGet(route, async (
            Guid projectId,
            Guid sourceResourceId,
            IQueryDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            var script = await dispatcher.QueryAsync(
                new GetAdaptationScriptQuery(projectId, sourceResourceId),
                cancellationToken);
            return script is null ? Results.NotFound() : Results.Ok(script);
        });
        app.MapPost(route, async (
            Guid projectId,
            Guid sourceResourceId,
            GenerateAdaptationScriptRequest request,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var script = await dispatcher.SendAsync(
                    new GenerateAdaptationScriptCommand(
                        projectId,
                        sourceResourceId,
                        request.DesiredEpisodeCount,
                        request.Instruction),
                    cancellationToken);
                return script is null ? Results.NotFound() : Results.Ok(script);
            }
            catch (ArgumentException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
            catch (StoryDevelopmentConflictException error)
            {
                return Results.Conflict(new { error = error.Message });
            }
            catch (ProjectGenerationConfigurationException error)
            {
                return Results.Conflict(new { error = error.Message });
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return Results.Problem(
                    title: "改编大纲生成失败",
                    detail: error.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });
        app.MapPost($"{route}/confirm", async (
            Guid projectId,
            Guid sourceResourceId,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var script = await dispatcher.SendAsync(
                    new ConfirmAdaptationScriptCommand(projectId, sourceResourceId),
                    cancellationToken);
                return script is null ? Results.NotFound() : Results.Ok(script);
            }
            catch (ProjectGenerationConfigurationException error)
            {
                return Results.Conflict(new { error = error.Message });
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return Results.Problem(
                    title: "正式剧本生成失败",
                    detail: error.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });
        app.MapPost($"{route}/episodes", async (
            Guid projectId,
            Guid sourceResourceId,
            AppendAdaptationEpisodeRequest request,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var script = await dispatcher.SendAsync(
                    new AppendAdaptationEpisodeCommand(projectId, sourceResourceId, request.Instruction),
                    cancellationToken);
                return script is null ? Results.NotFound() : Results.Ok(script);
            }
            catch (ArgumentException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
            catch (StoryDevelopmentConflictException error)
            {
                return Results.Conflict(new { error = error.Message });
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return Results.Problem(
                    title: "添加剧集失败",
                    detail: error.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });
        app.MapPost($"{route}/episodes/{{episodeNumber:int}}/regenerate", async (
            Guid projectId,
            Guid sourceResourceId,
            int episodeNumber,
            RegenerateAdaptationEpisodeRequest request,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var script = await dispatcher.SendAsync(
                    new RegenerateAdaptationEpisodeCommand(
                        projectId,
                        sourceResourceId,
                        episodeNumber,
                        request.Instruction),
                    cancellationToken);
                return script is null ? Results.NotFound() : Results.Ok(script);
            }
            catch (ArgumentException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
            catch (StoryDevelopmentConflictException error)
            {
                return Results.Conflict(new { error = error.Message });
            }
            catch (ProjectGenerationConfigurationException error)
            {
                return Results.Conflict(new { error = error.Message });
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return Results.Problem(
                    title: "重新生成剧集失败",
                    detail: error.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });
        return app;
    }
}

public sealed record GenerateAdaptationScriptRequest(
    int? DesiredEpisodeCount,
    string? Instruction);

public sealed record AppendAdaptationEpisodeRequest(string? Instruction);

public sealed record RegenerateAdaptationEpisodeRequest(string Instruction);
