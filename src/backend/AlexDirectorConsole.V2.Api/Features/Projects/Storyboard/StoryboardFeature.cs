using System.Security.Cryptography;
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
    IReadOnlyList<StoryboardHookDraft>? Hooks = null,
    string ProductionMode = "",
    string FrameStrategyReason = "",
    string FirstFrameDescription = "",
    string LastFrameDescription = "",
    string CutDescription = "");

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
    string? OutputUrl = null,
    string? OutputPrompt = null,
    Guid? LastFrameAssetId = null,
    string? LastFrameUrl = null,
    string? LastFramePrompt = null);

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
    string ProductionMode,
    string FrameStrategyReason,
    string FirstFrameDescription,
    string LastFrameDescription,
    string CutDescription,
    IReadOnlyList<StoryboardLinkedAssetView> LinkedAssets,
    StoryboardMediaPromptView? ImagePrompt,
    StoryboardMediaPromptView? VideoPrompt,
    StoryboardDialogueAudioView? DialogueAudio,
    ShotProductionView? Production,
    ShotVideoProductionView? VideoProduction,
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
    IReadOnlyList<StoryboardHookDraft>? Hooks = null,
    string ProductionMode = "",
    string FrameStrategyReason = "",
    string FirstFrameDescription = "",
    string LastFrameDescription = "",
    string CutDescription = "");

public interface IStoryboardDesigner
{
    Task<StoryboardDesignResult> DesignAsync(
        ProjectSettingsView settings,
        ProductionScriptPackageView scriptPackage,
        IReadOnlyList<VisualAssetView> assets,
        CancellationToken cancellationToken);
}

public sealed record StoryboardShotTextRevision(
    string VisualDescription,
    string Action,
    string FrameStrategyReason,
    string FirstFrameDescription,
    string LastFrameDescription,
    string CutDescription,
    string Dialogue,
    string Sound,
    string Model,
    string Runtime);

public interface IStoryboardShotTextRewriter
{
    Task<StoryboardShotTextRevision> RewriteAsync(
        StoryboardShotView shot,
        string instruction,
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

public sealed record UpdateStoryboardShotModeCommand(
    Guid ProjectId,
    Guid ProductionEpisodeId,
    Guid ShotResourceId,
    string ProductionMode)
    : ICommand<StoryboardView?>;

public sealed record UpdateStoryboardShotTextCommand(
    Guid ProjectId,
    Guid ProductionEpisodeId,
    Guid ShotResourceId,
    string Field,
    string Value)
    : ICommand<StoryboardView?>;

public sealed record RewriteStoryboardShotTextCommand(
    Guid ProjectId,
    Guid ProductionEpisodeId,
    Guid ShotResourceId,
    string Instruction)
    : ICommand<StoryboardView?>;

public sealed record StartShotProductionCommand(
    Guid ProjectId,
    Guid ProductionEpisodeId,
    Guid ShotResourceId,
    string ConfirmedPrompt,
    string? Instruction,
    bool FirstFrameOnly = false)
    : ICommand<ShotProductionView?>;

public sealed record UpdateStoryboardShotAssetsRequest(IReadOnlyList<Guid>? AssetResourceIds);
public sealed record UpdateStoryboardShotModeRequest(bool RequiresLastFrame);
public sealed record UpdateStoryboardShotTextRequest(string? Value);
public sealed record RewriteStoryboardShotTextRequest(string? Instruction);
public sealed record StartShotProductionRequest(string? ConfirmedPrompt, string? Instruction);

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
    IStoryboardDialogueAudioService dialogueAudioService,
    TimeProvider timeProvider,
    ILogger<GenerateStoryboardCommandHandler> logger)
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
        if (scriptPackage.Episode.Scenes.Any(scene => scene.ShotPlan is not { Count: > 0 }))
            throw new InvalidOperationException("正式剧本缺少拍摄计划，请从改编大纲重新生成正式剧本后再生成分镜。");

