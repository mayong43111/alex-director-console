using System.Text;
using System.Text.Json;
using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Projects.Sources;
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
    VisualReferenceImageView? ReferenceImage = null);

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
        return current
            .Select(item => VisualAssetMapper.ToView(
                item.Asset,
                item.State,
                references.GetValueOrDefault(item.Asset.ResourceId)))
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

        var analysisAssets = await (
            from state in dbContext.ResourceStates.AsNoTracking()
            join asset in dbContext.Assets.AsNoTracking() on state.CurrentAssetId equals asset.Id
            where state.ProjectId == command.ProjectId
                && state.ResourceType == StoryMaterialAnalysisQueries.AssetType
                && asset.Type == StoryMaterialAnalysisQueries.AssetType
            select asset)
            .ToListAsync(cancellationToken);
        var analysisAsset = analysisAssets
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefault();
        if (analysisAsset is null) return [];

        var analysis = StoryMaterialAnalysisQueries.ReadDocument(analysisAsset);
        var adaptationAssets = await (
            from state in dbContext.ResourceStates.AsNoTracking()
            join asset in dbContext.Assets.AsNoTracking() on state.CurrentAssetId equals asset.Id
            where state.ProjectId == command.ProjectId
                && state.ResourceType == AdaptationScriptQueries.AssetType
                && asset.Type == AdaptationScriptQueries.AssetType
            select asset)
            .ToListAsync(cancellationToken);
        var adaptationAsset = adaptationAssets
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefault();
        var adaptation = adaptationAsset is null
            ? null
            : AdaptationScriptQueries.ReadDocument(adaptationAsset);
        var existing = await new ListVisualAssetsQueryHandler(dbContext).HandleAsync(
            new ListVisualAssetsQuery(command.ProjectId, null, true),
            cancellationToken);
        var existingKeys = existing
            .Select(item => $"{item.Kind}\n{item.Name}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var propGroups = adaptation?.Episodes
            .SelectMany(episode => episode.Scenes.SelectMany(scene => scene.Props.Select(prop => new
            {
                Name = prop.Trim(),
                Reference = $"{episode.Title} · {scene.Heading}"
            })))
            .Where(item => item.Name.Length > 0)
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        var productionPropNames = propGroups
            .Where(group => SpecialPropPolicy.RequiresAsset(group.Key, group.Count()))
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requests = analysis.Characters.Select(item => new SaveVisualAssetRequest(
                "character",
                item.Name,
                $"{item.Role} · {item.Goal}",
                string.Join("、", item.Traits),
                [],
                [],
                item.ChapterNumbers.Select(number => $"第 {number} 章").ToArray(),
                analysisAsset.Id))
            .Concat(analysis.Locations.Select(item => new SaveVisualAssetRequest(
                "scene",
                item.Name,
                item.Function,
                item.Atmosphere,
                [],
                [],
                item.ChapterNumbers.Select(number => $"第 {number} 章").ToArray(),
                analysisAsset.Id)))
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
                        adaptationAsset!.Id)))
            .Where(item => existingKeys.Add($"{item.Kind}\n{item.Name}"))
            .ToArray();

        if (adaptationAsset is not null)
        {
            var adaptationAssetIds = await dbContext.Assets.AsNoTracking()
                .Where(item => item.ProjectId == command.ProjectId
                    && item.Type == AdaptationScriptQueries.AssetType)
                .Select(item => item.Id)
                .ToArrayAsync(cancellationToken);
            var importedAssets = await (
                from state in dbContext.ResourceStates
                join asset in dbContext.Assets on state.CurrentAssetId equals asset.Id
                where state.ProjectId == command.ProjectId
                    && state.ResourceType == VisualAssetDefaults.AssetType
                    && asset.Type == VisualAssetDefaults.AssetType
                select new { Asset = asset, State = state })
                .ToListAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();
            foreach (var item in importedAssets)
            {
                var document = JsonSerializer.Deserialize<VisualAssetDocument>(
                    item.Asset.DocumentJson ?? "{}",
                    VisualAssetDefaults.JsonOptions);
                if (document?.Kind != "prop"
                    || document.SourceAssetId is not Guid sourceAssetId
                    || !adaptationAssetIds.Contains(sourceAssetId))
                {
                    continue;
                }

                item.State.LifecycleStatus = productionPropNames.Contains(document.Name)
                    ? "draft"
                    : "retired";
                item.State.UpdatedAtUtc = now;
            }
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var handler = new SaveVisualAssetCommandHandler(dbContext, timeProvider);
        foreach (var request in requests)
        {
            await handler.HandleAsync(
                new SaveVisualAssetCommand(command.ProjectId, null, request),
                cancellationToken);
        }

        return await new ListVisualAssetsQueryHandler(dbContext).HandleAsync(
            new ListVisualAssetsQuery(command.ProjectId, null),
            cancellationToken);
    }
}

internal static class SpecialPropPolicy
{
    private static readonly string[] NarrativeMarkers =
    [
        "信", "剑", "枪", "匣", "密", "秘方", "印章", "徽章", "戒指", "项链",
        "宝石", "钥匙", "地图", "契约", "王家", "金饰", "残柄", "遗失", "毒药"
    ];

    private static readonly HashSet<string> SetDressing = new(StringComparer.OrdinalIgnoreCase)
    {
        "窗户", "窗框", "门", "木门", "门帘", "门把手", "扶手", "楼梯扶手",
        "桌子", "办公桌", "椅子", "长凳", "酒杯", "钱币", "墨水瓶", "羽毛笔",
        "手帕", "手套", "披风", "绷带", "木棍", "铁铲", "火钳"
    };

    public static bool RequiresAsset(string name, int sceneCount)
    {
        var normalized = name.Trim();
        if (normalized.Length == 0 || SetDressing.Contains(normalized)) return false;
        return sceneCount > 1
            || NarrativeMarkers.Any(marker => normalized.Contains(marker, StringComparison.OrdinalIgnoreCase));
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

        group.MapVisualReferenceEndpoints();
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