using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Agents;
using AlexDirectorConsole.V2.Api.Features.Generation;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Sources;

public static class AdaptationModes
{
    public const string SourceChapters = "source-chapters";
    public const string Rearranged = "rearranged";
}

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
    DateTimeOffset UpdatedAtUtc,
    string Mode = "rearranged",
    IReadOnlyDictionary<int, Guid>? ProductionEpisodeMap = null);

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
    IReadOnlyList<string>? OverallBigHooks = null,
    string Mode = "rearranged",
    IReadOnlyDictionary<int, Guid>? ProductionEpisodeMap = null);

internal sealed record ProductionScriptPackageDocument(
    Guid AdaptationScriptAssetId,
    Guid ProductionEpisodeId,
    ProductionScriptEpisodeDraft? Script = null,
    AdaptationEpisodeDraft? Episode = null);

public sealed record ProductionScriptPackageView(
    Guid AssetId,
    Guid ResourceId,
    int Version,
    Guid SourceResourceId,
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
        ProjectSourceView source,
        StoryMaterialAnalysisView analysis,
        int? desiredEpisodeCount,
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
    string Mode,
    int? DesiredEpisodeCount,
    string? Instruction) : ICommand<AdaptationScriptView?>;

public sealed record AppendAdaptationEpisodeCommand(
    Guid ProjectId,
    Guid SourceResourceId,
    int Count,
    string? Instruction) : ICommand<AdaptationScriptView?>;

public sealed record RegenerateAdaptationEpisodeCommand(
    Guid ProjectId,
    Guid SourceResourceId,
    int EpisodeNumber,
    string Instruction) : ICommand<AdaptationScriptView?>;

public sealed record UpdateAdaptationEpisodeCommand(
    Guid ProjectId,
    Guid SourceResourceId,
    int EpisodeNumber,
    string Title,
    string Logline,
    IReadOnlyList<string> SceneSummaries) : ICommand<AdaptationScriptView?>;

public sealed record DeleteAdaptationEpisodeCommand(
    Guid ProjectId,
    Guid SourceResourceId,
    int EpisodeNumber) : ICommand<AdaptationScriptView?>;

public sealed record ClearAdaptationEpisodesCommand(
    Guid ProjectId,
    Guid SourceResourceId) : ICommand<AdaptationScriptView?>;

public sealed record ConfirmAdaptationScriptCommand(
    Guid ProjectId,
    Guid SourceResourceId,
    int? EpisodeNumber = null)
    : ICommand<AdaptationScriptView?>;

public sealed record RegenerateProductionScriptCommand(Guid ProjectId, Guid ProductionEpisodeId)
    : ICommand<ProductionScriptPackageView?>;

