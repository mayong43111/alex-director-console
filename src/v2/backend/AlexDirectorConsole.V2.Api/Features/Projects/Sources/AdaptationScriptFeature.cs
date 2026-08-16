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

public sealed record AdaptationSceneDraft(
    int SceneNumber,
    string Heading,
    string Summary,
    IReadOnlyList<string> Characters,
    IReadOnlyList<string> Props,
    string StoryFunction,
    string DialogueNotes);

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
    AdaptationEpisodeDraft Episode);

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
    AdaptationEpisodeDraft Episode,
    DateTimeOffset UpdatedAtUtc);

public interface IAdaptationScriptWriter
{
    Task<AdaptationScriptResult> WriteAsync(
        ProjectSettingsView projectSettings,
        StoryMaterialAnalysisView analysis,
        IReadOnlyList<SourceChapterView> chapters,
        int desiredEpisodeCount,
        string? instruction,
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

public sealed record ConfirmAdaptationScriptCommand(Guid ProjectId, Guid SourceResourceId)
    : ICommand<AdaptationScriptView?>;

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

        var asset = await dbContext.Assets.AsNoTracking()
            .Where(item => item.ProjectId == query.ProjectId
                && item.ProductionEpisodeId == query.ProductionEpisodeId
                && item.Type == "script-package")
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken);
        if (asset?.DocumentJson is null) return null;

        var document = JsonSerializer.Deserialize<ProductionScriptPackageDocument>(
            asset.DocumentJson,
            ProjectSourceDefaults.JsonOptions);
        return document is null
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
                document.Episode,
                asset.UpdatedAtUtc);
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
            throw new StoryDevelopmentConflictException("原文已有新版本，请先重新分析素材，再生成新的剧本草案。");

        var sourceAsset = await dbContext.Assets.AsNoTracking().SingleAsync(
            item => item.Id == analysis.SourceAssetId,
            cancellationToken);
        var source = ProjectSourceMapper.ToView(sourceAsset);
        var result = await writer.WriteAsync(
            projectSettings,
            analysis,
            source.Chapters,
            desiredEpisodeCount,
            command.Instruction,
            cancellationToken);
        if (result.Episodes.Count != desiredEpisodeCount)
            throw new InvalidOperationException($"GPT-5.4 应返回 {desiredEpisodeCount} 集，实际返回 {result.Episodes.Count} 集。");

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
        var document = new AdaptationScriptDocument(
            sourceResourceId,
            analysis.SourceAssetId,
            analysis.SourceVersion,
            analysis.AssetId,
            "draft",
            result.Title,
            result.Approach,
            result.Episodes.Take(6).ToArray(),
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
        var asset = new Asset
        {
            ProjectId = projectId,
            ProductionEpisodeId = null,
            ResourceId = previous.Asset?.ResourceId ?? Guid.NewGuid(),
            Version = (previous.Asset?.Version ?? 0) + 1,
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

        var sourceAsset = await dbContext.Assets.AsNoTracking().SingleAsync(
            item => item.Id == analysis.SourceAssetId,
            cancellationToken);
        var source = ProjectSourceMapper.ToView(sourceAsset);
        var nextNumber = currentDocument.Episodes.Count + 1;
        var existingOutline = string.Join("；", currentDocument.Episodes.Select(item =>
            $"E{item.ProposalNumber:D2}《{item.Title}》：{item.Logline}"));
        var appendInstruction = $"只生成现有草案之后的第 {nextNumber} 集，不要重写已有剧集。已有分集：{existingOutline}。{command.Instruction}";
        var generated = await writer.WriteAsync(
            projectSettings,
            analysis,
            source.Chapters,
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
            [.. currentDocument.OverallSmallHooks ?? [], .. generated.OverallSmallHooks ?? []],
            [.. currentDocument.OverallBigHooks ?? [], .. generated.OverallBigHooks ?? []]);
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

public sealed class ConfirmAdaptationScriptCommandHandler(
    V2DbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<ConfirmAdaptationScriptCommand, AdaptationScriptView?>
{
    public async Task<AdaptationScriptView?> HandleAsync(
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
        current.State.LifecycleStatus = "approved";
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
                    currentDocument.Episodes[index]),
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
                LifecycleStatus = "approved",
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
        IReadOnlyList<SourceChapterView> chapters,
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
                            你是动画短剧改编编剧。根据原文章节和已提取的轻量素材图谱，创作可继续细化的分集剧本草案。
                            原文是参考源，不要求一章对应一集；允许跨章节重排、合并、删减和原创连接，但不得改变项目核心人物身份。
                            当前阶段只写剧本草案中的场景结构，不输出人物美术设定、场景美术设定或正式资产清单。
                            必须遵守项目设定的内容类型、受众、单集时长和创作方向。
                            同时分析节奏爆点：smallHooks 是推动继续观看的局部悬念、反转或情绪点；bigHooks 是改变局势、揭示核心秘密或形成集尾追看动力的大爆点。整体爆点描述跨集递进，每集爆点必须落到该集具体事件。
                            全部正文使用简体中文，专有名称使用通行中文译名。
                            只返回 JSON，不要 Markdown 围栏。结构必须为：
                            {"title":"...","approach":"...","overallSmallHooks":["..."],"overallBigHooks":["..."],"episodes":[{"proposalNumber":1,"title":"...","logline":"...","targetSeconds":100,"sourceChapterNumbers":[1,2],"smallHooks":["..."],"bigHooks":["..."],"scenes":[{"sceneNumber":1,"heading":"内/外景 地点 时间","summary":"...","characters":["..."],"props":["..."],"storyFunction":"...","dialogueNotes":"..."}]}]}
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
                },
                chapters = chapters.Select(item => new
                {
                    item.Number,
                    item.Title,
                    item.Content
                })
            },
            ProjectSourceDefaults.JsonOptions);
        var response = await agent.RunAsync(
            $"生成恰好 {desiredEpisodeCount} 个剧集草案：\n{input}",
            cancellationToken: cancellationToken);
        var json = ExtractJson(response.Text);
        var payload = JsonSerializer.Deserialize<AdaptationScriptPayload>(
            json,
            ProjectSourceDefaults.JsonOptions)
            ?? throw new InvalidOperationException("GPT-5.4 未返回有效的剧本草案。");
        if (payload.Episodes.Count == 0)
            throw new InvalidOperationException("GPT-5.4 返回的剧本草案没有剧集。");
        return new(
            payload.Title,
            payload.Approach,
            payload.Episodes,
            configuration.Deployment,
            "MAF HarnessAgent",
            payload.OverallSmallHooks,
            payload.OverallBigHooks);
    }

    private static string ExtractJson(string? response)
    {
        var text = response?.Trim() ?? string.Empty;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidOperationException("GPT-5.4 未返回 JSON 剧本草案。");
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
                    title: "剧本草案生成失败",
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
            var script = await dispatcher.SendAsync(
                new ConfirmAdaptationScriptCommand(projectId, sourceResourceId),
                cancellationToken);
            return script is null ? Results.NotFound() : Results.Ok(script);
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
        return app;
    }
}

public sealed record GenerateAdaptationScriptRequest(
    int? DesiredEpisodeCount,
    string? Instruction);

public sealed record AppendAdaptationEpisodeRequest(string? Instruction);
