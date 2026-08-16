using System.Text;
using System.Text.Json;
using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Projects.Assets;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.Projects.Sources;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Storyboard;

public sealed record StoryboardHookDraft(string Type, string Description);

public sealed record StoryboardShotDraft(
    int SceneNumber,
    int ShotNumber,
    double DurationSeconds,
    string ShotSize,
    string CameraAngle,
    string CameraMovement,
    string Composition,
    string VisualDescription,
    string Action,
    string Dialogue,
    string Sound,
    IReadOnlyList<string> Characters,
    IReadOnlyList<string> Props,
    IReadOnlyList<StoryboardHookDraft>? Hooks = null);

public sealed record StoryboardDesignResult(
    IReadOnlyList<StoryboardShotDraft> Shots,
    string Model,
    string Runtime);

public sealed record StoryboardLinkedAssetView(
    Guid AssetId,
    Guid ResourceId,
    string Kind,
    string Name);

public sealed record ShotProductionView(
    Guid RunId,
    string Mode,
    string Status,
    string CurrentStage,
    IReadOnlyList<string> Stages,
    DateTimeOffset CreatedAtUtc,
    Guid? OutputAssetId = null,
    string? OutputUrl = null);

public sealed record StoryboardShotView(
    Guid AssetId,
    Guid ResourceId,
    int Version,
    int SceneNumber,
    int ShotNumber,
    double DurationSeconds,
    string ShotSize,
    string CameraAngle,
    string CameraMovement,
    string Composition,
    string VisualDescription,
    string Action,
    string Dialogue,
    string Sound,
    IReadOnlyList<string> Characters,
    IReadOnlyList<string> Props,
    IReadOnlyList<StoryboardHookDraft> Hooks,
    IReadOnlyList<StoryboardLinkedAssetView> LinkedAssets,
    ShotProductionView? Production,
    string Status,
    DateTimeOffset UpdatedAtUtc);

public sealed record StoryboardView(
    Guid ProductionEpisodeId,
    int EpisodeNumber,
    string Title,
    Guid ScriptPackageAssetId,
    int Revision,
    bool IsStale,
    double TargetSeconds,
    double TotalDurationSeconds,
    IReadOnlyList<StoryboardShotView> Shots,
    string Model,
    string Runtime,
    DateTimeOffset UpdatedAtUtc);

internal sealed record StoryboardShotDocument(
    int SceneNumber,
    int ShotNumber,
    double DurationSeconds,
    string ShotSize,
    string CameraAngle,
    string CameraMovement,
    string Composition,
    string VisualDescription,
    string Action,
    string Dialogue,
    string Sound,
    IReadOnlyList<string> Characters,
    IReadOnlyList<string> Props,
    string Model,
    string Runtime,
    IReadOnlyList<StoryboardHookDraft>? Hooks = null);

public interface IStoryboardDesigner
{
    Task<StoryboardDesignResult> DesignAsync(
        ProjectSettingsView settings,
        ProductionScriptPackageView scriptPackage,
        IReadOnlyList<VisualAssetView> assets,
        CancellationToken cancellationToken);
}

public sealed record GetStoryboardQuery(Guid ProjectId, Guid ProductionEpisodeId)
    : IQuery<StoryboardView?>;

public sealed record GenerateStoryboardCommand(Guid ProjectId, Guid ProductionEpisodeId)
    : ICommand<StoryboardView?>;

public sealed record UpdateStoryboardShotAssetsCommand(
    Guid ProjectId,
    Guid ProductionEpisodeId,
    Guid ShotResourceId,
    IReadOnlyList<Guid> AssetResourceIds)
    : ICommand<StoryboardView?>;

public sealed record StartShotProductionCommand(
    Guid ProjectId,
    Guid ProductionEpisodeId,
    Guid ShotResourceId,
    string ConfirmedPrompt)
    : ICommand<ShotProductionView?>;

public sealed record UpdateStoryboardShotAssetsRequest(IReadOnlyList<Guid>? AssetResourceIds);

public sealed record StartShotProductionRequest(string? ConfirmedPrompt);

public sealed class GetStoryboardQueryHandler(V2DbContext dbContext)
    : IQueryHandler<GetStoryboardQuery, StoryboardView?>
{
    public Task<StoryboardView?> HandleAsync(
        GetStoryboardQuery query,
        CancellationToken cancellationToken) => StoryboardQueries.GetAsync(
            dbContext,
            query.ProjectId,
            query.ProductionEpisodeId,
            cancellationToken);
}