public sealed record UpdateProductionScriptSceneCommand(
    Guid ProjectId,
    Guid ProductionEpisodeId,
    int SceneNumber,
    ProductionScriptSceneDraft Scene)
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
        var adaptationAsset = document is null
            ? null
            : await dbContext.Assets.AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == document.AdaptationScriptAssetId
                    && item.ProjectId == query.ProjectId
                    && item.Type == AdaptationScriptQueries.AssetType,
                cancellationToken);
        return document is null || script is null || adaptationAsset is null
            ? null
            : new(
                asset.Id,
                asset.ResourceId,
                asset.Version,
                AdaptationScriptQueries.ReadDocument(adaptationAsset).SourceResourceId,
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
        if (command.Mode is not (AdaptationModes.SourceChapters or AdaptationModes.Rearranged))
            throw new ArgumentException("请选择按原章节改编或重新编排章节。", nameof(command));
        var source = await new GetProjectSourceQueryHandler(dbContext).HandleAsync(
            new GetProjectSourceQuery(command.ProjectId, command.SourceResourceId),
            cancellationToken);
        if (source is null) return null;
        int? desiredEpisodeCount = command.Mode == AdaptationModes.SourceChapters
            ? source.Chapters.Count
            : command.DesiredEpisodeCount
                ?? (projectSettings.PlannedEpisodeCount == -1
                    ? null
                    : Math.Min(projectSettings.PlannedEpisodeCount, 6));
        if (command.Mode == AdaptationModes.Rearranged
            && desiredEpisodeCount is < 1 or > 6)
            throw new ArgumentException("剧集大纲每次必须生成 1 至 6 集。", nameof(command));
        var analysis = command.Mode == AdaptationModes.SourceChapters
            ? CreateSourceOnlyAnalysis(source)
            : await StoryMaterialAnalysisQueries.GetCurrentAsync(
                dbContext,
                command.ProjectId,
                command.SourceResourceId,
                cancellationToken);
        if (analysis is null) return null;
        if (command.Mode == AdaptationModes.Rearranged && analysis.IsStale)
            throw new StoryDevelopmentConflictException("原文已有新版本，请先重新分析素材，再生成新的改编大纲。");

        var current = await AdaptationScriptQueries.FindCurrentAsync(
            dbContext,
            command.ProjectId,
            command.SourceResourceId,
            cancellationToken);
        var existingOutline = current.Asset is null
            ? null
            : string.Join("；", AdaptationScriptQueries.ReadDocument(current.Asset).Episodes.Select(item =>
                $"E{item.ProposalNumber:D2}《{item.Title}》：{item.Logline}"));
        var generationInstruction = string.IsNullOrWhiteSpace(existingOutline)
            ? command.Instruction
            : $"基于现有大纲生成新版本，保留合理内容并按当前故事、图谱和意见调整。现有大纲：{existingOutline}。用户意见：{command.Instruction}";
        var result = command.Mode == AdaptationModes.SourceChapters
            ? CreateSourceChapterResult(source, projectSettings)
            : await writer.WriteAsync(
                projectSettings,
                source,
                analysis,
                desiredEpisodeCount,
                generationInstruction,
                cancellationToken);
        if (result.Episodes.Count is < 1 or > 1000)
            throw new InvalidOperationException($"GPT-5.4 应返回 1 至 1000 集，实际返回 {result.Episodes.Count} 集。");
        if (desiredEpisodeCount.HasValue && result.Episodes.Count != desiredEpisodeCount.Value)
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
            command.Mode,
            null,
            cancellationToken);
    }

    private static AdaptationScriptResult CreateSourceChapterResult(
        ProjectSourceView source,
        ProjectSettingsView projectSettings) => new(
        source.Title,
        "按原文章节顺序改编；每章直接作为一集的内容依据，不重新编排章节，不规划大小爆点。",
        source.Chapters.Select((chapter, index) => new AdaptationEpisodeDraft(
            index + 1,
            chapter.Title,
            chapter.Content.ReplaceLineEndings(" ").Trim() is var content && content.Length > 160
                ? $"{content[..160]}…"
                : content,
            projectSettings.TargetEpisodeSeconds,
            [chapter.Number],
            [new AdaptationSceneDraft(
                1,
                chapter.Title,
                chapter.Content,
                [],
                [],
                "保留原章节顺序与内容",
                string.Empty)],
            [],
            [])).ToArray(),
        "source-chapters",
        "Deterministic chapter mapping",
        [],
        []);

    internal static StoryMaterialAnalysisView CreateSourceOnlyAnalysis(ProjectSourceView source) => new(
        Guid.Empty,
        Guid.Empty,
        0,
        source.Id,
        source.AssetId,
        source.Version,
        false,
        null,
        source.Description ?? source.Title,
        [],
        [],
        [],
        [],
        source.Chapters.Select(item => item.Id).ToArray(),
        "source-chapters",
        "Direct source chapters",
        source.UpdatedAtUtc);

    internal static async Task<StoryMaterialAnalysisView?> LoadSourceOnlyAnalysisAsync(
        V2DbContext dbContext,
        Guid projectId,
        Guid sourceAssetId,
        CancellationToken cancellationToken)
    {
        var sourceAsset = await dbContext.Assets.AsNoTracking().SingleOrDefaultAsync(
            item => item.ProjectId == projectId
                && item.Id == sourceAssetId
                && item.Type == ProjectSourceDefaults.AssetType,
            cancellationToken);
        return sourceAsset is null
            ? null
            : CreateSourceOnlyAnalysis(ProjectSourceMapper.ToView(sourceAsset));
    }

    internal static async Task<AdaptationScriptView> SaveDraftAsync(
        V2DbContext dbContext,
        TimeProvider timeProvider,
        Guid projectId,
        Guid sourceResourceId,
        StoryMaterialAnalysisView analysis,
        Guid? projectSettingsAssetId,
        AdaptationScriptResult result,
        int? requestedEpisodeCount,
        string? instruction,
        string mode,
        IReadOnlyDictionary<int, Guid>? productionEpisodeMap,
        CancellationToken cancellationToken)
    {
        var previous = await AdaptationScriptQueries.FindCurrentAsync(
            dbContext,
            projectId,
            sourceResourceId,
            cancellationToken);
        var normalizedEpisodes = result.Episodes
            .Select(NormalizeOutline)
            .ToArray();
        var retainedProductionEpisodeMap = new Dictionary<int, Guid>(
            (productionEpisodeMap ?? new Dictionary<int, Guid>())
                .Where(item => normalizedEpisodes.Any(
                    episode => episode.ProposalNumber == item.Key)));
        var document = new AdaptationScriptDocument(
            sourceResourceId,
            analysis.SourceAssetId,
            analysis.SourceVersion,
            analysis.AssetId,
            "draft",
            result.Title,
            result.Approach,
            normalizedEpisodes,
            normalizedEpisodes
                .Where(item => retainedProductionEpisodeMap.ContainsKey(item.ProposalNumber))
                .Select(item => retainedProductionEpisodeMap[item.ProposalNumber])
                .ToArray(),
            result.Model,
            result.Runtime,
            result.OverallSmallHooks ?? [],
            result.OverallBigHooks ?? [],
            mode,
            retainedProductionEpisodeMap);
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
                new
                {
                    result.Model,
                    result.Runtime,
                    requestedEpisodeCount,
                    actualEpisodeCount = normalizedEpisodes.Length,
                    instruction
                },
                ProjectSourceDefaults.JsonOptions),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.Assets.Add(asset);
        if (analysis.AssetId != Guid.Empty)
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
        if (string.IsNullOrWhiteSpace(episode.Title)
            || string.IsNullOrWhiteSpace(episode.Logline)
            || episode.TargetSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"第 {episode.ProposalNumber} 集缺少标题、故事线或有效目标时长。");
        }
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
        if (currentDocument.Mode != AdaptationModes.Rearranged)
            throw new StoryDevelopmentConflictException("按原章节改编不需要续写大纲。");
        if (command.Count is < 1 or > 6)
            throw new ArgumentException("每次可以继续生成 1 至 6 集。", nameof(command));
        if (currentDocument.Episodes.Count >= 1000)
            throw new ArgumentException("单份改编草案最多包含 1000 集。", nameof(command));

        var projectSettings = await new GetProjectSettingsQueryHandler(dbContext).HandleAsync(
            new GetProjectSettingsQuery(command.ProjectId),
            cancellationToken);
        if (projectSettings is null) return null;
        var source = await new GetProjectSourceQueryHandler(dbContext).HandleAsync(
            new GetProjectSourceQuery(command.ProjectId, command.SourceResourceId),
            cancellationToken);
        if (source is null) return null;
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
        var appendInstruction = $"继续生成现有草案之后的第 {nextNumber} 至 {nextNumber + command.Count - 1} 集，不要重写已有剧集。已有分集：{existingOutline}。用户意见：{command.Instruction}";
        var generated = await writer.WriteAsync(
            projectSettings,
            source,
            analysis,
            command.Count,
            appendInstruction,
            cancellationToken);
        if (generated.Episodes.Count != command.Count)
            throw new InvalidOperationException($"剧集大纲 Agent 本批应返回 {command.Count} 集，实际返回 {generated.Episodes.Count} 集。");

        var appendedEpisodes = generated.Episodes.Select((episode, index) =>
            episode with { ProposalNumber = nextNumber + index });
        var result = new AdaptationScriptResult(
            currentDocument.Title,
            currentDocument.Approach,
            [.. currentDocument.Episodes, .. appendedEpisodes],
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
            currentDocument.Mode,
            currentDocument.ProductionEpisodeMap,
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
        if (currentDocument.Mode != AdaptationModes.Rearranged)
            throw new StoryDevelopmentConflictException("按原章节改编直接使用原文，不生成单集大纲。");
        var targetEpisode = currentDocument.Episodes.SingleOrDefault(
            item => item.ProposalNumber == command.EpisodeNumber);
        if (targetEpisode is null)
            throw new ArgumentException($"草案中不存在第 {command.EpisodeNumber} 集。", nameof(command));

        var projectSettings = await new GetProjectSettingsQueryHandler(dbContext).HandleAsync(
            new GetProjectSettingsQuery(command.ProjectId),
            cancellationToken);
        if (projectSettings is null) return null;
        var source = await new GetProjectSourceQueryHandler(dbContext).HandleAsync(
            new GetProjectSourceQuery(command.ProjectId, command.SourceResourceId),
            cancellationToken);
        if (source is null) return null;
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
            source,
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
            currentDocument.Mode,
            currentDocument.ProductionEpisodeMap,
            cancellationToken);
    }
}

