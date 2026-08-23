using System.Text;
using System.Text.Json;
using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Generation;
using AlexDirectorConsole.V2.Api.Features.Projects.Generation;
using AlexDirectorConsole.V2.Api.Features.Projects.Sources;
using AlexDirectorConsole.V2.Api.Features.Projects.Voice;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Assets;

public sealed record VisualAssetView(
    Guid AssetId,
    Guid ResourceId,
    int Version,
    int Number,
    string Kind,
    string Name,
    string Summary,
    string VisualDescription,
    IReadOnlyList<string> MustKeep,
    IReadOnlyList<string> Avoid,
    IReadOnlyList<string> StoryReferences,
    string Status,
    Guid? SourceAssetId,
    DateTimeOffset UpdatedAtUtc,
    VisualReferenceImageView? ReferenceImage = null,
    VisualReferencePromptView? ReferencePrompt = null);

public sealed record SaveVisualAssetRequest(
    string Kind,
    string Name,
    string? Summary,
    string? VisualDescription,
    IReadOnlyList<string>? MustKeep,
    IReadOnlyList<string>? Avoid,
    IReadOnlyList<string>? StoryReferences,
    Guid? SourceAssetId);

internal sealed record VisualAssetDocument(
    string Kind,
    string Name,
    string Summary,
    string VisualDescription,
    IReadOnlyList<string> MustKeep,
    IReadOnlyList<string> Avoid,
    IReadOnlyList<string> StoryReferences,
    Guid? SourceAssetId);

public sealed record ListVisualAssetsQuery(Guid ProjectId, string? Kind, bool IncludeRetired = false)
    : IQuery<IReadOnlyList<VisualAssetView>>;

public sealed record SaveVisualAssetCommand(
    Guid ProjectId,
    Guid? ResourceId,
    SaveVisualAssetRequest Request)
    : ICommand<SaveVisualAssetResult>;

public sealed record ImportStoryMaterialAssetsCommand(Guid ProjectId)
    : ICommand<IReadOnlyList<VisualAssetView>?>;

public sealed record ScriptMaterialAnalysisStatusView(
    Guid SourceAssetId,
    int EpisodeNumber,
    string Title,
    string ScriptType,
    int Version,
    bool IsAnalyzed);

public enum SaveVisualAssetStatus
{
    Success,
    Invalid,
    NotFound
}

public sealed record SaveVisualAssetResult(
    SaveVisualAssetStatus Status,
    VisualAssetView? Asset,
    Dictionary<string, string[]> Errors);

public sealed class ListVisualAssetsQueryHandler(V2DbContext dbContext)
    : IQueryHandler<ListVisualAssetsQuery, IReadOnlyList<VisualAssetView>>
{
    public async Task<IReadOnlyList<VisualAssetView>> HandleAsync(
        ListVisualAssetsQuery query,
        CancellationToken cancellationToken)
    {
        var current = await (
            from state in dbContext.ResourceStates.AsNoTracking()
            join asset in dbContext.Assets.AsNoTracking() on state.CurrentAssetId equals asset.Id
            where state.ProjectId == query.ProjectId
                && state.ResourceType == VisualAssetDefaults.AssetType
                && (query.IncludeRetired || state.LifecycleStatus != "retired")
                && asset.Type == VisualAssetDefaults.AssetType
            orderby asset.Number
            select new { Asset = asset, State = state })
            .ToListAsync(cancellationToken);

        var references = await VisualReferenceQueries.GetLatestBySubjectAsync(
            dbContext,
            query.ProjectId,
            current.Select(item => item.Asset.ResourceId).ToArray(),
            cancellationToken);
        var prompts = await VisualReferenceQueries.GetLatestPromptsBySubjectAsync(
            dbContext,
            query.ProjectId,
            current.Select(item => item.Asset.ResourceId).ToArray(),
            cancellationToken);
        return current
            .Select(item =>
            {
                var reference = references.GetValueOrDefault(item.Asset.ResourceId);
                var prompt = prompts.GetValueOrDefault(item.Asset.ResourceId)
                    ?? (reference is null || string.IsNullOrWhiteSpace(reference.Prompt)
                        ? null
                        : new VisualReferencePromptView(
                            reference.AssetId,
                            reference.SubjectResourceId,
                            reference.SubjectType,
                            reference.SubjectName,
                            reference.Version,
                            reference.Prompt,
                            null,
                            false,
                            reference.CreatedAtUtc));
                return VisualAssetMapper.ToView(item.Asset, item.State, reference) with
                {
                    ReferencePrompt = prompt
                };
            })
            .Where(item => string.IsNullOrWhiteSpace(query.Kind) || item.Kind == query.Kind)
            .ToArray();
    }
}