public sealed class GenerateStoryboardCommandHandler(
    V2DbContext dbContext,
    IStoryboardDesigner designer,
    TimeProvider timeProvider)
    : ICommandHandler<GenerateStoryboardCommand, StoryboardView?>
{
    public async Task<StoryboardView?> HandleAsync(
        GenerateStoryboardCommand command,
        CancellationToken cancellationToken)
    {
        var scriptPackage = await new GetProductionScriptPackageQueryHandler(dbContext).HandleAsync(
            new GetProductionScriptPackageQuery(command.ProjectId, command.ProductionEpisodeId),
            cancellationToken);
        if (scriptPackage is null) return null;

        var settings = await new GetProjectSettingsQueryHandler(dbContext).HandleAsync(
            new GetProjectSettingsQuery(command.ProjectId),
            cancellationToken);
        if (settings is null) return null;
        var visualAssets = await new ListVisualAssetsQueryHandler(dbContext).HandleAsync(
            new ListVisualAssetsQuery(command.ProjectId, null),
            cancellationToken);
        var result = await designer.DesignAsync(settings, scriptPackage, visualAssets, cancellationToken);
        var shots = Normalize(result.Shots, scriptPackage);

        var definitions = await dbContext.ShotDefinitions
            .Where(item => item.ProjectId == command.ProjectId
                && item.ProductionEpisodeId == command.ProductionEpisodeId)
            .ToListAsync(cancellationToken);
        var definitionByKey = definitions.ToDictionary(item => (item.SceneNumber, item.ShotNumber));
        var currentKeys = shots.Select(item => (item.SceneNumber, item.ShotNumber)).ToHashSet();
        var retiredDefinitions = definitions
            .Where(item => !currentKeys.Contains((item.SceneNumber, item.ShotNumber)))
            .ToArray();
        foreach (var retired in retiredDefinitions)
        {
            var retiredState = await dbContext.ResourceStates.SingleAsync(
                item => item.ProjectId == command.ProjectId
                    && item.ResourceId == retired.ShotResourceId,
                cancellationToken);
            retiredState.LifecycleStatus = "retired";
            retiredState.UpdatedAtUtc = timeProvider.GetUtcNow();
            dbContext.ShotDefinitions.Remove(retired);
            definitionByKey.Remove((retired.SceneNumber, retired.ShotNumber));
        }

        var nextNumber = (await dbContext.Assets
            .Where(item => item.ProjectId == command.ProjectId)
            .Select(item => (int?)item.Number)
            .MaxAsync(cancellationToken) ?? 0) + 1;
        var now = timeProvider.GetUtcNow();
        foreach (var shot in shots)
        {
            definitionByKey.TryGetValue((shot.SceneNumber, shot.ShotNumber), out var definition);
            Asset? previousAsset = null;
            ResourceState? state = null;
            if (definition is not null)
            {
                previousAsset = await dbContext.Assets.SingleAsync(
                    item => item.Id == definition.ShotAssetId,
                    cancellationToken);
                state = await dbContext.ResourceStates.SingleAsync(
                    item => item.ProjectId == command.ProjectId
                        && item.ResourceId == definition.ShotResourceId,
                    cancellationToken);
            }

            var resourceId = definition?.ShotResourceId ?? Guid.NewGuid();
            var document = new StoryboardShotDocument(
                shot.SceneNumber,
                shot.ShotNumber,
                shot.DurationSeconds,
                shot.ShotSize.Trim(),
                shot.CameraAngle.Trim(),
                shot.CameraMovement.Trim(),
                shot.Composition.Trim(),
                shot.VisualDescription.Trim(),
                shot.Action.Trim(),
                shot.Dialogue.Trim(),
                shot.Sound.Trim(),
                NormalizeNames(shot.Characters),
                NormalizeNames(shot.Props),
                result.Model,
                result.Runtime,
                NormalizeHooks(shot.Hooks));
            var documentJson = JsonSerializer.Serialize(document, StoryboardDefaults.JsonOptions);
            var shotAsset = new Asset
            {
                ProjectId = command.ProjectId,
                ProductionEpisodeId = command.ProductionEpisodeId,
                ResourceId = resourceId,
                Version = (previousAsset?.Version ?? 0) + 1,
                Number = previousAsset?.Number ?? nextNumber++,
                Type = StoryboardDefaults.AssetType,
                Name = $"S{shot.SceneNumber:00}-{shot.ShotNumber:00} · {scriptPackage.Title}",
                DocumentJson = documentJson,
                ContentType = "application/json",
                SizeBytes = Encoding.UTF8.GetByteCount(documentJson),
                GenerationMetadataJson = JsonSerializer.Serialize(
                    new { result.Model, result.Runtime },
                    StoryboardDefaults.JsonOptions),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            dbContext.Assets.Add(shotAsset);

            state ??= new ResourceState
            {
                ProjectId = command.ProjectId,
                ResourceId = resourceId,
                ResourceType = StoryboardDefaults.AssetType
            };
            if (definition is null) dbContext.ResourceStates.Add(state);
            state.CurrentAssetId = shotAsset.Id;
            state.LifecycleStatus = "draft";
            state.IsStale = false;
            state.StaleReason = null;
            state.StaleSinceUtc = null;
            state.UpdatedAtUtc = now;

            if (definition is null)
            {
                definition = new ShotDefinition
                {
                    ProjectId = command.ProjectId,
                    ProductionEpisodeId = command.ProductionEpisodeId,
                    ShotResourceId = resourceId
                };
                dbContext.ShotDefinitions.Add(definition);
            }
            definition.ShotAssetId = shotAsset.Id;
            definition.ScriptPackageAssetId = scriptPackage.AssetId;
            definition.SceneNumber = shot.SceneNumber;
            definition.ShotNumber = shot.ShotNumber;
            definition.DurationSeconds = shot.DurationSeconds;
            definition.UpdatedAtUtc = now;

            dbContext.AssetDependencies.Add(new AssetDependency
            {
                ProjectId = command.ProjectId,
                ConsumerAssetId = shotAsset.Id,
                SourceAssetId = scriptPackage.AssetId,
                Role = "derived-from-script",
                IsRequired = true,
                CreatedAtUtc = now
            });
            foreach (var linkedAsset in MatchAssets(document, visualAssets))
            {
                dbContext.AssetDependencies.Add(new AssetDependency
                {
                    ProjectId = command.ProjectId,
                    ConsumerAssetId = shotAsset.Id,
                    SourceAssetId = linkedAsset.AssetId,
                    Role = $"uses-{linkedAsset.Kind}",
                    IsRequired = true,
                    CreatedAtUtc = now
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await StoryboardQueries.GetAsync(
            dbContext,
            command.ProjectId,
            command.ProductionEpisodeId,
            cancellationToken);
    }

    private static StoryboardShotDraft[] Normalize(
        IReadOnlyList<StoryboardShotDraft> proposed,
        ProductionScriptPackageView scriptPackage)
    {
        if (proposed.Count is < 1 or > 100)
            throw new InvalidOperationException("分镜必须包含 1 至 100 个镜头。");
        var validScenes = scriptPackage.Episode.Scenes.Select(item => item.SceneNumber).ToHashSet();
        if (proposed.Any(item => !validScenes.Contains(item.SceneNumber)))
            throw new InvalidOperationException("分镜包含正式剧本中不存在的场次。");
        if (proposed.Any(item => item.ShotNumber < 1 || item.DurationSeconds <= 0))
            throw new InvalidOperationException("镜号和镜头时长必须大于零。");
        if (proposed.GroupBy(item => (item.SceneNumber, item.ShotNumber)).Any(group => group.Count() > 1))
            throw new InvalidOperationException("同一场次内不能出现重复镜号。");

        ValidateHooks(proposed, scriptPackage);

        var target = scriptPackage.TargetSeconds ?? scriptPackage.Episode.TargetSeconds;
        var total = proposed.Sum(item => item.DurationSeconds);
        var scale = target > 0 && total > 0 ? target / total : 1;
        var normalized = proposed
            .OrderBy(item => item.SceneNumber)
            .ThenBy(item => item.ShotNumber)
            .Select(item => item with { DurationSeconds = Math.Max(.5, Math.Round(item.DurationSeconds * scale, 1)) })
            .ToArray();
        if (target > 0)
        {
            var difference = Math.Round(target - normalized.Sum(item => item.DurationSeconds), 1);
            var last = normalized[^1];
            normalized[^1] = last with { DurationSeconds = Math.Max(.5, Math.Round(last.DurationSeconds + difference, 1)) };
        }
        return normalized;
    }

    private static string[] NormalizeNames(IReadOnlyList<string> values) => values
        .Select(item => item.Trim())
        .Where(item => item.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static StoryboardHookDraft[] NormalizeHooks(IReadOnlyList<StoryboardHookDraft>? hooks) =>
        (hooks ?? [])
            .Select(item => new StoryboardHookDraft(item.Type.Trim().ToLowerInvariant(), item.Description.Trim()))
            .Where(item => item.Description.Length > 0)
            .ToArray();

    private static void ValidateHooks(
        IReadOnlyList<StoryboardShotDraft> shots,
        ProductionScriptPackageView scriptPackage)
    {
        var expected = (scriptPackage.Episode.SmallHooks ?? [])
            .Select(item => new StoryboardHookDraft("small", item.Trim()))
            .Concat((scriptPackage.Episode.BigHooks ?? [])
                .Select(item => new StoryboardHookDraft("big", item.Trim())))
            .Where(item => item.Description.Length > 0)
            .ToArray();
        var actual = shots.SelectMany(item => NormalizeHooks(item.Hooks)).ToArray();
        if (actual.Any(item => item.Type is not ("small" or "big")))
            throw new InvalidOperationException("分镜爆点类型只能是 small 或 big。");

        static Dictionary<string, int> Counts(IEnumerable<StoryboardHookDraft> hooks) => hooks
            .GroupBy(item => $"{item.Type}\n{item.Description}", StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var expectedCounts = Counts(expected);
        var actualCounts = Counts(actual);
        if (expectedCounts.Count != actualCounts.Count
            || expectedCounts.Any(item => !actualCounts.TryGetValue(item.Key, out var count) || count != item.Value))
            throw new InvalidOperationException("每条正式剧本爆点必须原文映射到且仅映射到一个具体分镜，不能遗漏或新增。");
    }

    private static IEnumerable<VisualAssetView> MatchAssets(
        StoryboardShotDocument shot,
        IReadOnlyList<VisualAssetView> assets)
    {
        return assets.Where(asset => asset.Kind switch
        {
            "character" => shot.Characters.Any(name => NamesMatch(asset.Name, name)),
            "prop" => shot.Props.Any(name => NamesMatch(asset.Name, name)),
            _ => false
        }).DistinctBy(item => item.AssetId);
    }

    private static bool NamesMatch(string left, string right)
    {
        static string Core(string value) => value
            .Split(['（', '(', '／', '/'], 2, StringSplitOptions.TrimEntries)[0]
            .Trim();
        var leftCore = Core(left);
        var rightCore = Core(right);
        return leftCore.Length > 0 && rightCore.Length > 0
            && string.Equals(leftCore, rightCore, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class StoryboardDefaults
{
    public const string AssetType = "storyboard-shot";
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal static class StoryboardQueries
{
    public static async Task<StoryboardView?> GetAsync(
        V2DbContext dbContext,
        Guid projectId,
        Guid productionEpisodeId,
        CancellationToken cancellationToken)
    {
        var scriptPackage = await new GetProductionScriptPackageQueryHandler(dbContext).HandleAsync(
            new GetProductionScriptPackageQuery(projectId, productionEpisodeId),
            cancellationToken);
        if (scriptPackage is null) return null;
        var definitions = await dbContext.ShotDefinitions.AsNoTracking()
            .Where(item => item.ProjectId == projectId && item.ProductionEpisodeId == productionEpisodeId)
            .OrderBy(item => item.SceneNumber)
            .ThenBy(item => item.ShotNumber)
            .ToListAsync(cancellationToken);
        if (definitions.Count == 0) return null;
        var assetIds = definitions.Select(item => item.ShotAssetId).ToArray();
        var assets = await dbContext.Assets.AsNoTracking()
            .Where(item => assetIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var resourceIds = definitions.Select(item => item.ShotResourceId).ToArray();
        var states = await dbContext.ResourceStates.AsNoTracking()
            .Where(item => item.ProjectId == projectId && resourceIds.Contains(item.ResourceId))
            .ToDictionaryAsync(item => item.ResourceId, cancellationToken);
        var shots = new List<StoryboardShotView>();
        foreach (var definition in definitions)
        {
            var asset = assets[definition.ShotAssetId];
            var state = states[definition.ShotResourceId];
            var document = JsonSerializer.Deserialize<StoryboardShotDocument>(
                asset.DocumentJson ?? throw new InvalidOperationException("镜头资产缺少文档内容。"),
                StoryboardDefaults.JsonOptions)
                ?? throw new InvalidOperationException("镜头资产内容无效。");
            var linkedAssets = await GetLinkedAssetsAsync(dbContext, definition, cancellationToken);
            var production = await GetProductionAsync(dbContext, definition, cancellationToken);
            shots.Add(new StoryboardShotView(
                asset.Id,
                asset.ResourceId,
                asset.Version,
                document.SceneNumber,
                document.ShotNumber,
                document.DurationSeconds,
                document.ShotSize,
                document.CameraAngle,
                document.CameraMovement,
                document.Composition,
                document.VisualDescription,
                document.Action,
                document.Dialogue,
                document.Sound,
                document.Characters,
                document.Props,
                document.Hooks ?? [],
                linkedAssets,
                production,
                state.LifecycleStatus,
                asset.UpdatedAtUtc));
            }
        var firstDocument = JsonSerializer.Deserialize<StoryboardShotDocument>(
            assets[definitions[0].ShotAssetId].DocumentJson!,
            StoryboardDefaults.JsonOptions)!;
        return new(
            productionEpisodeId,
            scriptPackage.EpisodeNumber,
            scriptPackage.Title,
            definitions[0].ScriptPackageAssetId,
            shots.Max(item => item.Version),
            definitions.Any(item => item.ScriptPackageAssetId != scriptPackage.AssetId),
            scriptPackage.TargetSeconds ?? scriptPackage.Episode.TargetSeconds,
            Math.Round(shots.Sum(item => item.DurationSeconds), 1),
            shots,
            firstDocument.Model,
            firstDocument.Runtime,
            shots.Max(item => item.UpdatedAtUtc));
    }

    public static async Task<Guid[]> GetLinkedAssetIdsAsync(
        V2DbContext dbContext,
        ShotDefinition definition,
        CancellationToken cancellationToken)
    {
        var linkedIds = await dbContext.ShotAssetLinks.AsNoTracking()
            .Where(item => item.ProjectId == definition.ProjectId
                && item.ShotResourceId == definition.ShotResourceId)
            .Select(item => item.AssetId)
            .ToArrayAsync(cancellationToken);
        if (linkedIds.Length == 0)
        {
            linkedIds = await dbContext.AssetDependencies.AsNoTracking()
                .Where(item => item.ProjectId == definition.ProjectId
                    && item.ConsumerAssetId == definition.ShotAssetId
                    && item.Role.StartsWith("uses-"))
                .Select(item => item.SourceAssetId)
                .ToArrayAsync(cancellationToken);
        }
        if (linkedIds.Length == 0) return [];

        var linkedResourceIds = await dbContext.Assets.AsNoTracking()
            .Where(item => linkedIds.Contains(item.Id))
            .Select(item => item.ResourceId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var currentAssetIds = await dbContext.ResourceStates.AsNoTracking()
            .Where(item => item.ProjectId == definition.ProjectId
                && item.ResourceType == VisualAssetDefaults.AssetType
                && item.LifecycleStatus != "retired"
                && linkedResourceIds.Contains(item.ResourceId))
            .Select(item => item.CurrentAssetId)
            .ToArrayAsync(cancellationToken);
        var currentAssets = await dbContext.Assets.AsNoTracking()
            .Where(item => currentAssetIds.Contains(item.Id))
            .ToListAsync(cancellationToken);
        if (currentAssets.Any(item => VisualAssetMapper.ReadDocument(item).Kind == "scene"))
            return currentAssetIds;

        var scriptPackage = await new GetProductionScriptPackageQueryHandler(dbContext).HandleAsync(
            new GetProductionScriptPackageQuery(definition.ProjectId, definition.ProductionEpisodeId),
            cancellationToken);
        var heading = scriptPackage?.Episode.Scenes
            .FirstOrDefault(item => item.SceneNumber == definition.SceneNumber)?.Heading;
        if (string.IsNullOrWhiteSpace(heading)) return currentAssetIds;
        var sceneAssets = await (
            from state in dbContext.ResourceStates.AsNoTracking()
            join asset in dbContext.Assets.AsNoTracking() on state.CurrentAssetId equals asset.Id
            where state.ProjectId == definition.ProjectId
                && state.ResourceType == VisualAssetDefaults.AssetType
                && state.LifecycleStatus != "retired"
                && asset.Type == VisualAssetDefaults.AssetType
            select asset)
            .ToListAsync(cancellationToken);
        var matchedSceneIds = sceneAssets
            .Where(asset =>
            {
                var document = VisualAssetMapper.ReadDocument(asset);
                return document.Kind == "scene" && SceneMatchesHeading(document.Name, heading);
            })
            .Select(item => item.Id);
        return currentAssetIds.Concat(matchedSceneIds).Distinct().ToArray();
    }

    private static bool SceneMatchesHeading(string sceneName, string heading)
    {
        var coreName = sceneName.Split(['（', '('], 2, StringSplitOptions.TrimEntries)[0]
            .Replace("一带", string.Empty, StringComparison.Ordinal)
            .Replace("通往", string.Empty, StringComparison.Ordinal)
            .Replace("的", string.Empty, StringComparison.Ordinal);
        var keywords = coreName
            .Split(["与", "及", "至", "／", "/"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(item => new[]
            {
                item,
                item.Replace("阁楼住处", string.Empty, StringComparison.Ordinal),
                item.Replace("办公室", string.Empty, StringComparison.Ordinal)
            })
            .Where(item => item.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return keywords.Any(keyword => heading.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            || (coreName.Contains("道路", StringComparison.Ordinal)
                && heading.Contains("道路", StringComparison.Ordinal));
    }

    private static async Task<StoryboardLinkedAssetView[]> GetLinkedAssetsAsync(
        V2DbContext dbContext,
        ShotDefinition definition,
        CancellationToken cancellationToken)
    {
        var assetIds = await GetLinkedAssetIdsAsync(dbContext, definition, cancellationToken);
        if (assetIds.Length == 0) return [];
        var linkedAssets = await dbContext.Assets.AsNoTracking()
            .Where(item => assetIds.Contains(item.Id))
            .ToListAsync(cancellationToken);
        return linkedAssets.Select(asset =>
        {
            var document = VisualAssetMapper.ReadDocument(asset);
            return new StoryboardLinkedAssetView(
                asset.Id,
                asset.ResourceId,
                document.Kind,
                document.Name);
        }).OrderBy(item => item.Kind).ThenBy(item => item.Name).ToArray();
    }

    private static async Task<ShotProductionView?> GetProductionAsync(
        V2DbContext dbContext,
        ShotDefinition definition,
        CancellationToken cancellationToken)
    {
        var items = await dbContext.ProductionRunItems.AsNoTracking()
            .Where(item => item.ProjectId == definition.ProjectId
                && item.ProductionEpisodeId == definition.ProductionEpisodeId
                && item.ShotResourceId == definition.ShotResourceId
                && item.ShotAssetId == definition.ShotAssetId)
            .ToListAsync(cancellationToken);
        if (items.Count == 0) return null;
        var runIds = items.Select(item => item.RunId).Distinct().ToArray();
        var runs = await dbContext.ProductionRuns.AsNoTracking()
            .Where(item => runIds.Contains(item.Id))
            .ToListAsync(cancellationToken);
        var run = runs.OrderByDescending(item => item.CreatedAtUtc).First();
        return ShotProductionModes.ToView(
            run,
            items.Where(item => item.RunId == run.Id).ToArray(),
            definition.DurationSeconds);
    }
}

#pragma warning disable MAAI001
public sealed class MafStoryboardDesigner(
    V2DbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    ILoggerFactory loggerFactory) : IStoryboardDesigner
{
    public async Task<StoryboardDesignResult> DesignAsync(
        ProjectSettingsView settings,
        ProductionScriptPackageView scriptPackage,
        IReadOnlyList<VisualAssetView> assets,
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
                    Name = "AlexStoryboardDesigner",
                    MaxContextWindowTokens = 1_050_000,
                    MaxOutputTokens = 16_384,
                    MaximumIterationsPerRequest = 4,
                    DisableFileMemory = true,
                    DisableWebSearch = true,
                    DisableTodoProvider = true,
                    DisableAgentModeProvider = true,
                    DisableAgentSkillsProvider = true,
                    ChatOptions = new ChatOptions
                    {
                        Instructions = """
                            你是动画导演和分镜师。将已批准的正式剧本转换为可生产的结构化镜头。
                            不得改写事件顺序、人物身份和对白含义。每个镜头必须属于输入中的真实场次，sceneNumber 必须原样使用；shotNumber 在每场从 1 连续编号。
                            根据项目画幅、摄影语言、视觉资产和单集目标时长设计景别、机位、运镜、构图、画面、动作、对白与声音。
                            镜头时长总和应等于目标时长。characters 和 props 只能使用输入剧本或资产中的名称。
                            episode.smallHooks 和 episode.bigHooks 是已批准的爆点。必须根据事件内容把每条爆点落实到最能体现它的一个具体镜头：在该镜头 hooks 中写入 type（small 或 big）和 description。description 必须逐字复制输入爆点，不得改写、遗漏、新增或重复；允许一个镜头承载多条爆点，无爆点镜头返回空数组。
                            只返回 JSON，不要 Markdown。结构：
                            {"shots":[{"sceneNumber":1,"shotNumber":1,"durationSeconds":3.5,"shotSize":"全景","cameraAngle":"平视","cameraMovement":"固定","composition":"...","visualDescription":"...","action":"...","dialogue":"...","sound":"...","characters":["..."],"props":["..."],"hooks":[{"type":"small","description":"逐字复制的既有爆点"}]}]}
                            """,
                        MaxOutputTokens = 16_384
                    }
                },
                loggerFactory);
        var input = JsonSerializer.Serialize(new
        {
            project = new
            {
                settings.ProjectName,
                settings.AspectRatio,
                settings.VisualStyle,
                settings.ArtDirection,
                settings.CameraLanguage,
                settings.SoundStrategy,
                settings.CharacterDesign,
                settings.ImagePromptPrefix
            },
            targetSeconds = scriptPackage.TargetSeconds ?? scriptPackage.Episode.TargetSeconds,
            episode = scriptPackage.Episode,
            assets = assets.Select(item => new
            {
                item.Kind,
                item.Name,
                item.Summary,
                item.VisualDescription,
                item.MustKeep,
                item.Avoid
            })
        }, StoryboardDefaults.JsonOptions);
        var response = await agent.RunAsync(
            $"为该正式剧本设计完整分镜：\n{input}",
            cancellationToken: cancellationToken);
        var text = response.Text?.Trim() ?? string.Empty;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidOperationException("GPT-5.4 未返回 JSON 分镜。");
        var payload = JsonSerializer.Deserialize<StoryboardPayload>(
            text[start..(end + 1)],
            StoryboardDefaults.JsonOptions)
            ?? throw new InvalidOperationException("GPT-5.4 未返回有效分镜。");
        return new(payload.Shots, configuration.Deployment, "MAF HarnessAgent");
    }

    private sealed class StoryboardPayload
    {
        public List<StoryboardShotDraft> Shots { get; set; } = [];
    }
}
#pragma warning restore MAAI001

public static class StoryboardEndpoints
{
    public static IEndpointRouteBuilder MapStoryboards(this IEndpointRouteBuilder app)
    {
        var route = "/api/v2/projects/{projectId:guid}/production-episodes/{productionEpisodeId:guid}/storyboard";
        app.MapGet(route, async (
            Guid projectId,
            Guid productionEpisodeId,
            IQueryDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            var storyboard = await dispatcher.QueryAsync(
                new GetStoryboardQuery(projectId, productionEpisodeId),
                cancellationToken);
            return storyboard is null ? Results.NotFound() : Results.Ok(storyboard);
        });
        app.MapPost($"{route}/generate", async (
            Guid projectId,
            Guid productionEpisodeId,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            var storyboard = await dispatcher.SendAsync(
                new GenerateStoryboardCommand(projectId, productionEpisodeId),
                cancellationToken);
            return storyboard is null ? Results.NotFound() : Results.Ok(storyboard);
        });
        app.MapPut($"{route}/shots/{{shotResourceId:guid}}/assets", async (
            Guid projectId,
            Guid productionEpisodeId,
            Guid shotResourceId,
            UpdateStoryboardShotAssetsRequest request,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            var storyboard = await dispatcher.SendAsync(
                new UpdateStoryboardShotAssetsCommand(
                    projectId,
                    productionEpisodeId,
                    shotResourceId,
                    request.AssetResourceIds ?? []),
                cancellationToken);
            return storyboard is null ? Results.NotFound() : Results.Ok(storyboard);
        });
        app.MapPost($"{route}/shots/{{shotResourceId:guid}}/production/start", async (
            Guid projectId,
            Guid productionEpisodeId,
            Guid shotResourceId,
            StartShotProductionRequest request,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.ConfirmedPrompt))
                {
                    return Results.BadRequest(new { error = "请先预览并确认完整提示词和参数。" });
                }
                var production = await dispatcher.SendAsync(
                    new StartShotProductionCommand(
                        projectId,
                        productionEpisodeId,
                        shotResourceId,
                        request.ConfirmedPrompt),
                    cancellationToken);
                return production is null ? Results.NotFound() : Results.Ok(production);
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
                    title: "首帧生成失败",
                    detail: error.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });
        app.MapPost($"{route}/shots/{{shotResourceId:guid}}/production/preview", async (
            Guid projectId,
            Guid productionEpisodeId,
            Guid shotResourceId,
            IShotFrameService frameService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var preview = await frameService.PreviewFirstFrameAsync(
                    projectId,
                    productionEpisodeId,
                    shotResourceId,
                    cancellationToken);
                return preview is null ? Results.NotFound() : Results.Ok(preview);
            }
            catch (ProjectGenerationConfigurationException error)
            {
                return Results.Conflict(new { error = error.Message });
            }
            catch (InvalidOperationException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
        });
        return app;
    }
}

public sealed class UpdateStoryboardShotAssetsCommandHandler(
    V2DbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<UpdateStoryboardShotAssetsCommand, StoryboardView?>
{
    public async Task<StoryboardView?> HandleAsync(
        UpdateStoryboardShotAssetsCommand command,
        CancellationToken cancellationToken)
    {
        var definition = await dbContext.ShotDefinitions.SingleOrDefaultAsync(
            item => item.ProjectId == command.ProjectId
                && item.ProductionEpisodeId == command.ProductionEpisodeId
                && item.ShotResourceId == command.ShotResourceId,
            cancellationToken);
        if (definition is null) return null;

        var resourceIds = command.AssetResourceIds.Distinct().ToArray();
        if (resourceIds.Length == 0)
            throw new InvalidOperationException("每个镜头至少关联一个出场角色、场景或特殊道具。");
        var assets = await (
            from state in dbContext.ResourceStates.AsNoTracking()
            join asset in dbContext.Assets.AsNoTracking() on state.CurrentAssetId equals asset.Id
            where state.ProjectId == command.ProjectId
                && state.ResourceType == VisualAssetDefaults.AssetType
                && state.LifecycleStatus != "retired"
                && resourceIds.Contains(state.ResourceId)
            select new { Asset = asset, state.ResourceId })
            .ToListAsync(cancellationToken);
        if (assets.Count != resourceIds.Length)
            throw new InvalidOperationException("镜头关联中包含不存在或已退休的资产。");

        var existing = await dbContext.ShotAssetLinks
            .Where(item => item.ProjectId == command.ProjectId
                && item.ShotResourceId == command.ShotResourceId)
            .ToListAsync(cancellationToken);
        dbContext.ShotAssetLinks.RemoveRange(existing);
        var now = timeProvider.GetUtcNow();
        foreach (var item in assets)
        {
            var document = VisualAssetMapper.ReadDocument(item.Asset);
            dbContext.ShotAssetLinks.Add(new ShotAssetLink
            {
                ProjectId = command.ProjectId,
                ProductionEpisodeId = command.ProductionEpisodeId,
                ShotResourceId = command.ShotResourceId,
                AssetId = item.Asset.Id,
                Role = document.Kind,
                SubjectId = item.ResourceId,
                CreatedAtUtc = now
            });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return await StoryboardQueries.GetAsync(
            dbContext,
            command.ProjectId,
            command.ProductionEpisodeId,
            cancellationToken);
    }
}

public sealed class StartShotProductionCommandHandler(
    V2DbContext dbContext,
    TimeProvider timeProvider,
    IShotFrameService frameService)
    : ICommandHandler<StartShotProductionCommand, ShotProductionView?>
{
    public async Task<ShotProductionView?> HandleAsync(
        StartShotProductionCommand command,
        CancellationToken cancellationToken)
    {
        var definition = await dbContext.ShotDefinitions.AsNoTracking().SingleOrDefaultAsync(
            item => item.ProjectId == command.ProjectId
                && item.ProductionEpisodeId == command.ProductionEpisodeId
                && item.ShotResourceId == command.ShotResourceId,
            cancellationToken);
        if (definition is null) return null;

        var activeItems = await (
            from item in dbContext.ProductionRunItems.AsNoTracking()
            join productionRun in dbContext.ProductionRuns.AsNoTracking() on item.RunId equals productionRun.Id
            where item.ProjectId == command.ProjectId
                && item.ProductionEpisodeId == command.ProductionEpisodeId
                && item.ShotResourceId == command.ShotResourceId
                && (productionRun.Status == "queued" || productionRun.Status == "running")
            select new { Run = productionRun, Item = item })
            .ToListAsync(cancellationToken);
        if (activeItems.Count > 0)
        {
            var activeRun = activeItems[0].Run;
            return ShotProductionModes.ToView(activeRun, activeItems.Select(item => item.Item).ToArray(), definition.DurationSeconds);
        }

        var preview = await frameService.PreviewFirstFrameAsync(
            command.ProjectId,
            command.ProductionEpisodeId,
            command.ShotResourceId,
            cancellationToken)
            ?? throw new InvalidOperationException("镜头不存在。");
        if (!string.Equals(command.ConfirmedPrompt, preview.Prompt, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("镜头、项目设定或参考资产已变化，请重新预览并确认提示词。");
        }

        var project = await dbContext.Projects.AsNoTracking().SingleAsync(
            item => item.Id == command.ProjectId,
            cancellationToken);
        if (project.CurrentCreativeSettingsId is not Guid creativeSettingsAssetId)
            throw new InvalidOperationException("开始制作前必须先保存项目设定。");
        var shotAsset = await dbContext.Assets.AsNoTracking().SingleAsync(
            item => item.Id == definition.ShotAssetId,
            cancellationToken);
        var productionInputs = await ShotProductionPreflight.ResolveAsync(
            dbContext,
            definition,
            cancellationToken);
        var inputAssetIds = productionInputs.ReferenceImageAssetIds
            .Concat(productionInputs.PropAssetIds)
            .Append(shotAsset.Id)
            .Append(creativeSettingsAssetId)
            .Distinct()
            .ToArray();
        var mode = ShotProductionModes.ForDuration(definition.DurationSeconds);
        var stages = ShotProductionModes.Stages(mode);
        var now = timeProvider.GetUtcNow();
        var run = new ProductionRun
        {
            ProjectId = command.ProjectId,
            ProductionEpisodeId = command.ProductionEpisodeId,
            ScriptPackageAssetId = definition.ScriptPackageAssetId,
            CreativeSettingsAssetId = creativeSettingsAssetId,
            Status = "queued",
            CurrentStage = stages[0],
            SpecJson = JsonSerializer.Serialize(new
            {
                mode,
                thresholdSeconds = ShotProductionModes.ThresholdSeconds,
                durationSeconds = definition.DurationSeconds,
                command.ShotResourceId,
                execution = mode == ShotProductionModes.FirstLastContinuous ? "sequential" : "direct"
            }, StoryboardDefaults.JsonOptions),
            OriginalInstruction = mode == ShotProductionModes.FirstLastContinuous
                ? "生成首帧后连续执行尾帧制作。"
                : "直接制作镜头首帧。",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.ProductionRuns.Add(run);
        for (var index = 0; index < stages.Count; index++)
        {
            dbContext.ProductionRunItems.Add(new ProductionRunItem
            {
                RunId = run.Id,
                ProjectId = command.ProjectId,
                ProductionEpisodeId = command.ProductionEpisodeId,
                ShotResourceId = command.ShotResourceId,
                ShotAssetId = shotAsset.Id,
                ShotName = shotAsset.Name,
                Stage = stages[index],
                Status = index == 0 ? "queued" : "waiting",
                Attempt = 0,
                InputAssetIdsJson = JsonSerializer.Serialize(inputAssetIds, StoryboardDefaults.JsonOptions),
                CreatedAtUtc = now
            });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await frameService.GenerateFirstFrameAsync(run.Id, command.ConfirmedPrompt, cancellationToken);
        var outputAssetId = await dbContext.ProductionRunItems
            .Where(item => item.RunId == run.Id && item.Stage == "first-frame")
            .Select(item => item.OutputAssetId)
            .SingleAsync(cancellationToken);
        return new(
            run.Id,
            mode,
            run.Status,
            run.CurrentStage,
            stages,
            run.CreatedAtUtc,
            outputAssetId,
            outputAssetId is null
                ? null
                : $"/api/v2/projects/{run.ProjectId}/storyboard/frames/{outputAssetId}/content");
    }
}

internal sealed record ShotProductionInputs(
    IReadOnlyList<Guid> ReferenceImageAssetIds,
    IReadOnlyList<Guid> PropAssetIds);

internal static class ShotProductionPreflight
{
    public static async Task<ShotProductionInputs> ResolveAsync(
        V2DbContext dbContext,
        ShotDefinition definition,
        CancellationToken cancellationToken)
    {
        var linkedAssetIds = await StoryboardQueries.GetLinkedAssetIdsAsync(
            dbContext,
            definition,
            cancellationToken);
        var linkedAssets = await (
            from state in dbContext.ResourceStates.AsNoTracking()
            join asset in dbContext.Assets.AsNoTracking() on state.CurrentAssetId equals asset.Id
            where state.ProjectId == definition.ProjectId
                && state.ResourceType == VisualAssetDefaults.AssetType
                && state.LifecycleStatus != "retired"
                && linkedAssetIds.Contains(asset.Id)
            select asset)
            .ToListAsync(cancellationToken);
        var subjects = linkedAssets
            .Select(asset => new { Asset = asset, Document = VisualAssetMapper.ReadDocument(asset) })
            .ToArray();
        var characters = subjects.Where(item => item.Document.Kind == "character").ToArray();
        var scenes = subjects.Where(item => item.Document.Kind == "scene").ToArray();
        if (scenes.Length == 0)
        {
            throw new InvalidOperationException("开始制作前必须为镜头关联场景。");
        }

        var requiredSubjects = characters.Concat(scenes).ToArray();
        var subjectResourceIds = requiredSubjects.Select(item => item.Asset.ResourceId).ToArray();
        var referenceCandidates = await (
            from reference in dbContext.VisualReferences.AsNoTracking()
            join image in dbContext.Assets.AsNoTracking() on reference.ImageAssetId equals image.Id
            where reference.ProjectId == definition.ProjectId
                && subjectResourceIds.Contains(reference.SubjectResourceId)
                && image.ProjectId == definition.ProjectId
                && image.BlobContent != null
                && image.ContentType != null
                && image.ContentType.StartsWith("image/")
            select new { reference.SubjectResourceId, reference.ImageAssetId, image.Version })
            .ToListAsync(cancellationToken);
        var references = referenceCandidates
            .GroupBy(item => item.SubjectResourceId)
            .Select(group => group.OrderByDescending(item => item.Version).First())
            .ToArray();
        var referencedSubjectIds = references.Select(item => item.SubjectResourceId).ToHashSet();
        var missingCharacters = characters
            .Where(item => !referencedSubjectIds.Contains(item.Asset.ResourceId))
            .Select(item => item.Document.Name)
            .ToArray();
        var missingScenes = scenes
            .Where(item => !referencedSubjectIds.Contains(item.Asset.ResourceId))
            .Select(item => item.Document.Name)
            .ToArray();
        if (missingCharacters.Length > 0 || missingScenes.Length > 0)
        {
            var details = new List<string>();
            if (missingCharacters.Length > 0)
                details.Add($"人物缺少参考图：{string.Join("、", missingCharacters)}");
            if (missingScenes.Length > 0)
                details.Add($"场景缺少参考图：{string.Join("、", missingScenes)}");
            throw new InvalidOperationException(string.Join("；", details));
        }

        return new ShotProductionInputs(
            references.Select(item => item.ImageAssetId).Distinct().ToArray(),
            subjects
                .Where(item => item.Document.Kind == "prop")
                .Select(item => item.Asset.Id)
                .Distinct()
                .ToArray());
    }
}

public static class ShotProductionModes
{
    public const double ThresholdSeconds = 15;
    public const string DirectFirstFrame = "direct-first-frame";
    public const string FirstLastContinuous = "first-last-continuous";

    public static string ForDuration(double durationSeconds) =>
        durationSeconds <= ThresholdSeconds ? DirectFirstFrame : FirstLastContinuous;

    public static IReadOnlyList<string> Stages(string mode) => mode == FirstLastContinuous
        ? ["first-frame", "last-frame"]
        : ["first-frame"];

    internal static ShotProductionView ToView(
        ProductionRun run,
        IReadOnlyList<ProductionRunItem> items,
        double durationSeconds)
    {
        var outputAssetId = items.FirstOrDefault(item => item.Stage == "first-frame")?.OutputAssetId;
        return new(
            run.Id,
            ForDuration(durationSeconds),
            run.Status,
            run.CurrentStage,
            items.OrderBy(item => item.CreatedAtUtc).Select(item => item.Stage).Distinct().ToArray(),
            run.CreatedAtUtc,
            outputAssetId,
            outputAssetId is null
                ? null
                : $"/api/v2/projects/{run.ProjectId}/storyboard/frames/{outputAssetId}/content");
    }
}