public sealed class DeleteAdaptationEpisodeCommandHandler(
    V2DbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<DeleteAdaptationEpisodeCommand, AdaptationScriptView?>
{
    public async Task<AdaptationScriptView?> HandleAsync(
        DeleteAdaptationEpisodeCommand command,
        CancellationToken cancellationToken)
    {
        var current = await AdaptationScriptQueries.FindCurrentAsync(
            dbContext,
            command.ProjectId,
            command.SourceResourceId,
            cancellationToken);
        if (current.Asset is null) return null;
        var document = AdaptationScriptQueries.ReadDocument(current.Asset);
        if (!document.Episodes.Any(item => item.ProposalNumber == command.EpisodeNumber))
            throw new ArgumentException($"草案中不存在第 {command.EpisodeNumber} 集。", nameof(command));

        var settings = await new GetProjectSettingsQueryHandler(dbContext).HandleAsync(
            new GetProjectSettingsQuery(command.ProjectId),
            cancellationToken);
        var analysis = document.Mode == AdaptationModes.SourceChapters
            ? await GenerateAdaptationScriptCommandHandler.LoadSourceOnlyAnalysisAsync(
                dbContext,
                command.ProjectId,
                document.SourceAssetId,
                cancellationToken)
            : await StoryMaterialAnalysisQueries.GetCurrentAsync(
                dbContext,
                command.ProjectId,
                command.SourceResourceId,
                cancellationToken);
        if (settings is null || analysis is null) return null;
        var episodes = document.Episodes
            .Where(item => item.ProposalNumber != command.EpisodeNumber)
            .Select((item, index) => item with { ProposalNumber = index + 1 })
            .ToArray();
        var result = new AdaptationScriptResult(
            document.Title,
            document.Approach,
            episodes,
            document.Model,
            document.Runtime,
            [],
            []);
        var productionEpisodeMap = document.ProductionEpisodeMap?
            .Where(item => item.Key != command.EpisodeNumber)
            .ToDictionary(
                item => item.Key > command.EpisodeNumber ? item.Key - 1 : item.Key,
                item => item.Value);
        return await GenerateAdaptationScriptCommandHandler.SaveDraftAsync(
            dbContext,
            timeProvider,
            command.ProjectId,
            command.SourceResourceId,
            analysis,
            settings.AssetId,
            result,
            episodes.Length,
            $"删除第 {command.EpisodeNumber} 集",
            document.Mode,
            productionEpisodeMap,
            cancellationToken);
    }
}

public sealed class UpdateAdaptationEpisodeCommandHandler(
    V2DbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<UpdateAdaptationEpisodeCommand, AdaptationScriptView?>
{
    public async Task<AdaptationScriptView?> HandleAsync(
        UpdateAdaptationEpisodeCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Title))
            throw new ArgumentException("章节标题不能为空。", nameof(command));
        if (string.IsNullOrWhiteSpace(command.Logline))
            throw new ArgumentException("章节概要不能为空。", nameof(command));

        var current = await AdaptationScriptQueries.FindCurrentAsync(
            dbContext,
            command.ProjectId,
            command.SourceResourceId,
            cancellationToken);
        if (current.Asset is null) return null;
        var document = AdaptationScriptQueries.ReadDocument(current.Asset);
        var target = document.Episodes.SingleOrDefault(
            item => item.ProposalNumber == command.EpisodeNumber);
        if (target is null)
            throw new ArgumentException($"方案中不存在第 {command.EpisodeNumber} 章。", nameof(command));
        if (command.SceneSummaries.Count != target.Scenes.Count
            || command.SceneSummaries.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("每个剧情节点都必须填写内容。", nameof(command));

        var replacement = target with
        {
            Title = command.Title.Trim(),
            Logline = command.Logline.Trim(),
            Scenes = target.Scenes.Select((scene, index) => scene with
            {
                Summary = command.SceneSummaries[index].Trim()
            }).ToArray()
        };
        var result = new AdaptationScriptResult(
            document.Title,
            document.Approach,
            document.Episodes.Select(item => item.ProposalNumber == command.EpisodeNumber
                ? replacement
                : item).ToArray(),
            document.Model,
            document.Runtime,
            [],
            []);
        return await SaveEditedAsync(
            dbContext,
            timeProvider,
            command.ProjectId,
            command.SourceResourceId,
            document,
            result,
            $"手工修改第 {command.EpisodeNumber} 章",
            cancellationToken);
    }

    internal static async Task<AdaptationScriptView?> SaveEditedAsync(
        V2DbContext dbContext,
        TimeProvider timeProvider,
        Guid projectId,
        Guid sourceResourceId,
        AdaptationScriptDocument document,
        AdaptationScriptResult result,
        string instruction,
        CancellationToken cancellationToken)
    {
        var settings = await new GetProjectSettingsQueryHandler(dbContext).HandleAsync(
            new GetProjectSettingsQuery(projectId),
            cancellationToken);
        var analysis = document.Mode == AdaptationModes.SourceChapters
            ? await GenerateAdaptationScriptCommandHandler.LoadSourceOnlyAnalysisAsync(
                dbContext,
                projectId,
                document.SourceAssetId,
                cancellationToken)
            : await StoryMaterialAnalysisQueries.GetCurrentAsync(
                dbContext,
                projectId,
                sourceResourceId,
                cancellationToken);
        if (settings is null || analysis is null) return null;
        return await GenerateAdaptationScriptCommandHandler.SaveDraftAsync(
            dbContext,
            timeProvider,
            projectId,
            sourceResourceId,
            analysis,
            settings.AssetId,
            result,
            result.Episodes.Count,
            instruction,
            document.Mode,
            document.ProductionEpisodeMap,
            cancellationToken);
    }
}

public sealed class ClearAdaptationEpisodesCommandHandler(
    V2DbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<ClearAdaptationEpisodesCommand, AdaptationScriptView?>
{
    public async Task<AdaptationScriptView?> HandleAsync(
        ClearAdaptationEpisodesCommand command,
        CancellationToken cancellationToken)
    {
        var current = await AdaptationScriptQueries.FindCurrentAsync(
            dbContext,
            command.ProjectId,
            command.SourceResourceId,
            cancellationToken);
        if (current.Asset is null) return null;
        var document = AdaptationScriptQueries.ReadDocument(current.Asset);
        if (document.Episodes.Count == 0)
            return await AdaptationScriptQueries.ToViewAsync(
                dbContext,
                current.Asset,
                document,
                cancellationToken);

        var result = new AdaptationScriptResult(
            document.Title,
            document.Approach,
            [],
            document.Model,
            document.Runtime,
            [],
            []);
        return await UpdateAdaptationEpisodeCommandHandler.SaveEditedAsync(
            dbContext,
            timeProvider,
            command.ProjectId,
            command.SourceResourceId,
            document,
            result,
            "清空改编方案",
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
        var productionEpisodeMap = new Dictionary<int, Guid>(
            currentDocument.ProductionEpisodeMap ?? new Dictionary<int, Guid>());
        var requestedOutline = command.EpisodeNumber.HasValue
            ? currentDocument.Episodes.SingleOrDefault(
                item => item.ProposalNumber == command.EpisodeNumber.Value)
            : null;
        if (command.EpisodeNumber.HasValue && requestedOutline is null)
            throw new ArgumentException($"方案中不存在第 {command.EpisodeNumber} 集。", nameof(command));
        var targetOutlines = requestedOutline is null
            ? currentDocument.Episodes
                .Where(item => !productionEpisodeMap.ContainsKey(item.ProposalNumber))
                .ToArray()
            : productionEpisodeMap.ContainsKey(requestedOutline.ProposalNumber)
                ? []
                : [requestedOutline];
        if (targetOutlines.Length == 0)
            return await AdaptationScriptQueries.ToViewAsync(
                dbContext,
                current.Asset,
                currentDocument,
                cancellationToken);

        var projectSettings = await new GetProjectSettingsQueryHandler(dbContext).HandleAsync(
            new GetProjectSettingsQuery(command.ProjectId),
            cancellationToken);
        if (projectSettings is null) return null;
        var analysis = currentDocument.Mode == AdaptationModes.SourceChapters
            ? await GenerateAdaptationScriptCommandHandler.LoadSourceOnlyAnalysisAsync(
                dbContext,
                command.ProjectId,
                currentDocument.SourceAssetId,
                cancellationToken)
            : await StoryMaterialAnalysisQueries.GetCurrentAsync(
                dbContext,
                command.ProjectId,
                command.SourceResourceId,
                cancellationToken);
        if (analysis is null) return null;
        var productionScripts = new List<ProductionScriptEpisodeDraft>(targetOutlines.Length);
        foreach (var outline in targetOutlines)
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
        var episodes = targetOutlines.Select((draft, index) => new ProductionEpisode
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
        for (var index = 0; index < targetOutlines.Length; index++)
            productionEpisodeMap[targetOutlines[index].ProposalNumber] = episodes[index].Id;

        var generatedDocument = currentDocument with
        {
            Status = "draft",
            ProductionEpisodeIds = currentDocument.Episodes
                .Where(item => productionEpisodeMap.ContainsKey(item.ProposalNumber))
                .Select(item => productionEpisodeMap[item.ProposalNumber])
                .ToArray(),
            ProductionEpisodeMap = productionEpisodeMap
        };
        var documentJson = JsonSerializer.Serialize(generatedDocument, ProjectSourceDefaults.JsonOptions);
        var generatedAsset = new Asset
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
        dbContext.Assets.Add(generatedAsset);
        if (currentDocument.AnalysisAssetId != Guid.Empty)
            GenerateAdaptationScriptCommandHandler.AddDependency(
                dbContext,
                command.ProjectId,
                generatedAsset.Id,
                currentDocument.AnalysisAssetId,
                "based-on-analysis",
                now);
        GenerateAdaptationScriptCommandHandler.AddDependency(
            dbContext,
            command.ProjectId,
            generatedAsset.Id,
            currentDocument.SourceAssetId,
            "reference-source",
            now);
        current.State.CurrentAssetId = generatedAsset.Id;
        current.State.LifecycleStatus = "draft";
        current.State.UpdatedAtUtc = now;

        var nextAssetNumber = (await dbContext.Assets
            .Where(item => item.ProjectId == command.ProjectId)
            .Select(item => (int?)item.Number)
            .MaxAsync(cancellationToken) ?? 0) + 1;
        for (var index = 0; index < episodes.Length; index++)
        {
            var episodeJson = JsonSerializer.Serialize(
                new ProductionScriptPackageDocument(
                    generatedAsset.Id,
                    episodes[index].Id,
                    productionScripts[index],
                    targetOutlines[index]),
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
                generatedAsset.Id,
                "derived-from",
                now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await AdaptationScriptQueries.ToViewAsync(
            dbContext,
            generatedAsset,
            generatedDocument,
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

    internal static ProductionScriptEpisodeDraft NormalizeProductionScript(
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
                DurationSeconds = durations[durationIndex++],
                ShotSize = shot.ShotSize.Trim(),
                CameraAngle = shot.CameraAngle.Trim(),
                CameraMovement = shot.CameraMovement.Trim(),
                Purpose = shot.Purpose.Trim()
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
                Heading = scene.Heading.Trim(),
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
                StoryFunction = scene.StoryFunction?.Trim() ?? string.Empty,
                TargetSeconds = Math.Round(normalizedShots.Sum(shot => shot.DurationSeconds), 1),
                Rhythm = scene.Rhythm.Trim(),
                VisualContrast = scene.VisualContrast.Trim(),
                ShotPlan = normalizedShots
            };
        }).ToArray();
        ValidateDialogueShotCapacity(normalizedScenes, outline.ProposalNumber);
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

    private static void ValidateDialogueShotCapacity(
        IReadOnlyList<ProductionScriptSceneDraft> scenes,
        int episodeNumber)
    {
        var failures = new List<string>();
        foreach (var scene in scenes)
        {
            var lines = scene.Dialogues.SelectMany(dialogue => dialogue.Lines).ToArray();
            if (scene.ShotPlan!.Count < lines.Length)
                failures.Add(
                    $"第 {scene.SceneNumber} 场有 {lines.Length} 句对白，但只有 {scene.ShotPlan.Count} 个镜头；每句对白必须有独立镜头");
        }

        if (failures.Count > 0)
            throw new InvalidOperationException(
                $"第 {episodeNumber} 集对白未通过：{string.Join("；", failures)}。请一次修正全部场次。");
    }

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

public sealed class UpdateProductionScriptSceneCommandHandler(
    V2DbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<UpdateProductionScriptSceneCommand, ProductionScriptPackageView?>
{
    public async Task<ProductionScriptPackageView?> HandleAsync(
        UpdateProductionScriptSceneCommand command,
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
        if (packageDocument.Script is null)
            throw new InvalidOperationException("历史大纲不能直接编辑，请先重新生成正式剧本。");
        if (!packageDocument.Script.Scenes.Any(scene => scene.SceneNumber == command.SceneNumber))
            return null;

        var editedScript = packageDocument.Script with
        {
            Scenes = packageDocument.Script.Scenes.Select(scene => scene.SceneNumber == command.SceneNumber
                ? command.Scene with { SceneNumber = command.SceneNumber }
                : scene).ToArray()
        };
        var outline = new AdaptationEpisodeDraft(
            productionEpisode.EpisodeNumber,
            editedScript.Title,
            editedScript.Logline,
            editedScript.TargetSeconds,
            [],
            [],
            editedScript.SmallHooks,
            editedScript.BigHooks);
        var normalizedScript = ConfirmAdaptationScriptCommandHandler.NormalizeProductionScript(
            outline,
            editedScript);

        var adaptationAsset = await dbContext.Assets.AsNoTracking().SingleOrDefaultAsync(
            asset => asset.Id == packageDocument.AdaptationScriptAssetId
                && asset.ProjectId == command.ProjectId
                && asset.Type == AdaptationScriptQueries.AssetType,
            cancellationToken)
            ?? throw new InvalidOperationException("正式剧本关联的改编方案不存在。");
        var now = timeProvider.GetUtcNow();
        var documentJson = JsonSerializer.Serialize(
            new ProductionScriptPackageDocument(
                adaptationAsset.Id,
                command.ProductionEpisodeId,
                normalizedScript),
            ProjectSourceDefaults.JsonOptions);
        var editedAsset = new Asset
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
        dbContext.Assets.Add(editedAsset);
        GenerateAdaptationScriptCommandHandler.AddDependency(
            dbContext,
            command.ProjectId,
            editedAsset.Id,
            adaptationAsset.Id,
            "derived-from",
            now);
        current.State.CurrentAssetId = editedAsset.Id;
        current.State.LifecycleStatus = "active";
        current.State.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ProductionScriptPackageView(
            editedAsset.Id,
            editedAsset.ResourceId,
            editedAsset.Version,
            AdaptationScriptQueries.ReadDocument(adaptationAsset).SourceResourceId,
            command.ProductionEpisodeId,
            productionEpisode.EpisodeNumber,
            productionEpisode.Title,
            productionEpisode.TargetSeconds,
            productionEpisode.Status,
            adaptationAsset.Id,
            false,
            normalizedScript,
            now);
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
        var currentAdaptation = await AdaptationScriptQueries.FindCurrentAsync(
            dbContext,
            command.ProjectId,
            adaptation.SourceResourceId,
            cancellationToken);
        if (currentAdaptation.Asset is not null)
        {
            var currentDocument = AdaptationScriptQueries.ReadDocument(currentAdaptation.Asset);
            if (currentDocument.ProductionEpisodeMap?.Values.Contains(command.ProductionEpisodeId) == true)
            {
                adaptationAsset = currentAdaptation.Asset;
                adaptation = currentDocument;
            }
        }
        var mappedProposalNumber = adaptation.ProductionEpisodeMap?
            .Where(item => item.Value == command.ProductionEpisodeId)
            .Select(item => (int?)item.Key)
            .SingleOrDefault();
        var legacyEpisodeIndex = adaptation.ProductionEpisodeIds.ToList().IndexOf(command.ProductionEpisodeId);
        AdaptationEpisodeDraft? outline = null;
        if (mappedProposalNumber.HasValue)
            outline = adaptation.Episodes.SingleOrDefault(
                item => item.ProposalNumber == mappedProposalNumber.Value);
        outline ??= packageDocument.Episode;
        if (outline is null && legacyEpisodeIndex >= 0 && legacyEpisodeIndex < adaptation.Episodes.Count)
            outline = adaptation.Episodes[legacyEpisodeIndex];
        if (outline is null)
            throw new InvalidOperationException("无法确定当前生产集对应的改编大纲。");

        var projectSettings = await new GetProjectSettingsQueryHandler(dbContext).HandleAsync(
            new GetProjectSettingsQuery(command.ProjectId),
            cancellationToken)
            ?? throw new InvalidOperationException("项目设定不存在。");
        StoryMaterialAnalysisView analysis;
        if (adaptation.Mode == AdaptationModes.SourceChapters)
        {
            analysis = await GenerateAdaptationScriptCommandHandler.LoadSourceOnlyAnalysisAsync(
                dbContext,
                command.ProjectId,
                adaptation.SourceAssetId,
                cancellationToken)
                ?? throw new InvalidOperationException("改编方案关联的原文不存在。");
        }
        else
        {
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
            analysis = StoryMaterialAnalysisQueries.ToView(
                analysisAsset,
                analysisState,
                StoryMaterialAnalysisQueries.ReadDocument(analysisAsset));
        }
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
            AdaptationScriptQueries.ReadDocument(adaptationAsset).SourceResourceId,
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
        var productionEpisodeIds = document.ProductionEpisodeIds ?? [];
        return document with
        {
            Status = "draft",
            OverallSmallHooks = document.OverallSmallHooks ?? [],
            OverallBigHooks = document.OverallBigHooks ?? [],
            ProductionEpisodeIds = productionEpisodeIds,
            ProductionEpisodeMap = document.ProductionEpisodeMap
                ?? document.Episodes
                    .Take(productionEpisodeIds.Count)
                    .Select((episode, index) => new
                    {
                        episode.ProposalNumber,
                        ProductionEpisodeId = productionEpisodeIds[index]
                    })
                    .ToDictionary(item => item.ProposalNumber, item => item.ProductionEpisodeId),
            Mode = string.IsNullOrWhiteSpace(document.Mode)
                ? AdaptationModes.Rearranged
                : document.Mode,
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
            asset.UpdatedAtUtc,
            document.Mode,
            document.ProductionEpisodeMap);
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
        ProjectSourceView source,
        StoryMaterialAnalysisView analysis,
        int? desiredEpisodeCount,
        string? instruction,
        CancellationToken cancellationToken)
    {
        if (desiredEpisodeCount is > 6)
            throw new ArgumentException("剧集大纲每次最多生成 6 集。", nameof(desiredEpisodeCount));
        var configuration = await dbContext.FoundryConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (!LlmChatClientFactory.IsConfigured(configuration))
            throw new ProjectGenerationConfigurationException("请先在系统设置中配置语言模型。");
        var agentDefinition = await dbContext.AgentDefinitions.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == BuiltInAgents.EpisodeOutlinePlannerId,
            cancellationToken)
            ?? throw new InvalidOperationException("剧集大纲编排 Agent 未配置。");

        var agent = LlmChatClientFactory
            .Create(configuration!, dataProtectionProvider)
            .AsIChatClient()
            .AsHarnessAgent(
                new HarnessAgentOptions
                {
                    Name = agentDefinition.Name,
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
                        Instructions = agentDefinition.SystemPrompt,
                        MaxOutputTokens = 16_384
                    }
                },
                loggerFactory);
        var batchCounts = new List<int?> { desiredEpisodeCount };

        var episodes = new List<AdaptationEpisodeDraft>();
        var overallSmallHooks = new List<string>();
        var overallBigHooks = new List<string>();
        var title = string.Empty;
        var approach = string.Empty;
        foreach (var batchCount in batchCounts)
        {
            var batchStart = episodes.Count + 1;
            var batchEnd = batchStart + (batchCount ?? 1) - 1;
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
                        projectSettings.CharacterDesign,
                        projectSettings.CameraLanguage,
                        projectSettings.SoundStrategy
                    },
                    desiredEpisodeCount = batchCount,
                    seriesPlan = desiredEpisodeCount is > 6
                        ? new
                        {
                            totalEpisodeCount = desiredEpisodeCount.Value,
                            batchStart,
                            batchEnd,
                            previousEpisodes = episodes.TakeLast(6).Select(item => new
                            {
                                item.ProposalNumber,
                                item.Title,
                                item.Logline,
                                item.SmallHooks,
                                item.BigHooks
                            })
                        }
                        : null,
                    instruction = instruction?.Trim(),
                    source = new
                    {
                        source.Title,
                        source.Description,
                        source.Version,
                        chapters = source.Chapters.Select(chapter => new
                        {
                            chapter.Number,
                            chapter.Title,
                            chapter.Content
                        })
                    },
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
            var episodePlanningInstruction = desiredEpisodeCount is > 6
                ? $"整个项目共规划 {desiredEpisodeCount.Value} 集；当前只生成第 {batchStart} 至 {batchEnd} 集，恰好返回 {batchCount} 集，并承接 previousEpisodes"
                : batchCount.HasValue
                    ? $"生成恰好 {batchCount.Value} 集改编大纲"
                    : "根据素材内容和单集目标时长自行规划合理集数，返回 1 至 6 集改编大纲";
            var response = await agent.RunAsync(
                $"{episodePlanningInstruction}：\n{input}",
                cancellationToken: cancellationToken);
            var json = ExtractJson(response.Text);
            var payload = JsonSerializer.Deserialize<AdaptationScriptPayload>(
                json,
                ProjectSourceDefaults.JsonOptions)
                ?? throw new InvalidOperationException("GPT-5.4 未返回有效的改编大纲。");
            if (payload.Episodes.Count == 0)
                throw new InvalidOperationException("GPT-5.4 返回的改编大纲没有剧集。");
            if (batchCount.HasValue && payload.Episodes.Count != batchCount.Value)
                throw new InvalidOperationException(
                    $"GPT-5.4 本批应返回 {batchCount.Value} 集，实际返回 {payload.Episodes.Count} 集。");

            if (episodes.Count == 0)
            {
                title = payload.Title;
                approach = payload.Approach;
            }
            episodes.AddRange(payload.Episodes.Select((episode, index) =>
                episode with { ProposalNumber = batchStart + index }));
            overallSmallHooks.AddRange(payload.OverallSmallHooks);
            overallBigHooks.AddRange(payload.OverallBigHooks);
        }

        return new(
            title,
            approach,
            episodes,
            LlmChatClientFactory.GetModel(configuration!),
            "MAF HarnessAgent",
            overallSmallHooks,
            overallBigHooks);
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
        if (!LlmChatClientFactory.IsConfigured(configuration))
            throw new ProjectGenerationConfigurationException("请先在系统设置中配置语言模型。");

        var instructions = await BuiltInAgentPromptLoader.LoadAsync(
            dbContext,
            BuiltInAgents.ProductionScriptWriterId,
            cancellationToken);

        var agent = LlmChatClientFactory
            .Create(configuration!, dataProtectionProvider)
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
                        Instructions = instructions,
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
                projectSettings.CharacterDesign,
                projectSettings.CameraLanguage,
                projectSettings.SoundStrategy
            },
            storyGraph = new { analysis.Summary, analysis.Characters, analysis.Relations },
            correction = string.IsNullOrWhiteSpace(correction)
                ? null
                : new
                {
                    Failure = correction,
                    RequiredAction = "根据 Failure 修正上一版，同时保持大纲、人物身份和事件顺序不变。"
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
        app.MapPut(
            "/api/v2/projects/{projectId:guid}/production-episodes/{productionEpisodeId:guid}/script-package/scenes/{sceneNumber:int}",
            async (
                Guid projectId,
                Guid productionEpisodeId,
                int sceneNumber,
                UpdateProductionScriptSceneRequest request,
                ICommandDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var package = await dispatcher.SendAsync(
                        new UpdateProductionScriptSceneCommand(
                            projectId,
                            productionEpisodeId,
                            sceneNumber,
                            request.Scene),
                        cancellationToken);
                    return package is null ? Results.NotFound() : Results.Ok(package);
                }
                catch (InvalidOperationException error)
                {
                    return Results.BadRequest(new { error = error.Message });
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
                        request.Mode ?? AdaptationModes.Rearranged,
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
                    new AppendAdaptationEpisodeCommand(
                        projectId,
                        sourceResourceId,
                        request.Count,
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
        app.MapPost($"{route}/episodes/{{episodeNumber:int}}/production-script", async (
            Guid projectId,
            Guid sourceResourceId,
            int episodeNumber,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var script = await dispatcher.SendAsync(
                    new ConfirmAdaptationScriptCommand(projectId, sourceResourceId, episodeNumber),
                    cancellationToken);
                return script is null ? Results.NotFound() : Results.Ok(script);
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
                    title: "单集正式剧本生成失败",
                    detail: error.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });
        app.MapPost($"{route}/episodes/{{episodeNumber:int}}/production-script/tasks", async (
            Guid projectId,
            Guid sourceResourceId,
            int episodeNumber,
            IGenerationTaskScheduler scheduler,
            CancellationToken cancellationToken) =>
        {
            var task = await scheduler.EnqueueAsync(
                GenerationTaskTypes.ProductionScript,
                $"生成第 {episodeNumber} 集正式剧本",
                new GenerationTaskPayload(
                    projectId,
                    SourceResourceId: sourceResourceId,
                    EpisodeNumber: episodeNumber),
                cancellationToken);
            return Results.Accepted($"/api/v2/tasks/{task.Id}", task);
        });
        app.MapPut($"{route}/episodes/{{episodeNumber:int}}", async (
            Guid projectId,
            Guid sourceResourceId,
            int episodeNumber,
            UpdateAdaptationEpisodeRequest request,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var script = await dispatcher.SendAsync(
                    new UpdateAdaptationEpisodeCommand(
                        projectId,
                        sourceResourceId,
                        episodeNumber,
                        request.Title,
                        request.Logline,
                        request.SceneSummaries),
                    cancellationToken);
                return script is null ? Results.NotFound() : Results.Ok(script);
            }
            catch (ArgumentException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
        });
        app.MapDelete($"{route}/episodes", async (
            Guid projectId,
            Guid sourceResourceId,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            var script = await dispatcher.SendAsync(
                new ClearAdaptationEpisodesCommand(projectId, sourceResourceId),
                cancellationToken);
            return script is null ? Results.NotFound() : Results.Ok(script);
        });
        app.MapDelete($"{route}/episodes/{{episodeNumber:int}}", async (
            Guid projectId,
            Guid sourceResourceId,
            int episodeNumber,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var script = await dispatcher.SendAsync(
                    new DeleteAdaptationEpisodeCommand(projectId, sourceResourceId, episodeNumber),
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
        });
        return app;
    }
}

public sealed record UpdateProductionScriptSceneRequest(
    ProductionScriptSceneDraft Scene);

public sealed record GenerateAdaptationScriptRequest(
    string? Mode,
    int? DesiredEpisodeCount,
    string? Instruction);

public sealed record AppendAdaptationEpisodeRequest(int Count = 1, string? Instruction = null);

public sealed record RegenerateAdaptationEpisodeRequest(string Instruction);

public sealed record UpdateAdaptationEpisodeRequest(
    string Title,
    string Logline,
    IReadOnlyList<string> SceneSummaries);