        var settings = await new GetProjectSettingsQueryHandler(dbContext).HandleAsync(
            new GetProjectSettingsQuery(command.ProjectId),
            cancellationToken);
        if (settings is null) return null;
        var visualAssets = await new ListVisualAssetsQueryHandler(dbContext).HandleAsync(
            new ListVisualAssetsQuery(command.ProjectId, null),
            cancellationToken);
        var result = await designer.DesignAsync(settings, scriptPackage, visualAssets, cancellationToken);
        var shots = Normalize(result.Shots, scriptPackage, visualAssets);
        var beatIds = BuildBeatIdQueues(scriptPackage);
        var previousClaims = await dbContext.ShotBeatClaims
            .Where(item => item.ScriptPackageAssetId == scriptPackage.AssetId)
            .ToListAsync(cancellationToken);
        dbContext.ShotBeatClaims.RemoveRange(previousClaims);

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
                NormalizeHooks(shot.Hooks),
                ShotProductionModes.Normalize(shot.ProductionMode),
                shot.FrameStrategyReason.Trim(),
                shot.FirstFrameDescription.Trim(),
                shot.LastFrameDescription.Trim(),
                shot.CutDescription.Trim());
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
            var ordinalInShot = 0;
            foreach (var hook in document.Hooks ?? [])
            {
                var key = HookKey(hook);
                if (!beatIds.TryGetValue(key, out var availableBeatIds)
                    || !availableBeatIds.TryDequeue(out var beatId))
                {
                    throw new InvalidOperationException("分镜爆点无法映射到正式剧本 BeatId。");
                }
                dbContext.ShotBeatClaims.Add(new ShotBeatClaim
                {
                    ProjectId = command.ProjectId,
                    ProductionEpisodeId = command.ProductionEpisodeId,
                    ScriptPackageAssetId = scriptPackage.AssetId,
                    BeatId = beatId,
                    ShotAssetId = shotAsset.Id,
                    ShotResourceId = resourceId,
                    OrdinalInShot = ordinalInShot++
                });
            }

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
        var dialogueResult = await dialogueAudioService.GenerateMissingAsync(
            command.ProjectId,
            command.ProductionEpisodeId,
            cancellationToken);
        if (dialogueResult.Failed > 0)
        {
            logger.LogWarning(
                "Storyboard dialogue TTS completed with {FailureCount} failures: {Errors}",
                dialogueResult.Failed,
                string.Join(" | ", dialogueResult.Errors));
        }
        return await StoryboardQueries.GetAsync(
            dbContext,
            command.ProjectId,
            command.ProductionEpisodeId,
            cancellationToken);
    }

    private static StoryboardShotDraft[] Normalize(
        IReadOnlyList<StoryboardShotDraft> proposed,
        ProductionScriptPackageView scriptPackage,
        IReadOnlyList<VisualAssetView> visualAssets)
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
        if (proposed.Any(item => !ShotProductionModes.IsSupported(item.ProductionMode)))
            throw new InvalidOperationException("每个镜头必须分析并指定仅首帧或首尾帧模式。");
        if (proposed.Any(item => string.IsNullOrWhiteSpace(item.FrameStrategyReason)
            || string.IsNullOrWhiteSpace(item.FirstFrameDescription)
            || string.IsNullOrWhiteSpace(item.CutDescription)))
            throw new InvalidOperationException("每个镜头必须包含帧策略理由、首帧描述和 cut 级执行描述。");
        if (proposed.Any(item => item.ProductionMode == ShotProductionModes.FirstLastContinuous
            && string.IsNullOrWhiteSpace(item.LastFrameDescription)))
            throw new InvalidOperationException("首尾帧镜头必须包含明确的尾帧描述。");

        ValidateHooks(proposed, scriptPackage);

        if (scriptPackage.Episode.Scenes.Any(scene => scene.ShotPlan is not { Count: > 0 }))
            throw new InvalidOperationException("正式剧本缺少拍摄计划，请从改编大纲重新生成正式剧本后再生成分镜。");

        var plannedShots = scriptPackage.Episode.Scenes
            .SelectMany(scene => scene.ShotPlan!.Select(shot => (scene.SceneNumber, Shot: shot)))
            .ToDictionary(item => (item.SceneNumber, item.Shot.ShotNumber), item => item.Shot);
        var proposedKeys = proposed.Select(item => (item.SceneNumber, item.ShotNumber)).ToHashSet();
        if (!proposedKeys.SetEquals(plannedShots.Keys))
            throw new InvalidOperationException("分镜镜号必须与正式剧本中的镜头计划完全一致。");

        ValidateScriptCoverage(proposed, scriptPackage);

        return proposed
            .OrderBy(item => item.SceneNumber)
            .ThenBy(item => item.ShotNumber)
            .Select(item =>
            {
                var plan = plannedShots[(item.SceneNumber, item.ShotNumber)];
                return item with
                {
                    DurationSeconds = plan.DurationSeconds,
                    ShotSize = plan.ShotSize,
                    CameraAngle = plan.CameraAngle,
                    CameraMovement = plan.CameraMovement,
                    Props = NormalizePropNames(item.Props, visualAssets)
                };
            })
            .ToArray();
    }

    private static void ValidateScriptCoverage(
        IReadOnlyList<StoryboardShotDraft> shots,
        ProductionScriptPackageView scriptPackage)
    {
        foreach (var scene in scriptPackage.Episode.Scenes)
        {
            var sceneShots = shots.Where(shot => shot.SceneNumber == scene.SceneNumber).ToArray();
            if (sceneShots.All(shot => string.IsNullOrWhiteSpace(shot.Action)))
                throw new InvalidOperationException($"分镜未呈现正式剧本第 {scene.SceneNumber} 场的动作。");

            var storyboardDialogue = string.Join("\n", sceneShots.Select(shot => shot.Dialogue));
            var missingLine = (scene.Dialogues ?? [])
                .SelectMany(dialogue => dialogue.Lines ?? [])
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)
                    && !storyboardDialogue.Contains(line.Trim(), StringComparison.Ordinal));
            if (missingLine is not null)
                throw new InvalidOperationException($"分镜遗漏正式剧本第 {scene.SceneNumber} 场对白：{missingLine}");
        }
    }

    private static string[] NormalizeNames(IReadOnlyList<string> values) => values
        .Select(item => item.Trim())
        .Where(item => item.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string[] NormalizePropNames(
        IReadOnlyList<string> values,
        IReadOnlyList<VisualAssetView> visualAssets) => values
        .SelectMany(name => visualAssets
            .Where(asset => asset.Kind == "prop" && NamesMatch(asset.Name, name))
            .Select(asset => asset.Name))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(2)
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

    private static Dictionary<string, Queue<Guid>> BuildBeatIdQueues(
        ProductionScriptPackageView scriptPackage)
    {
        var hooks = (scriptPackage.Episode.SmallHooks ?? [])
            .Select(item => new StoryboardHookDraft("small", item.Trim()))
            .Concat((scriptPackage.Episode.BigHooks ?? [])
                .Select(item => new StoryboardHookDraft("big", item.Trim())))
            .Where(item => item.Description.Length > 0)
            .ToArray();
        return hooks
            .Select((hook, index) => new
            {
                Key = HookKey(hook),
                BeatId = CreateBeatId(scriptPackage.AssetId, hook, index)
            })
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new Queue<Guid>(group.Select(item => item.BeatId)),
                StringComparer.Ordinal);
    }

    private static string HookKey(StoryboardHookDraft hook) =>
        $"{hook.Type}\n{hook.Description}";

    private static Guid CreateBeatId(
        Guid scriptPackageAssetId,
        StoryboardHookDraft hook,
        int ordinal)
    {
        var bytes = Encoding.UTF8.GetBytes(
            $"{scriptPackageAssetId:N}\n{hook.Type}\n{ordinal}\n{hook.Description}");
        return new Guid(SHA256.HashData(bytes).AsSpan(0, 16));
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
        var shotResourceIdsByAssetId = definitions.ToDictionary(item => item.ShotAssetId, item => item.ShotResourceId);
        var imagePrompts = await StoryboardMediaPromptQueries.GetCurrentByShotAsync(
            dbContext,
            projectId,
            shotResourceIdsByAssetId,
            StoryboardMediaPromptService.ImageKind,
            cancellationToken);
        var videoPrompts = await StoryboardMediaPromptQueries.GetCurrentByShotAsync(
            dbContext,
            projectId,
            shotResourceIdsByAssetId,
            StoryboardMediaPromptService.VideoKind,
            cancellationToken);
        var dialogueAudios = await StoryboardDialogueAudioQueries.GetCurrentByShotAsync(
            dbContext,
            projectId,
            shotResourceIdsByAssetId,
            cancellationToken);
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
            var videoProduction = await ShotVideoQueries.GetAsync(
                dbContext,
                projectId,
                productionEpisodeId,
                definition.ShotResourceId,
                cancellationToken);
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
                ShotProductionModes.ForShot(document),
                document.FrameStrategyReason,
                string.IsNullOrWhiteSpace(document.FirstFrameDescription)
                    ? document.VisualDescription
                    : document.FirstFrameDescription,
                document.LastFrameDescription,
                string.IsNullOrWhiteSpace(document.CutDescription)
                    ? document.Action
                    : document.CutDescription,
                linkedAssets,
                imagePrompts.GetValueOrDefault(definition.ShotResourceId),
                videoPrompts.GetValueOrDefault(definition.ShotResourceId),
                dialogueAudios.GetValueOrDefault(definition.ShotResourceId),
                production,
                videoProduction,
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
                && item.ShotResourceId == definition.ShotResourceId)
            .ToListAsync(cancellationToken);
        if (items.Count == 0) return null;
        var runIds = items.Select(item => item.RunId).Distinct().ToArray();
        var runs = await dbContext.ProductionRuns.AsNoTracking()
            .Where(item => runIds.Contains(item.Id) && item.RunType == "shot-frames")
            .ToListAsync(cancellationToken);
        if (runs.Count == 0) return null;
        var run = runs.OrderByDescending(item => item.CreatedAtUtc).First();
        var runItems = items.Where(item => item.RunId == run.Id).ToArray();
        var outputAssetIds = runItems
            .Where(item => item.OutputAssetId is not null)
            .Select(item => item.OutputAssetId!.Value)
            .ToArray();
        var sourceOutputs = await dbContext.Assets.AsNoTracking()
            .Where(item => outputAssetIds.Contains(item.Id)
                && item.ProjectId == definition.ProjectId
                && item.Type == ShotFrameService.AssetType)
            .ToListAsync(cancellationToken);
        var outputResourceIds = sourceOutputs.Select(item => item.ResourceId).ToArray();
        var currentOutputIds = await dbContext.ResourceStates.AsNoTracking()
            .Where(item => item.ProjectId == definition.ProjectId
                && item.ResourceType == ShotFrameService.AssetType
                && outputResourceIds.Contains(item.ResourceId))
            .ToDictionaryAsync(item => item.ResourceId, item => item.CurrentAssetId, cancellationToken);
        var currentOutputs = await dbContext.Assets.AsNoTracking()
            .Where(item => currentOutputIds.Values.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var resolvedOutputs = sourceOutputs.ToDictionary(
            item => item.Id,
            item => currentOutputIds.TryGetValue(item.ResourceId, out var currentId)
                && currentOutputs.TryGetValue(currentId, out var current)
                    ? current
                    : item);
        var view = ShotProductionModes.ToView(
            run,
            runItems,
            definition.DurationSeconds,
            resolvedOutputs);
        var shotAsset = await dbContext.Assets.AsNoTracking().SingleAsync(
            item => item.Id == definition.ShotAssetId,
            cancellationToken);
        var shot = JsonSerializer.Deserialize<StoryboardShotDocument>(
            shotAsset.DocumentJson ?? "{}",
            StoryboardDefaults.JsonOptions)
            ?? throw new InvalidOperationException("当前镜头内容无法读取。");
        return view.Mode == ShotProductionModes.DirectFirstFrame
            || ShotProductionModes.ForShot(shot) == ShotProductionModes.DirectFirstFrame
            ? view with
            {
                Mode = ShotProductionModes.DirectFirstFrame,
                LastFrameAssetId = null,
                LastFrameUrl = null,
                LastFramePrompt = null
            }
            : view with { Mode = ShotProductionModes.FirstLastContinuous };
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
        if (!LlmChatClientFactory.IsConfigured(configuration))
            throw new ProjectGenerationConfigurationException("请先在系统设置中配置语言模型。");

        var agent = LlmChatClientFactory
            .Create(configuration!, dataProtectionProvider)
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
                            你是动画导演和分镜师。将当前正式剧本中的上游拍摄计划细化为可生产的结构化镜头。
                            不得改写事件顺序、人物身份和对白。每个镜头必须属于输入中的真实场次，sceneNumber 必须原样使用；shotNumber 在每场从 1 连续编号。
                            episode.scenes[].shotPlan 是正式剧本已确定的摄影骨架。必须逐镜保留其中的 sceneNumber、shotNumber、durationSeconds、shotSize、cameraAngle 和 cameraMovement，不得新增、删除、合并、拆分或重新定时；只负责细化构图、画面、动作、对白与声音。
                            必须把 episode.scenes[].action 分解落实到本场镜头动作中；episode.scenes[].dialogues 中每一句 lines 台词都必须逐字出现在本场某个镜头的 dialogue 中，不得遗漏、改写或新增剧情信息。
                            characters 只能使用输入剧本或资产中的名称。props 不是画面物件清单，只用于声明生成时必须加载设定图以保持外观连续的特殊道具；props 只能逐字使用 input.specialPropNames 中的名称，不得从剧本 props 自行抄录其他物件。通常每镜最多 1 个；只有两个特殊道具在同一动作中同时被操作且都需要外观连续时才可写 2 个。普通武器、家具、交通工具、钱袋、衣物、食物、工具、布景，以及仅出现但未推动本镜动作的物件一律不写。若没有必须加载设定图的特殊道具，返回空数组。
                            必须逐镜分析帧生成策略。productionMode 只能是 direct-first-frame 或 first-last-continuous。若主体方向、位置、姿态、表情、遮挡关系或关键道具状态在镜头结尾发生必须被明确控制的可见变化（例如背对转为正面、开门前后、交接前后、起身或倒下），使用 first-last-continuous；若主体保持同一朝向和主要状态，动作可由单一首帧自然延展，则使用 direct-first-frame。不得按时长机械判断。
                            frameStrategyReason 用一句具体中文说明为什么只需首帧或必须首尾帧。firstFrameDescription 必须写清镜头开始瞬间每个主体的位置、朝向、姿态、视线、手部/道具状态、前中后景关系和光线。first-last-continuous 时 lastFrameDescription 必须写清结束瞬间相对于首帧的可见变化；direct-first-frame 时返回空字符串。
                            cutDescription 必须达到实际拍摄 cut 的执行粒度：按时间顺序描述起始画面、演员调度、动作节拍、视线与轴线、摄影机运动的起止和速度、焦点转移、画面结束点；不得只复述剧情，不得使用“展现冲突”“营造氛围”等不可执行措辞。
                            episode.smallHooks 和 episode.bigHooks 是当前剧本中的爆点。必须根据事件内容把每条爆点落实到最能体现它的一个具体镜头：在该镜头 hooks 中写入 type（small 或 big）和 description。description 必须逐字复制输入爆点，不得改写、遗漏、新增或重复；允许一个镜头承载多条爆点，无爆点镜头返回空数组。
                            只返回 JSON，不要 Markdown。结构：
                            {"shots":[{"sceneNumber":1,"shotNumber":1,"durationSeconds":3.5,"shotSize":"全景","cameraAngle":"平视","cameraMovement":"固定","composition":"...","visualDescription":"...","action":"...","dialogue":"...","sound":"...","characters":["..."],"props":[],"hooks":[{"type":"small","description":"逐字复制的既有爆点"}],"productionMode":"direct-first-frame","frameStrategyReason":"主体始终朝向门口，姿态与空间关系无必须锁定的终态变化。","firstFrameDescription":"...","lastFrameDescription":"","cutDescription":"0.0-1.0 秒……；1.0-3.5 秒……；切在……"}]}
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
            specialPropNames = assets.Where(item => item.Kind == "prop").Select(item => item.Name),
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
        return new(payload.Shots, LlmChatClientFactory.GetModel(configuration!), "MAF HarnessAgent");
    }

    private sealed class StoryboardPayload
    {
        public List<StoryboardShotDraft> Shots { get; set; } = [];
    }
}
#pragma warning restore MAAI001