public sealed class SaveVisualAssetCommandHandler(
    V2DbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<SaveVisualAssetCommand, SaveVisualAssetResult>
{
    public async Task<SaveVisualAssetResult> HandleAsync(
        SaveVisualAssetCommand command,
        CancellationToken cancellationToken)
    {
        var document = VisualAssetMapper.Normalize(command.Request);
        var errors = VisualAssetMapper.Validate(document);
        if (errors.Count > 0)
        {
            return new(SaveVisualAssetStatus.Invalid, null, errors);
        }

        if (!await dbContext.Projects.AnyAsync(item => item.Id == command.ProjectId, cancellationToken))
        {
            return new(SaveVisualAssetStatus.NotFound, null, errors);
        }

        Asset? previousAsset = null;
        ResourceState? state = null;
        if (command.ResourceId is not null)
        {
            state = await dbContext.ResourceStates.SingleOrDefaultAsync(
                item => item.ProjectId == command.ProjectId
                    && item.ResourceId == command.ResourceId
                    && item.ResourceType == VisualAssetDefaults.AssetType,
                cancellationToken);
            if (state is null)
            {
                return new(SaveVisualAssetStatus.NotFound, null, errors);
            }
            previousAsset = await dbContext.Assets.SingleAsync(
                item => item.Id == state.CurrentAssetId,
                cancellationToken);
        }

        if (document.SourceAssetId is not null
            && !await dbContext.Assets.AnyAsync(
                item => item.Id == document.SourceAssetId && item.ProjectId == command.ProjectId,
                cancellationToken))
        {
            return new(
                SaveVisualAssetStatus.Invalid,
                null,
                new Dictionary<string, string[]> { ["sourceAssetId"] = ["来源资产不存在。"] });
        }

        var documentJson = JsonSerializer.Serialize(document, VisualAssetDefaults.JsonOptions);
        var now = timeProvider.GetUtcNow();
        var resourceId = command.ResourceId ?? Guid.NewGuid();
        var number = previousAsset?.Number
            ?? (await dbContext.Assets
                .Where(item => item.ProjectId == command.ProjectId)
                .Select(item => (int?)item.Number)
                .MaxAsync(cancellationToken) ?? 0) + 1;
        var asset = new Asset
        {
            ProjectId = command.ProjectId,
            ResourceId = resourceId,
            Version = (previousAsset?.Version ?? 0) + 1,
            Number = number,
            Type = VisualAssetDefaults.AssetType,
            Name = document.Name,
            DocumentJson = documentJson,
            ContentType = "application/json",
            SizeBytes = Encoding.UTF8.GetByteCount(documentJson),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.Assets.Add(asset);

        state ??= new ResourceState
        {
            ProjectId = command.ProjectId,
            ResourceId = resourceId,
            ResourceType = VisualAssetDefaults.AssetType
        };
        if (command.ResourceId is null) dbContext.ResourceStates.Add(state);
        state.CurrentAssetId = asset.Id;
        state.LifecycleStatus = "draft";
        state.IsStale = false;
        state.StaleReason = null;
        state.StaleSinceUtc = null;
        state.UpdatedAtUtc = now;

        if (document.SourceAssetId is not null)
        {
            dbContext.AssetDependencies.Add(new AssetDependency
            {
                ProjectId = command.ProjectId,
                ConsumerAssetId = asset.Id,
                SourceAssetId = document.SourceAssetId.Value,
                Role = "derived-from",
                IsRequired = true,
                CreatedAtUtc = now
            });
        }

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
        return new(SaveVisualAssetStatus.Success, VisualAssetMapper.ToView(asset, state), errors);
    }
}

public sealed class ImportStoryMaterialAssetsCommandHandler(
    V2DbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<ImportStoryMaterialAssetsCommand, IReadOnlyList<VisualAssetView>?>
{
    public async Task<IReadOnlyList<VisualAssetView>?> HandleAsync(
        ImportStoryMaterialAssetsCommand command,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Projects.AnyAsync(item => item.Id == command.ProjectId, cancellationToken))
        {
            return null;
        }

        var scriptSources = await LoadScriptSourcesAsync(dbContext, command.ProjectId, cancellationToken);
        if (scriptSources.Count == 0) return [];
        var markers = await LoadMarkersAsync(dbContext, command.ProjectId, cancellationToken);
        var pendingSources = scriptSources
            .Where(source => !markers.Any(marker =>
                marker.SourceAssetId == source.AssetId && marker.EpisodeNumber == source.EpisodeNumber))
            .ToArray();
        if (pendingSources.Length == 0)
        {
            return await new ListVisualAssetsQueryHandler(dbContext).HandleAsync(
                new ListVisualAssetsQuery(command.ProjectId, null),
                cancellationToken);
        }

        var existing = await new ListVisualAssetsQueryHandler(dbContext).HandleAsync(
            new ListVisualAssetsQuery(command.ProjectId, null, true),
            cancellationToken);
        var existingKeys = existing
            .Select(item => $"{item.Kind}\n{item.Name}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scenes = pendingSources.SelectMany(source => source.Scenes.Select(scene => new
        {
            Source = source,
            Scene = scene,
            Reference = $"E{source.EpisodeNumber:D2}《{source.Title}》 · {scene.Heading}"
        })).ToArray();
        var characterGroups = scenes
            .SelectMany(item => item.Scene.Characters.Select(character => new
            {
                Name = character.Trim(),
                item.Source,
                item.Reference
            }))
            .Where(item => item.Name.Length > 0)
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sceneGroups = scenes
            .Where(item => !string.IsNullOrWhiteSpace(item.Scene.Heading))
            .GroupBy(item => item.Scene.Heading.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var propGroups = scenes
            .SelectMany(item => item.Scene.Props.Select(prop => new
            {
                Name = prop.Trim(),
                item.Source,
                item.Reference
            }))
            .Where(item => item.Name.Length > 0)
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var productionPropNames = propGroups
            .Where(group => SpecialPropPolicy.RequiresAsset(group.Key, group.Count()))
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requests = characterGroups.Select(group => new SaveVisualAssetRequest(
                "character",
                group.Key,
                $"出现于 {group.Select(item => item.Source.EpisodeNumber).Distinct().Count()} 集剧本",
                string.Empty,
                [],
                [],
                group.Select(item => item.Reference).Distinct().ToArray(),
                group.First().Source.AssetId))
            .Concat(sceneGroups.Select(group => new SaveVisualAssetRequest(
                "scene",
                group.Key,
                string.Join("；", group.Select(item => item.Scene.Summary).Where(value => value.Length > 0).Distinct().Take(3)),
                string.Join("；", group.Select(item => item.Scene.VisualDescription).Where(value => value.Length > 0).Distinct().Take(3)),
                [],
                [],
                group.Select(item => item.Reference).Distinct().ToArray(),
                group.First().Source.AssetId)))
            .Concat(propGroups
                .Where(group => productionPropNames.Contains(group.Key))
                .Select(group => new SaveVisualAssetRequest(
                    "prop",
                    group.Key,
                    $"出现于 {group.Count()} 个场次",
                    string.Empty,
                    [],
                    [],
                    group.Select(item => item.Reference).Distinct().ToArray(),
                    group.First().Source.AssetId)))
            .Where(item => existingKeys.Add($"{item.Kind}\n{item.Name}"))
            .ToArray();

        var handler = new SaveVisualAssetCommandHandler(dbContext, timeProvider);
        foreach (var request in requests)
        {
            await handler.HandleAsync(
                new SaveVisualAssetCommand(command.ProjectId, null, request),
                cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var nextNumber = (await dbContext.Assets
            .Where(item => item.ProjectId == command.ProjectId)
            .Select(item => (int?)item.Number)
            .MaxAsync(cancellationToken) ?? 0) + 1;
        foreach (var source in pendingSources)
        {
            var marker = new ScriptMaterialAnalysisMarker(
                source.AssetId,
                source.EpisodeNumber,
                source.Title,
                source.ScriptType,
                source.Scenes.SelectMany(scene => scene.Characters).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                source.Scenes.Select(scene => scene.Heading).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            var markerJson = JsonSerializer.Serialize(marker, VisualAssetDefaults.JsonOptions);
            var markerAsset = new Asset
            {
                ProjectId = command.ProjectId,
                ResourceId = Guid.NewGuid(),
                Version = 1,
                Number = nextNumber++,
                Type = ScriptMaterialAnalysisQueries.AssetType,
                SchemaVersion = 1,
                Name = $"E{source.EpisodeNumber:D2}《{source.Title}》素材分析",
                DocumentJson = markerJson,
                ContentType = "application/json",
                SizeBytes = Encoding.UTF8.GetByteCount(markerJson),
                ProductionEpisodeId = source.ProductionEpisodeId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            dbContext.Assets.Add(markerAsset);
            dbContext.ResourceStates.Add(new ResourceState
            {
                ProjectId = command.ProjectId,
                ResourceId = markerAsset.ResourceId,
                ResourceType = ScriptMaterialAnalysisQueries.AssetType,
                CurrentAssetId = markerAsset.Id,
                LifecycleStatus = "current",
                UpdatedAtUtc = now
            });
            dbContext.AssetDependencies.Add(new AssetDependency
            {
                ProjectId = command.ProjectId,
                ConsumerAssetId = markerAsset.Id,
                SourceAssetId = source.AssetId,
                Role = "analyzes-script-materials",
                IsRequired = true,
                CreatedAtUtc = now
            });
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        return await new ListVisualAssetsQueryHandler(dbContext).HandleAsync(
            new ListVisualAssetsQuery(command.ProjectId, null),
            cancellationToken);
    }

    public static async Task<IReadOnlyList<ScriptMaterialAnalysisStatusView>> GetStatusesAsync(
        V2DbContext dbContext,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var sources = await LoadScriptSourcesAsync(dbContext, projectId, cancellationToken);
        var markers = await LoadMarkersAsync(dbContext, projectId, cancellationToken);
        return sources.Select(source => new ScriptMaterialAnalysisStatusView(
            source.AssetId,
            source.EpisodeNumber,
            source.Title,
            source.ScriptType,
            source.Version,
            markers.Any(marker => marker.SourceAssetId == source.AssetId
                && marker.EpisodeNumber == source.EpisodeNumber))).ToArray();
    }

    private static async Task<IReadOnlyList<ScriptMaterialSource>> LoadScriptSourcesAsync(
        V2DbContext dbContext,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var formalRows = await (
            from state in dbContext.ResourceStates.AsNoTracking()
            join asset in dbContext.Assets.AsNoTracking() on state.CurrentAssetId equals asset.Id
            join episode in dbContext.ProductionEpisodes.AsNoTracking() on asset.ProductionEpisodeId equals episode.Id
            where state.ProjectId == projectId
                && state.ResourceType == "script-package"
                && asset.Type == "script-package"
            select new { Asset = asset, Episode = episode })
            .ToListAsync(cancellationToken);
        var formalSources = formalRows.Select(row =>
        {
            var document = JsonSerializer.Deserialize<ProductionScriptPackageDocument>(
                row.Asset.DocumentJson ?? "{}",
                ProjectSourceDefaults.JsonOptions);
            var scenes = document?.Script?.Scenes.Select(scene => new ScriptMaterialScene(
                    scene.Heading,
                    scene.Summary,
                    scene.VisualContrast,
                    scene.Characters,
                    scene.Props))
                ?? document?.Episode?.Scenes.Select(scene => new ScriptMaterialScene(
                    scene.Heading,
                    scene.Summary,
                    scene.VisualContrast ?? string.Empty,
                    scene.Characters ?? [],
                    scene.Props ?? []))
                ?? [];
            return new ScriptMaterialSource(
                row.Asset.Id,
                row.Asset.ResourceId,
                row.Asset.Version,
                row.Episode.Id,
                row.Episode.EpisodeNumber,
                row.Episode.Title,
                "正式剧本",
                scenes.ToArray());
        }).ToList();

        var coveredEpisodeIds = formalSources
            .Where(item => item.ProductionEpisodeId is not null)
            .Select(item => item.ProductionEpisodeId!.Value)
            .ToHashSet();
        var adaptationAssets = await (
            from state in dbContext.ResourceStates.AsNoTracking()
            join asset in dbContext.Assets.AsNoTracking() on state.CurrentAssetId equals asset.Id
            where state.ProjectId == projectId
                && state.ResourceType == AdaptationScriptQueries.AssetType
                && asset.Type == AdaptationScriptQueries.AssetType
            select asset)
            .ToListAsync(cancellationToken);
        foreach (var asset in adaptationAssets)
        {
            var document = AdaptationScriptQueries.ReadDocument(asset);
            foreach (var episode in document.Episodes)
            {
                if (document.ProductionEpisodeMap?.TryGetValue(episode.ProposalNumber, out var productionEpisodeId) == true
                    && coveredEpisodeIds.Contains(productionEpisodeId))
                {
                    continue;
                }
                formalSources.Add(new ScriptMaterialSource(
                    asset.Id,
                    asset.ResourceId,
                    asset.Version,
                    null,
                    episode.ProposalNumber,
                    episode.Title,
                    "改编方案",
                    episode.Scenes.Select(scene => new ScriptMaterialScene(
                        scene.Heading,
                        scene.Summary,
                        scene.VisualContrast ?? string.Empty,
                        scene.Characters ?? [],
                        scene.Props ?? [])).ToArray()));
            }
        }
        return formalSources.OrderBy(item => item.EpisodeNumber).ToArray();
    }

    private static async Task<IReadOnlyList<ScriptMaterialAnalysisMarker>> LoadMarkersAsync(
        V2DbContext dbContext,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var documents = await dbContext.Assets.AsNoTracking()
            .Where(item => item.ProjectId == projectId
                && item.Type == ScriptMaterialAnalysisQueries.AssetType
                && item.DocumentJson != null)
            .Select(item => item.DocumentJson!)
            .ToListAsync(cancellationToken);
        return documents.Select(document => JsonSerializer.Deserialize<ScriptMaterialAnalysisMarker>(
                document,
                VisualAssetDefaults.JsonOptions))
            .OfType<ScriptMaterialAnalysisMarker>()
            .ToArray();
    }
}

internal static class ScriptMaterialAnalysisQueries
{
    public const string AssetType = "script-material-analysis";
}

internal sealed record ScriptMaterialAnalysisMarker(
    Guid SourceAssetId,
    int EpisodeNumber,
    string Title,
    string ScriptType,
    int CharacterCount,
    int SceneCount);

internal sealed record ScriptMaterialSource(
    Guid AssetId,
    Guid ResourceId,
    int Version,
    Guid? ProductionEpisodeId,
    int EpisodeNumber,
    string Title,
    string ScriptType,
    IReadOnlyList<ScriptMaterialScene> Scenes);

internal sealed record ScriptMaterialScene(
    string Heading,
    string Summary,
    string VisualDescription,
    IReadOnlyList<string> Characters,
    IReadOnlyList<string> Props);

internal static class SpecialPropPolicy
{
    private static readonly string[] LargePropMarkers =
    [
        "柜", "桌", "台", "床", "车", "船", "架", "屏", "设备", "机器", "仪器",
        "雕像", "钢琴"
    ];

    private static readonly string[] NarrativeMarkers =
    [
        "信", "剑", "枪", "匣", "密", "秘方", "印章", "徽章", "戒指", "项链",
        "宝石", "钥匙", "地图", "契约", "王家", "金饰", "残柄", "遗失", "毒药"
    ];

    private static readonly HashSet<string> SetDressing = new(StringComparer.OrdinalIgnoreCase)
    {
        "窗户", "窗框", "门", "木门", "门帘", "门把手", "扶手", "楼梯扶手",
        "桌子", "办公桌", "椅子", "长凳", "酒杯", "钱币", "墨水瓶", "羽毛笔",
        "手帕", "手套", "披风", "绷带", "木棍", "铁铲", "火钳",
        "老马", "马车", "钱袋", "空钱袋", "疗伤膏"
    };

    public static bool RequiresAsset(string name, int sceneCount)
    {
        var normalized = name.Trim();
        if (normalized.Length == 0 || SetDressing.Contains(normalized)) return false;
        return sceneCount > 1
            || NarrativeMarkers.Any(marker => normalized.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public static bool RequiresLargeRecurringAsset(string name, int sceneCount)
    {
        var normalized = name.Trim();
        return sceneCount > 1
            && !SetDressing.Contains(normalized)
            && LargePropMarkers.Any(marker =>
                normalized.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}

internal static class VisualAssetDefaults
{
    public const string AssetType = "visual-asset";
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal static class VisualAssetMapper
{
    private static readonly HashSet<string> ValidKinds = ["character", "scene", "prop"];

    public static VisualAssetDocument Normalize(SaveVisualAssetRequest request) => new(
        request.Kind.Trim().ToLowerInvariant(),
        request.Name.Trim(),
        request.Summary?.Trim() ?? string.Empty,
        request.VisualDescription?.Trim() ?? string.Empty,
        NormalizeList(request.MustKeep),
        NormalizeList(request.Avoid),
        NormalizeList(request.StoryReferences),
        request.SourceAssetId);

    public static Dictionary<string, string[]> Validate(VisualAssetDocument document)
    {
        var errors = new Dictionary<string, string[]>();
        if (!ValidKinds.Contains(document.Kind)) errors["kind"] = ["资产类型必须是 character、scene 或 prop。"];
        if (document.Name.Length is < 1 or > 100) errors["name"] = ["资产名称必须为 1 至 100 个字符。"];
        if (document.Summary.Length > 1000) errors["summary"] = ["叙事定义不能超过 1000 个字符。"];
        if (document.VisualDescription.Length > 4000) errors["visualDescription"] = ["视觉定义不能超过 4000 个字符。"];
        return errors;
    }

    public static VisualAssetView ToView(
        Asset asset,
        ResourceState state,
        VisualReferenceImageView? referenceImage = null)
    {
        var document = ReadDocument(asset);
        return new(
            asset.Id,
            asset.ResourceId,
            asset.Version,
            asset.Number,
            document.Kind,
            document.Name,
            document.Summary,
            document.VisualDescription,
            document.MustKeep,
            document.Avoid,
            document.StoryReferences,
            state.LifecycleStatus,
            document.SourceAssetId,
            asset.UpdatedAtUtc,
            referenceImage);
    }

    public static VisualAssetDocument ReadDocument(Asset asset) =>
        JsonSerializer.Deserialize<VisualAssetDocument>(
            asset.DocumentJson ?? throw new InvalidOperationException("视觉资产缺少文档内容。"),
            VisualAssetDefaults.JsonOptions)
        ?? throw new InvalidOperationException("视觉资产内容无效。");

    private static string[] NormalizeList(IReadOnlyList<string>? values) => values?
        .Select(item => item.Trim())
        .Where(item => item.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(30)
        .ToArray() ?? [];
}

public static class VisualAssetEndpoints
{
    public static IEndpointRouteBuilder MapVisualAssets(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/projects/{projectId:guid}/visual-assets");

        group.MapGet("/", async (
            Guid projectId,
            string? kind,
            IQueryDispatcher dispatcher,
            CancellationToken cancellationToken) => Results.Ok(
                await dispatcher.QueryAsync(new ListVisualAssetsQuery(projectId, kind), cancellationToken)));

        group.MapPost("/", async (
            Guid projectId,
            SaveVisualAssetRequest request,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) => ToResult(
                await dispatcher.SendAsync(
                    new SaveVisualAssetCommand(projectId, null, request),
                    cancellationToken),
                true));

        group.MapPut("/{resourceId:guid}", async (
            Guid projectId,
            Guid resourceId,
            SaveVisualAssetRequest request,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) => ToResult(
                await dispatcher.SendAsync(
                    new SaveVisualAssetCommand(projectId, resourceId, request),
                    cancellationToken),
                false));

        group.MapPost("/import-story-materials", async (
            Guid projectId,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            var assets = await dispatcher.SendAsync(
                new ImportStoryMaterialAssetsCommand(projectId),
                cancellationToken);
            return assets is null ? Results.NotFound() : Results.Ok(assets);
        });

        group.MapGet("/script-material-analysis-status", async (
            Guid projectId,
            V2DbContext dbContext,
            CancellationToken cancellationToken) => Results.Ok(
                await ImportStoryMaterialAssetsCommandHandler.GetStatusesAsync(
                    dbContext,
                    projectId,
                    cancellationToken)));

        group.MapPost("/import-script-materials/tasks", async (
            Guid projectId,
            IGenerationTaskScheduler scheduler,
            CancellationToken cancellationToken) =>
        {
            var task = await scheduler.EnqueueAsync(
                GenerationTaskTypes.StoryMaterialAssets,
                "从剧本提取视觉资产",
                new GenerationTaskPayload(projectId),
                cancellationToken);
            return Results.Accepted($"/api/v2/tasks/{task.Id}", task);
        });

        group.MapPost("/import-story-materials/tasks", async (
            Guid projectId,
            IGenerationTaskScheduler scheduler,
            CancellationToken cancellationToken) =>
        {
            var task = await scheduler.EnqueueAsync(
                GenerationTaskTypes.StoryMaterialAssets,
                "从故事资料建立视觉资产",
                new GenerationTaskPayload(projectId),
                cancellationToken);
            return Results.Accepted($"/api/v2/tasks/{task.Id}", task);
        });

        group.MapVisualReferenceEndpoints();
        group.MapVoiceProfileEndpoints();
        return app;
    }

    private static IResult ToResult(SaveVisualAssetResult result, bool created) => result.Status switch
    {
        SaveVisualAssetStatus.Success when created => Results.Created(
            $"/api/v2/projects/{result.Asset!.ResourceId}",
            result.Asset),
        SaveVisualAssetStatus.Success => Results.Ok(result.Asset),
        SaveVisualAssetStatus.Invalid => Results.ValidationProblem(result.Errors),
        _ => Results.NotFound()
    };
}