#pragma warning disable MAAI001
public sealed class MafStoryboardShotTextRewriter(
    V2DbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    ILoggerFactory loggerFactory) : IStoryboardShotTextRewriter
{
    public async Task<StoryboardShotTextRevision> RewriteAsync(
        StoryboardShotView shot,
        string instruction,
        CancellationToken cancellationToken)
    {
        var configuration = await dbContext.FoundryConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (!LlmChatClientFactory.IsConfigured(configuration))
            throw new ProjectGenerationConfigurationException("请先在系统设置中配置语言模型。");
        var agent = LlmChatClientFactory.Create(configuration!, dataProtectionProvider)
            .AsIChatClient()
            .AsHarnessAgent(new HarnessAgentOptions
            {
                Name = "AlexStoryboardShotRewriter",
                MaxContextWindowTokens = 1_050_000,
                MaxOutputTokens = 6_000,
                MaximumIterationsPerRequest = 2,
                DisableFileMemory = true,
                DisableWebSearch = true,
                DisableTodoProvider = true,
                DisableAgentModeProvider = true,
                DisableAgentSkillsProvider = true,
                ChatOptions = new ChatOptions
                {
                    Instructions = """
                        你是动画导演和分镜师。只按用户意见重写输入中的单个镜头执行文本，不改变镜号、时长、景别、机位、运镜、人物、道具、剧情事实和 productionMode。
                        firstFrameDescription 必须写清主体位置、朝向、姿态、视线、手部/道具、前中后景和光线。productionMode 为 first-last-continuous 时 lastFrameDescription 必须明确相对首帧的结束变化；direct-first-frame 时必须为空字符串。
                        cutDescription 必须按时间顺序写清动作节拍、演员调度、摄影机运动和切出点。只返回 JSON，不要 Markdown。
                        """,
                    MaxOutputTokens = 6_000
                }
            }, loggerFactory);
        var input = JsonSerializer.Serialize(new { instruction, shot }, StoryboardDefaults.JsonOptions);
        var response = await agent.RunAsync($"重写这个镜头的执行文本：\n{input}", cancellationToken: cancellationToken);
        var text = response.Text?.Trim() ?? string.Empty;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidOperationException("GPT-5.4 未返回 JSON 镜头文本。");
        var payload = JsonSerializer.Deserialize<ShotTextPayload>(text[start..(end + 1)], StoryboardDefaults.JsonOptions)
            ?? throw new InvalidOperationException("GPT-5.4 未返回有效镜头文本。");
        if (string.IsNullOrWhiteSpace(payload.VisualDescription)
            || string.IsNullOrWhiteSpace(payload.Action)
            || string.IsNullOrWhiteSpace(payload.FrameStrategyReason)
            || string.IsNullOrWhiteSpace(payload.FirstFrameDescription)
            || string.IsNullOrWhiteSpace(payload.CutDescription))
            throw new InvalidOperationException("Agent 返回的镜头文本缺少必填内容。");
        if (shot.ProductionMode == ShotProductionModes.FirstLastContinuous
            && string.IsNullOrWhiteSpace(payload.LastFrameDescription))
            throw new InvalidOperationException("首尾帧模式必须包含尾帧描述。");
        return new(
            payload.VisualDescription.Trim(), payload.Action.Trim(), payload.FrameStrategyReason.Trim(),
            payload.FirstFrameDescription.Trim(), shot.ProductionMode == ShotProductionModes.DirectFirstFrame ? "" : payload.LastFrameDescription.Trim(),
            payload.CutDescription.Trim(), payload.Dialogue.Trim(), payload.Sound.Trim(),
            LlmChatClientFactory.GetModel(configuration!), "MAF HarnessAgent");
    }

    private sealed record ShotTextPayload(
        string VisualDescription = "", string Action = "", string FrameStrategyReason = "",
        string FirstFrameDescription = "", string LastFrameDescription = "", string CutDescription = "",
        string Dialogue = "", string Sound = "");
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
            try
            {
                var storyboard = await dispatcher.SendAsync(
                    new GenerateStoryboardCommand(projectId, productionEpisodeId),
                    cancellationToken);
                return storyboard is null ? Results.NotFound() : Results.Ok(storyboard);
            }
            catch (InvalidOperationException error)
            {
                return Results.Conflict(new { error = error.Message });
            }
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
        app.MapPut($"{route}/shots/{{shotResourceId:guid}}/mode", async (
            Guid projectId,
            Guid productionEpisodeId,
            Guid shotResourceId,
            UpdateStoryboardShotModeRequest request,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            var storyboard = await dispatcher.SendAsync(
                new UpdateStoryboardShotModeCommand(
                    projectId,
                    productionEpisodeId,
                    shotResourceId,
                    request.RequiresLastFrame
                        ? ShotProductionModes.FirstLastContinuous
                        : ShotProductionModes.DirectFirstFrame),
                cancellationToken);
            return storyboard is null ? Results.NotFound() : Results.Ok(storyboard);
        });
        app.MapPut($"{route}/shots/{{shotResourceId:guid}}/text/{{field}}", async (
            Guid projectId,
            Guid productionEpisodeId,
            Guid shotResourceId,
            string field,
            UpdateStoryboardShotTextRequest request,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var storyboard = await dispatcher.SendAsync(
                    new UpdateStoryboardShotTextCommand(
                        projectId,
                        productionEpisodeId,
                        shotResourceId,
                        field,
                        request.Value ?? string.Empty),
                    cancellationToken);
                return storyboard is null ? Results.NotFound() : Results.Ok(storyboard);
            }
            catch (InvalidOperationException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
        });
        app.MapPost($"{route}/shots/{{shotResourceId:guid}}/rewrite", async (
            Guid projectId,
            Guid productionEpisodeId,
            Guid shotResourceId,
            RewriteStoryboardShotTextRequest request,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Instruction))
                return Results.BadRequest(new { error = "请输入镜头文本修改意见。" });
            try
            {
                var storyboard = await dispatcher.SendAsync(
                    new RewriteStoryboardShotTextCommand(
                        projectId,
                        productionEpisodeId,
                        shotResourceId,
                        request.Instruction.Trim()),
                    cancellationToken);
                return storyboard is null ? Results.NotFound() : Results.Ok(storyboard);
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
                        request.ConfirmedPrompt,
                        request.Instruction),
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
            string? instruction,
            IShotFrameService frameService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var preview = await frameService.PreviewFirstFrameAsync(
                    projectId,
                    productionEpisodeId,
                    shotResourceId,
                    instruction,
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

public sealed class UpdateStoryboardShotModeCommandHandler(
    V2DbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<UpdateStoryboardShotModeCommand, StoryboardView?>
{
    public async Task<StoryboardView?> HandleAsync(
        UpdateStoryboardShotModeCommand command,
        CancellationToken cancellationToken)
    {
        var context = await StoryboardShotVersioning.LoadAsync(
            dbContext, command.ProjectId, command.ProductionEpisodeId, command.ShotResourceId, cancellationToken);
        if (context is null) return null;
        var mode = ShotProductionModes.Normalize(command.ProductionMode);
        if (context.Document.ProductionMode == mode)
            return await StoryboardQueries.GetAsync(dbContext, command.ProjectId, command.ProductionEpisodeId, cancellationToken);
        var updated = context.Document with
        {
            ProductionMode = mode,
            FrameStrategyReason = mode == ShotProductionModes.FirstLastContinuous
                ? "手动指定需要尾帧，以明确约束镜头结束状态。"
                : "手动指定只需要首帧，动作由首帧自然延展。",
            LastFrameDescription = mode == ShotProductionModes.FirstLastContinuous
                ? string.IsNullOrWhiteSpace(context.Document.LastFrameDescription)
                    ? $"镜头结束时，{context.Document.Action}"
                    : context.Document.LastFrameDescription
                : ""
        };
        await StoryboardShotVersioning.SaveAsync(
            dbContext, context, updated, "manual-frame-strategy", timeProvider.GetUtcNow(), cancellationToken);
        return await StoryboardQueries.GetAsync(dbContext, command.ProjectId, command.ProductionEpisodeId, cancellationToken);
    }
}

public sealed class RewriteStoryboardShotTextCommandHandler(
    V2DbContext dbContext,
    IStoryboardShotTextRewriter rewriter,
    TimeProvider timeProvider)
    : ICommandHandler<RewriteStoryboardShotTextCommand, StoryboardView?>
{
    public async Task<StoryboardView?> HandleAsync(
        RewriteStoryboardShotTextCommand command,
        CancellationToken cancellationToken)
    {
        var storyboard = await StoryboardQueries.GetAsync(
            dbContext, command.ProjectId, command.ProductionEpisodeId, cancellationToken);
        var shot = storyboard?.Shots.SingleOrDefault(item => item.ResourceId == command.ShotResourceId);
        if (shot is null) return null;
        var context = await StoryboardShotVersioning.LoadAsync(
            dbContext, command.ProjectId, command.ProductionEpisodeId, command.ShotResourceId, cancellationToken)
            ?? throw new InvalidOperationException("镜头不存在。");
        var revision = await rewriter.RewriteAsync(shot, command.Instruction, cancellationToken);
        var updated = context.Document with
        {
            VisualDescription = revision.VisualDescription,
            Action = revision.Action,
            FrameStrategyReason = revision.FrameStrategyReason,
            FirstFrameDescription = revision.FirstFrameDescription,
            LastFrameDescription = revision.LastFrameDescription,
            CutDescription = revision.CutDescription,
            Dialogue = context.Document.Dialogue,
            Sound = revision.Sound,
            Model = revision.Model,
            Runtime = revision.Runtime
        };
        await StoryboardShotVersioning.SaveAsync(
            dbContext, context, updated, command.Instruction, timeProvider.GetUtcNow(), cancellationToken);
        return await StoryboardQueries.GetAsync(dbContext, command.ProjectId, command.ProductionEpisodeId, cancellationToken);
    }
}

public sealed class UpdateStoryboardShotTextCommandHandler(
    V2DbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<UpdateStoryboardShotTextCommand, StoryboardView?>
{
    public async Task<StoryboardView?> HandleAsync(
        UpdateStoryboardShotTextCommand command,
        CancellationToken cancellationToken)
    {
        var context = await StoryboardShotVersioning.LoadAsync(
            dbContext, command.ProjectId, command.ProductionEpisodeId, command.ShotResourceId, cancellationToken);
        if (context is null) return null;
        var field = StoryboardShotTextFields.Normalize(command.Field)
            ?? throw new InvalidOperationException("不支持编辑这个镜头字段。");
        var updated = StoryboardShotTextFields.Apply(context.Document, field, command.Value);
        if (updated == context.Document)
            return await StoryboardQueries.GetAsync(dbContext, command.ProjectId, command.ProductionEpisodeId, cancellationToken);
        await StoryboardShotVersioning.SaveAsync(
            dbContext, context, updated, $"manual-edit:{field}", timeProvider.GetUtcNow(), cancellationToken);
        return await StoryboardQueries.GetAsync(dbContext, command.ProjectId, command.ProductionEpisodeId, cancellationToken);
    }
}

internal static class StoryboardShotTextFields
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "visualDescription", "firstFrameDescription",
        "lastFrameDescription", "cutDescription", "dialogue", "sound"
    };

    public static string? Normalize(string? field) =>
        string.IsNullOrWhiteSpace(field) || !Supported.Contains(field)
            ? null
            : Supported.Single(item => item.Equals(field, StringComparison.OrdinalIgnoreCase));

    public static string Label(string field) => field switch
    {
        "visualDescription" => "镜头描述",
        "firstFrameDescription" => "首帧描述",
        "lastFrameDescription" => "尾帧描述",
        "cutDescription" => "CUT 执行描述",
        "dialogue" => "对白",
        "sound" => "声音",
        _ => throw new InvalidOperationException("不支持编辑这个镜头字段。")
    };

    public static StoryboardShotDocument Apply(StoryboardShotDocument document, string field, string value)
    {
        var normalized = value.Trim();
        if (field is not ("dialogue" or "sound") && string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException($"{Label(field)}不能为空。");
        return field switch
        {
            "visualDescription" => document with { VisualDescription = normalized },
            "firstFrameDescription" => document with { FirstFrameDescription = normalized },
            "lastFrameDescription" => document with { LastFrameDescription = normalized },
            "cutDescription" => document with { CutDescription = normalized },
            "dialogue" => document with { Dialogue = normalized },
            "sound" => document with { Sound = normalized },
            _ => throw new InvalidOperationException("不支持编辑这个镜头字段。")
        };
    }
}

internal static class StoryboardShotVersioning
{
    internal sealed record Context(ShotDefinition Definition, Asset Asset, ResourceState State, StoryboardShotDocument Document);

    public static async Task<Context?> LoadAsync(
        V2DbContext dbContext,
        Guid projectId,
        Guid productionEpisodeId,
        Guid shotResourceId,
        CancellationToken cancellationToken)
    {
        var definition = await dbContext.ShotDefinitions.SingleOrDefaultAsync(item =>
            item.ProjectId == projectId && item.ProductionEpisodeId == productionEpisodeId && item.ShotResourceId == shotResourceId,
            cancellationToken);
        if (definition is null) return null;
        var asset = await dbContext.Assets.SingleAsync(item => item.Id == definition.ShotAssetId, cancellationToken);
        var state = await dbContext.ResourceStates.SingleAsync(item => item.ProjectId == projectId && item.ResourceId == shotResourceId, cancellationToken);
        var document = JsonSerializer.Deserialize<StoryboardShotDocument>(asset.DocumentJson ?? "{}", StoryboardDefaults.JsonOptions)
            ?? throw new InvalidOperationException("当前镜头内容无法读取。");
        return new(definition, asset, state, document);
    }

    public static async Task SaveAsync(
        V2DbContext dbContext,
        Context context,
        StoryboardShotDocument document,
        string instruction,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(document, StoryboardDefaults.JsonOptions);
        var asset = new Asset
        {
            ProjectId = context.Asset.ProjectId,
            ProductionEpisodeId = context.Asset.ProductionEpisodeId,
            ResourceId = context.Asset.ResourceId,
            Version = context.Asset.Version + 1,
            Number = context.Asset.Number,
            Type = context.Asset.Type,
            Name = context.Asset.Name,
            DocumentJson = json,
            ContentType = "application/json",
            SizeBytes = Encoding.UTF8.GetByteCount(json),
            GenerationMetadataJson = JsonSerializer.Serialize(new { instruction, document.Model, document.Runtime }, StoryboardDefaults.JsonOptions),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.Assets.Add(asset);
        context.Definition.ShotAssetId = asset.Id;
        context.Definition.UpdatedAtUtc = now;
        context.State.CurrentAssetId = asset.Id;
        context.State.IsStale = false;
        context.State.UpdatedAtUtc = now;
        var claims = await dbContext.ShotBeatClaims
            .Where(item => item.ProjectId == context.Asset.ProjectId
                && item.ShotResourceId == context.Asset.ResourceId)
            .ToListAsync(cancellationToken);
        foreach (var claim in claims) claim.ShotAssetId = asset.Id;
        dbContext.AssetDependencies.Add(new AssetDependency
        {
            ProjectId = context.Asset.ProjectId,
            ConsumerAssetId = asset.Id,
            SourceAssetId = context.Definition.ScriptPackageAssetId,
            Role = "derived-from-script",
            IsRequired = true,
            CreatedAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
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

        var project = await dbContext.Projects.AsNoTracking().SingleAsync(
            item => item.Id == command.ProjectId,
            cancellationToken);
        if (project.CurrentCreativeSettingsId is not Guid creativeSettingsAssetId)
            throw new InvalidOperationException("开始制作前必须先保存项目设定。");
        var shotAsset = await dbContext.Assets.AsNoTracking().SingleAsync(
            item => item.Id == definition.ShotAssetId,
            cancellationToken);
        var shotDocument = JsonSerializer.Deserialize<StoryboardShotDocument>(
            shotAsset.DocumentJson ?? "{}",
            StoryboardDefaults.JsonOptions)
            ?? throw new InvalidOperationException("当前镜头内容无法读取。");
        var mode = command.FirstFrameOnly
            ? ShotProductionModes.DirectFirstFrame
            : ShotProductionModes.ForShot(shotDocument);
        var reusableFirstFrame = mode == ShotProductionModes.FirstLastContinuous
            ? await frameService.ResolveCurrentFrameAsync(
                command.ProjectId,
                command.ShotResourceId,
                "frame-for-shot",
                cancellationToken)
            : null;
        var preflight = await ShotProductionPreflight.EvaluateAsync(
            dbContext,
            definition,
            cancellationToken);
        if (!preflight.Passed && reusableFirstFrame?.BlobContent is not null)
        {
            preflight = ShotProductionPreflight.ForLastFrameReuse(preflight.Inputs, reusableFirstFrame);
        }
        if (!preflight.Passed)
        {
            CreateValidationRun(
                command,
                shotAsset.Id,
                preflight,
                timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException(preflight.FailureMessage);
        }

        var preview = await frameService.PreviewFirstFrameAsync(
            command.ProjectId,
            command.ProductionEpisodeId,
            command.ShotResourceId,
            creativeSettingsAssetId,
            preflight.Inputs.ReferenceImageAssetIds,
            preflight.Inputs.PropAssetIds,
            command.Instruction,
            cancellationToken)
            ?? throw new InvalidOperationException("镜头不存在。");
        if (!string.Equals(command.ConfirmedPrompt, preview.Prompt, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("镜头、项目设定或参考资产已变化，请重新预览并确认提示词。");
        }
        var validationRun = CreateValidationRun(
            command,
            shotAsset.Id,
            preflight,
            timeProvider.GetUtcNow());

        var productionInputs = preflight.Inputs;
        var inputAssetIds = productionInputs.ReferenceImageAssetIds
            .Concat(productionInputs.PropAssetIds)
            .Append(shotAsset.Id)
            .Append(creativeSettingsAssetId)
            .Distinct()
            .ToArray();
        var stages = ShotProductionModes.Stages(mode);
        var now = timeProvider.GetUtcNow();
        var run = new ProductionRun
        {
            ProjectId = command.ProjectId,
            ProductionEpisodeId = command.ProductionEpisodeId,
            ScriptPackageAssetId = definition.ScriptPackageAssetId,
            CreativeSettingsAssetId = creativeSettingsAssetId,
            PreflightValidationRunId = validationRun.Id,
            Status = "queued",
            CurrentStage = stages[0],
            SpecJson = JsonSerializer.Serialize(new
            {
                mode,
                durationSeconds = definition.DurationSeconds,
                shotDocument.FrameStrategyReason,
                command.ShotResourceId,
                userInstruction = string.IsNullOrWhiteSpace(command.Instruction) ? null : command.Instruction.Trim(),
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
                Status = stages[index] == "first-frame" && reusableFirstFrame is not null
                    ? "completed"
                    : reusableFirstFrame is not null || index == 0 ? "queued" : "waiting",
                Attempt = 0,
                OutputAssetId = stages[index] == "first-frame" ? reusableFirstFrame?.Id : null,
                CompletedAtUtc = stages[index] == "first-frame" && reusableFirstFrame is not null ? now : null,
                InputAssetIdsJson = JsonSerializer.Serialize(inputAssetIds, StoryboardDefaults.JsonOptions),
                CreatedAtUtc = now
            });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        if (reusableFirstFrame is null)
        {
            await frameService.GenerateFirstFrameAsync(run.Id, command.ConfirmedPrompt, cancellationToken);
        }
        if (mode == ShotProductionModes.FirstLastContinuous)
        {
            await frameService.GenerateLastFrameAsync(run.Id, cancellationToken);
        }
        var completedItems = await dbContext.ProductionRunItems.AsNoTracking()
            .Where(item => item.RunId == run.Id)
            .ToListAsync(cancellationToken);
        var outputAssetIds = completedItems
            .Where(item => item.OutputAssetId is not null)
            .Select(item => item.OutputAssetId!.Value)
            .ToArray();
        var outputAssets = await dbContext.Assets.AsNoTracking()
            .Where(item => outputAssetIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        return ShotProductionModes.ToView(run, completedItems, definition.DurationSeconds, outputAssets);
    }

    private ValidationRun CreateValidationRun(
        StartShotProductionCommand command,
        Guid shotAssetId,
        ShotProductionPreflightReport preflight,
        DateTimeOffset now)
    {
        var validationRun = new ValidationRun
        {
            ProjectId = command.ProjectId,
            ProductionEpisodeId = command.ProductionEpisodeId,
            SubjectAssetId = shotAssetId,
            ValidatorSet = "shot-production-preflight",
            ValidatorVersion = "1.0",
            Status = "completed",
            StartedAtUtc = now,
            CompletedAtUtc = now
        };
        dbContext.ValidationRuns.Add(validationRun);
        foreach (var gate in preflight.Gates)
        {
            dbContext.ValidationResults.Add(new ValidationResult
            {
                ValidationRunId = validationRun.Id,
                GateId = gate.GateId,
                Severity = gate.Status == "pass" ? "info" : "blocker",
                Status = gate.Status,
                Message = gate.Message,
                SubjectType = StoryboardDefaults.AssetType,
                SubjectId = command.ShotResourceId,
                ReferencesJson = JsonSerializer.Serialize(gate.ReferenceAssetIds, StoryboardDefaults.JsonOptions),
                SuggestedAction = gate.SuggestedAction
            });
        }
        return validationRun;
    }
}

internal sealed record ShotProductionInputs(
    IReadOnlyList<Guid> ReferenceImageAssetIds,
    IReadOnlyList<Guid> PropAssetIds);

internal sealed record ShotProductionPreflightGate(
    string GateId,
    string Status,
    string Message,
    string? SuggestedAction,
    IReadOnlyList<Guid> ReferenceAssetIds);

internal sealed record ShotProductionPreflightReport(
    ShotProductionInputs Inputs,
    IReadOnlyList<ShotProductionPreflightGate> Gates)
{
    public bool Passed => Gates.All(item => item.Status == "pass");
    public string FailureMessage => Gates.First(item => item.Status != "pass").Message;
}

internal static class ShotProductionPreflight
{
    public static ShotProductionPreflightReport ForLastFrameReuse(
        ShotProductionInputs inputs,
        Asset firstFrame) =>
        new(
            inputs,
            [
                new(
                    "shot.first-frame-reused",
                    "pass",
                    "复用已生成首帧作为尾帧场景与连续性锚点。",
                    null,
                    [firstFrame.Id])
            ]);

    public static async Task<ShotProductionInputs> ResolveAsync(
        V2DbContext dbContext,
        ShotDefinition definition,
        CancellationToken cancellationToken)
    {
        var report = await EvaluateAsync(dbContext, definition, cancellationToken);
        if (!report.Passed)
        {
            throw new InvalidOperationException(report.FailureMessage);
        }
        return report.Inputs;
    }

    public static async Task<ShotProductionPreflightReport> EvaluateAsync(
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
            return new(
                new([], subjects
                    .Where(item => item.Document.Kind == "prop")
                    .Select(item => item.Asset.Id)
                    .Distinct()
                    .ToArray()),
                [
                    new(
                        "shot.scene-linked",
                        "fail",
                        "开始制作前必须为镜头关联场景。",
                        "请在镜头详情中关联至少一个场景资产。",
                        linkedAssetIds),
                    new(
                        "shot.references-complete",
                        "fail",
                        "场景关联未通过，无法完成参考图校验。",
                        "请先修复场景关联后重新预检。",
                        [])
                ]);
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
            var message = string.Join("；", details);
            return new(
                new(
                    references.Select(item => item.ImageAssetId).Distinct().ToArray(),
                    subjects
                        .Where(item => item.Document.Kind == "prop")
                        .Select(item => item.Asset.Id)
                        .Distinct()
                        .ToArray()),
                [
                    new(
                        "shot.scene-linked",
                        "pass",
                        "镜头已关联场景资产。",
                        null,
                        scenes.Select(item => item.Asset.Id).ToArray()),
                    new(
                        "shot.references-complete",
                        "fail",
                        message,
                        "请为缺失的人物或场景生成参考图。",
                        requiredSubjects.Select(item => item.Asset.Id)
                            .Concat(references.Select(item => item.ImageAssetId))
                            .Distinct()
                            .ToArray())
                ]);
        }

        var referenceImageAssetIds = references.Select(item => item.ImageAssetId).Distinct().ToArray();
        return new(
            new(
                referenceImageAssetIds,
                subjects
                    .Where(item => item.Document.Kind == "prop")
                    .Select(item => item.Asset.Id)
                    .Distinct()
                    .ToArray()),
            [
                new(
                    "shot.scene-linked",
                    "pass",
                    "镜头已关联场景资产。",
                    null,
                    scenes.Select(item => item.Asset.Id).ToArray()),
                new(
                    "shot.references-complete",
                    "pass",
                    "人物和场景参考图齐全。",
                    null,
                    requiredSubjects.Select(item => item.Asset.Id)
                        .Concat(referenceImageAssetIds)
                        .Distinct()
                        .ToArray())
            ]);
    }
}

public static class ShotProductionModes
{
    public const double ThresholdSeconds = 15;
    public const string DirectFirstFrame = "direct-first-frame";
    public const string FirstLastContinuous = "first-last-continuous";

    public static string ForDuration(double durationSeconds) =>
        durationSeconds <= ThresholdSeconds ? DirectFirstFrame : FirstLastContinuous;

    public static bool IsSupported(string? mode) => mode is DirectFirstFrame or FirstLastContinuous;

    public static string Normalize(string? mode) => IsSupported(mode)
        ? mode!
        : throw new InvalidOperationException("镜头生产模式只能是 direct-first-frame 或 first-last-continuous。");

    internal static string ForShot(StoryboardShotDocument shot) => IsSupported(shot.ProductionMode)
        ? shot.ProductionMode
        : ForDuration(shot.DurationSeconds);

    public static IReadOnlyList<string> Stages(string mode) => mode == FirstLastContinuous
        ? ["first-frame", "last-frame"]
        : ["first-frame"];

    internal static ShotProductionView ToView(
        ProductionRun run,
        IReadOnlyList<ProductionRunItem> items,
        double durationSeconds,
        IReadOnlyDictionary<Guid, Asset>? outputAssets = null)
    {
        var outputAssetId = items.FirstOrDefault(item => item.Stage == "first-frame")?.OutputAssetId;
        var lastFrameAssetId = items.FirstOrDefault(item => item.Stage == "last-frame")?.OutputAssetId;
        Asset? Output(Guid? assetId) => assetId is Guid id
            && outputAssets is not null
            && outputAssets.TryGetValue(id, out var asset)
                ? asset
                : null;
        var firstFrame = Output(outputAssetId);
        var lastFrame = Output(lastFrameAssetId);
        return new(
            run.Id,
            items.Any(item => item.Stage == "last-frame") ? FirstLastContinuous : DirectFirstFrame,
            run.Status,
            run.CurrentStage,
            items.OrderBy(item => item.CreatedAtUtc).Select(item => item.Stage).Distinct().ToArray(),
            run.CreatedAtUtc,
            firstFrame?.Id ?? outputAssetId,
            outputAssetId is null
                ? null
                : $"/api/v2/projects/{run.ProjectId}/storyboard/frames/{firstFrame?.Id ?? outputAssetId}/content",
            ReadPrompt(firstFrame),
            lastFrame?.Id ?? lastFrameAssetId,
            lastFrameAssetId is null
                ? null
                : $"/api/v2/projects/{run.ProjectId}/storyboard/frames/{lastFrame?.Id ?? lastFrameAssetId}/content",
            ReadPrompt(lastFrame));
    }

    private static string? ReadPrompt(Asset? asset)
    {
        if (string.IsNullOrWhiteSpace(asset?.GenerationMetadataJson)) return null;
        try
        {
            using var metadata = JsonDocument.Parse(asset.GenerationMetadataJson);
            return metadata.RootElement.TryGetProperty("prompt", out var prompt)
                ? prompt.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}