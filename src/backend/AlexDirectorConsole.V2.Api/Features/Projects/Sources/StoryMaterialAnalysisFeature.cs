using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Agents;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Sources;

public sealed record StoryCharacterMaterial(
    string Name,
    string Role,
    string Goal,
    IReadOnlyList<string> Traits,
    IReadOnlyList<int> ChapterNumbers,
    IReadOnlyList<Guid>? ChapterIds = null);

public sealed record StoryLocationMaterial(
    string Name,
    string Function,
    string Atmosphere,
    IReadOnlyList<int> ChapterNumbers,
    IReadOnlyList<Guid>? ChapterIds = null);

public sealed record StoryPlotBeatMaterial(
    int Order,
    string Title,
    string Summary,
    IReadOnlyList<int> ChapterNumbers,
    IReadOnlyList<string> CharacterNames,
    string? LocationName,
    IReadOnlyList<Guid>? ChapterIds = null);

public sealed record StoryRelationMaterial(
    string Source,
    string Target,
    string Type,
    string Evidence,
    IReadOnlyList<int>? ChapterNumbers = null,
    IReadOnlyList<Guid>? ChapterIds = null);

internal sealed record StoryChapterMaterialAnalysis(
    Guid ChapterId,
    int ChapterNumber,
    string ChapterTitle,
    string Summary,
    IReadOnlyList<StoryCharacterMaterial> Characters,
    IReadOnlyList<StoryLocationMaterial> Locations,
    IReadOnlyList<StoryPlotBeatMaterial> PlotBeats,
    IReadOnlyList<StoryRelationMaterial> Relations,
    string Model,
    string Runtime,
    string? ContentFingerprint = null);

public sealed record StoryMaterialAnalysisResult(
    string Summary,
    IReadOnlyList<StoryCharacterMaterial> Characters,
    IReadOnlyList<StoryLocationMaterial> Locations,
    IReadOnlyList<StoryPlotBeatMaterial> PlotBeats,
    IReadOnlyList<StoryRelationMaterial> Relations,
    string Model,
    string Runtime);

public sealed record StoryMaterialAnalysisView(
    Guid AssetId,
    Guid ResourceId,
    int Version,
    Guid SourceResourceId,
    Guid SourceAssetId,
    int SourceVersion,
    bool IsStale,
    string? StaleReason,
    string Summary,
    IReadOnlyList<StoryCharacterMaterial> Characters,
    IReadOnlyList<StoryLocationMaterial> Locations,
    IReadOnlyList<StoryPlotBeatMaterial> PlotBeats,
    IReadOnlyList<StoryRelationMaterial> Relations,
    IReadOnlyList<Guid> AnalyzedChapterIds,
    string Model,
    string Runtime,
    DateTimeOffset UpdatedAtUtc);

internal sealed record StoryMaterialAnalysisDocument(
    Guid SourceResourceId,
    Guid SourceAssetId,
    int SourceVersion,
    string Summary,
    IReadOnlyList<StoryCharacterMaterial> Characters,
    IReadOnlyList<StoryLocationMaterial> Locations,
    IReadOnlyList<StoryPlotBeatMaterial> PlotBeats,
    IReadOnlyList<StoryRelationMaterial> Relations,
    string Model,
    string Runtime,
    IReadOnlyList<StoryChapterMaterialAnalysis>? ChapterAnalyses = null);

public interface IStoryMaterialAnalyzer
{
    Task<StoryMaterialAnalysisResult> AnalyzeAsync(
        string projectName,
        IReadOnlyList<SourceChapterView> chapters,
        CancellationToken cancellationToken);
}

public sealed record GetStoryMaterialAnalysisQuery(Guid ProjectId, Guid SourceResourceId)
    : IQuery<StoryMaterialAnalysisView?>;

public sealed record AnalyzeStoryMaterialCommand(Guid ProjectId, Guid SourceResourceId, Guid? ChapterId = null)
    : ICommand<StoryMaterialAnalysisView?>;

public sealed class GetStoryMaterialAnalysisQueryHandler(V2DbContext dbContext)
    : IQueryHandler<GetStoryMaterialAnalysisQuery, StoryMaterialAnalysisView?>
{
    public Task<StoryMaterialAnalysisView?> HandleAsync(
        GetStoryMaterialAnalysisQuery query,
        CancellationToken cancellationToken) =>
        StoryMaterialAnalysisQueries.GetCurrentAsync(
            dbContext,
            query.ProjectId,
            query.SourceResourceId,
            cancellationToken);
}

public sealed class AnalyzeStoryMaterialCommandHandler(
    V2DbContext dbContext,
    IStoryMaterialAnalyzer analyzer,
    TimeProvider timeProvider)
    : ICommandHandler<AnalyzeStoryMaterialCommand, StoryMaterialAnalysisView?>
{
    public async Task<StoryMaterialAnalysisView?> HandleAsync(
        AnalyzeStoryMaterialCommand command,
        CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == command.ProjectId,
            cancellationToken);
        if (project is null) return null;

        var sourceState = await dbContext.ResourceStates.AsNoTracking().SingleOrDefaultAsync(
            item => item.ProjectId == command.ProjectId
                && item.ResourceId == command.SourceResourceId
                && item.ResourceType == ProjectSourceDefaults.AssetType,
            cancellationToken);
        if (sourceState is null) return null;

        var sourceAsset = await dbContext.Assets.AsNoTracking().SingleAsync(
            item => item.Id == sourceState.CurrentAssetId,
            cancellationToken);
        var source = ProjectSourceMapper.ToView(sourceAsset);
        var previous = await StoryMaterialAnalysisQueries.FindCurrentAssetAsync(
            dbContext,
            command.ProjectId,
            command.SourceResourceId,
            cancellationToken);
        var previousChapterAnalyses = previous.Asset is null
            ? []
            : StoryMaterialAnalysisQueries.ReadDocument(previous.Asset).ChapterAnalyses?.ToList() ?? [];
        var targetChapters = command.ChapterId is Guid chapterId
            ? source.Chapters.Where(item => item.Id == chapterId).ToArray()
            : source.Chapters.Where(chapter => !previousChapterAnalyses.Any(previousChapter =>
                StoryMaterialAnalysisQueries.MatchesChapter(previousChapter, chapter))).ToArray();
        if (targetChapters.Length == 0)
        {
            return previous.Asset is null || previous.State is null
                ? null
                : StoryMaterialAnalysisQueries.ToView(
                    previous.Asset,
                    previous.State,
                    StoryMaterialAnalysisQueries.ReadDocument(previous.Asset),
                    source);
        }

        var currentChapterIds = source.Chapters.Select(item => item.Id).ToHashSet();
        var targetChapterIds = targetChapters.Select(item => item.Id).ToHashSet();
        var chapterAnalyses = previousChapterAnalyses
            .Where(item => currentChapterIds.Contains(item.ChapterId)
                && !targetChapterIds.Contains(item.ChapterId))
            .ToList();
        foreach (var chapter in targetChapters)
        {
            var result = await analyzer.AnalyzeAsync(project.Name, [chapter], cancellationToken);
            chapterAnalyses.Add(new StoryChapterMaterialAnalysis(
                chapter.Id,
                chapter.Number,
                chapter.Title,
                result.Summary,
                result.Characters.Take(16).Select(item => item with
                {
                    ChapterNumbers = [chapter.Number],
                    ChapterIds = [chapter.Id]
                }).ToArray(),
                result.Locations.Take(12).Select(item => item with
                {
                    ChapterNumbers = [chapter.Number],
                    ChapterIds = [chapter.Id]
                }).ToArray(),
                result.PlotBeats.Take(16).Select(item => item with
                {
                    ChapterNumbers = [chapter.Number],
                    ChapterIds = [chapter.Id]
                }).ToArray(),
                result.Relations.Take(30).Select(item => item with
                {
                    ChapterNumbers = [chapter.Number],
                    ChapterIds = [chapter.Id]
                }).ToArray(),
                result.Model,
                result.Runtime,
                StoryMaterialAnalysisQueries.GetChapterFingerprint(chapter)));
        }

        var document = StoryMaterialAnalysisQueries.AggregateDocument(source, chapterAnalyses);
        var documentJson = JsonSerializer.Serialize(document, ProjectSourceDefaults.JsonOptions);
        var now = timeProvider.GetUtcNow();
        var number = previous.Asset?.Number
            ?? (await dbContext.Assets
                .Where(item => item.ProjectId == command.ProjectId)
                .Select(item => (int?)item.Number)
                .MaxAsync(cancellationToken) ?? 0) + 1;
        var resourceId = previous.Asset?.ResourceId ?? Guid.NewGuid();
        var version = previous.Asset is null
            ? 1
            : await dbContext.Assets
                .Where(item => item.ProjectId == command.ProjectId && item.ResourceId == resourceId)
                .MaxAsync(item => item.Version, cancellationToken) + 1;
        var asset = new Asset
        {
            ProjectId = command.ProjectId,
            ProductionEpisodeId = null,
            ResourceId = resourceId,
            Version = version,
            Number = number,
            Type = StoryMaterialAnalysisQueries.AssetType,
            SchemaVersion = 2,
            Name = $"{source.Title} · 素材分析",
            DocumentJson = documentJson,
            ContentType = "application/json",
            SizeBytes = Encoding.UTF8.GetByteCount(documentJson),
            GenerationMetadataJson = JsonSerializer.Serialize(
                new { document.Model, document.Runtime, ChapterIds = targetChapterIds },
                ProjectSourceDefaults.JsonOptions),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.Assets.Add(asset);
        dbContext.AssetDependencies.Add(new AssetDependency
        {
            ProjectId = command.ProjectId,
            ConsumerAssetId = asset.Id,
            SourceAssetId = sourceAsset.Id,
            Role = "derived-from",
            IsRequired = true,
            CreatedAtUtc = now
        });

        ResourceState state;
        if (previous.State is null)
        {
            state = new ResourceState
            {
                ProjectId = command.ProjectId,
                ResourceId = asset.ResourceId,
                ResourceType = StoryMaterialAnalysisQueries.AssetType
            };
            dbContext.ResourceStates.Add(state);
        }
        else
        {
            state = previous.State;
        }
        state.CurrentAssetId = asset.Id;
        state.LifecycleStatus = "draft";
        state.IsStale = false;
        state.StaleReason = null;
        state.StaleSinceUtc = null;
        state.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return StoryMaterialAnalysisQueries.ToView(asset, state, document, source);
    }
}

internal static class StoryMaterialAnalysisQueries
{
    public const string AssetType = "story-material-analysis";

    public static async Task<StoryMaterialAnalysisView?> GetCurrentAsync(
        V2DbContext dbContext,
        Guid projectId,
        Guid sourceResourceId,
        CancellationToken cancellationToken)
    {
        var current = await FindCurrentAssetAsync(
            dbContext,
            projectId,
            sourceResourceId,
            cancellationToken);
        if (current.Asset is null || current.State is null) return null;
        var document = ReadDocument(current.Asset);
        var sourceState = await dbContext.ResourceStates.AsNoTracking().SingleOrDefaultAsync(
            item => item.ProjectId == projectId
                && item.ResourceId == sourceResourceId
                && item.ResourceType == ProjectSourceDefaults.AssetType,
            cancellationToken);
        if (sourceState is null) return null;
        var sourceAsset = await dbContext.Assets.AsNoTracking().SingleAsync(
            item => item.Id == sourceState.CurrentAssetId,
            cancellationToken);
        return ToView(
            current.Asset,
            current.State,
            document,
            ProjectSourceMapper.ToView(sourceAsset));
    }

    public static async Task<(Asset? Asset, ResourceState? State)> FindCurrentAssetAsync(
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

    public static StoryMaterialAnalysisDocument ReadDocument(Asset asset) =>
        JsonSerializer.Deserialize<StoryMaterialAnalysisDocument>(
            asset.DocumentJson ?? throw new InvalidOperationException("素材分析缺少文档内容。"),
            ProjectSourceDefaults.JsonOptions)
        ?? throw new InvalidOperationException("素材分析内容无效。");

    public static string GetChapterFingerprint(SourceChapterView chapter) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{chapter.Title}\n{chapter.Content}")));

    public static bool MatchesChapter(
        StoryChapterMaterialAnalysis analysis,
        SourceChapterView chapter) =>
        analysis.ChapterId == chapter.Id
        && analysis.ContentFingerprint is not null
        && analysis.ContentFingerprint == GetChapterFingerprint(chapter);

    public static StoryMaterialAnalysisDocument AggregateDocument(
        ProjectSourceView source,
        IReadOnlyList<StoryChapterMaterialAnalysis> chapterAnalyses)
    {
        var ordered = chapterAnalyses.OrderBy(item => item.ChapterNumber).ToArray();
        var characters = ordered
            .SelectMany(item => item.Characters)
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new StoryCharacterMaterial(
                    first.Name,
                    first.Role,
                    first.Goal,
                    group.SelectMany(item => item.Traits).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    group.SelectMany(item => item.ChapterNumbers).Distinct().Order().ToArray(),
                    group.SelectMany(item => item.ChapterIds ?? []).Distinct().ToArray());
            })
            .ToArray();
        var locations = ordered
            .SelectMany(item => item.Locations)
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new StoryLocationMaterial(
                    first.Name,
                    first.Function,
                    first.Atmosphere,
                    group.SelectMany(item => item.ChapterNumbers).Distinct().Order().ToArray(),
                    group.SelectMany(item => item.ChapterIds ?? []).Distinct().ToArray());
            })
            .ToArray();
        var plotBeats = ordered
            .SelectMany(chapter => chapter.PlotBeats
                .OrderBy(item => item.Order)
                .Select(item => (Chapter: chapter, Beat: item)))
            .Select((item, index) => item.Beat with
            {
                Order = index + 1,
                ChapterNumbers = [item.Chapter.ChapterNumber],
                ChapterIds = [item.Chapter.ChapterId]
            })
            .ToArray();
        var relations = ordered
            .SelectMany(item => item.Relations)
            .GroupBy(item => new { item.Source, item.Target, item.Type })
            .Select(group =>
            {
                var first = group.First();
                return new StoryRelationMaterial(
                    first.Source,
                    first.Target,
                    first.Type,
                    string.Join("；", group.Select(item => item.Evidence).Distinct()),
                    group.SelectMany(item => item.ChapterNumbers ?? []).Distinct().Order().ToArray(),
                    group.SelectMany(item => item.ChapterIds ?? []).Distinct().ToArray());
            })
            .ToArray();
        var latest = ordered.Last();
        return new StoryMaterialAnalysisDocument(
            source.Id,
            source.AssetId,
            source.Version,
            string.Join(Environment.NewLine, ordered.Select(item => $"第{item.ChapterNumber}章：{item.Summary}")),
            characters,
            locations,
            plotBeats,
            relations,
            latest.Model,
            latest.Runtime,
            ordered);
    }

    public static StoryMaterialAnalysisView ToView(
        Asset asset,
        ResourceState state,
        StoryMaterialAnalysisDocument document,
        ProjectSourceView? currentSource = null) => new(
            asset.Id,
            asset.ResourceId,
            asset.Version,
            document.SourceResourceId,
            document.SourceAssetId,
            document.SourceVersion,
            state.IsStale,
            state.StaleReason,
            document.Summary,
            document.Characters,
            document.Locations,
            document.PlotBeats,
            document.Relations,
            currentSource is null
                ? document.ChapterAnalyses?.Select(item => item.ChapterId).Distinct().ToArray() ?? []
                : currentSource.Chapters
                    .Where(chapter => document.ChapterAnalyses?.Any(analysis => MatchesChapter(analysis, chapter)) == true)
                    .Select(chapter => chapter.Id)
                    .ToArray(),
            document.Model,
            document.Runtime,
            asset.UpdatedAtUtc);
}

#pragma warning disable MAAI001
public sealed class MafStoryMaterialAnalyzer(
    V2DbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    ILoggerFactory loggerFactory) : IStoryMaterialAnalyzer
{
    public async Task<StoryMaterialAnalysisResult> AnalyzeAsync(
        string projectName,
        IReadOnlyList<SourceChapterView> chapters,
        CancellationToken cancellationToken)
    {
        var configuration = await dbContext.FoundryConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (!LlmChatClientFactory.IsConfigured(configuration))
        {
            throw new ProjectGenerationConfigurationException("请先在系统设置中配置语言模型。");
        }
        var instructions = await BuiltInAgentPromptLoader.LoadAsync(
            dbContext,
            BuiltInAgents.StoryMaterialAnalystId,
            cancellationToken);

        var agent = LlmChatClientFactory
            .Create(configuration!, dataProtectionProvider)
            .AsIChatClient()
            .AsHarnessAgent(
                new HarnessAgentOptions
                {
                    Name = "AlexStoryMaterialAnalyst",
                    MaxContextWindowTokens = 1_050_000,
                    MaxOutputTokens = 8_192,
                    MaximumIterationsPerRequest = 6,
                    DisableFileMemory = true,
                    DisableWebSearch = true,
                    DisableTodoProvider = true,
                    DisableAgentModeProvider = true,
                    DisableAgentSkillsProvider = true,
                    ChatOptions = new ChatOptions
                    {
                        Instructions = instructions,
                        MaxOutputTokens = 8_192
                    }
                },
                loggerFactory);

        var sourceJson = JsonSerializer.Serialize(
            new
            {
                projectName,
                chapters = chapters.Select(item => new
                {
                    item.Number,
                    item.Title,
                    item.Content
                })
            },
            ProjectSourceDefaults.JsonOptions);
        var response = await agent.RunAsync(
            $"分析以下原文章节：\n{sourceJson}",
            cancellationToken: cancellationToken);
        var json = ExtractJson(response.Text);
        var payload = JsonSerializer.Deserialize<StoryMaterialAnalysisPayload>(
            json,
            ProjectSourceDefaults.JsonOptions)
            ?? throw new InvalidOperationException("GPT-5.4 未返回有效的素材分析。");
        if (string.IsNullOrWhiteSpace(payload.Summary) || payload.PlotBeats.Count == 0)
        {
            throw new InvalidOperationException("GPT-5.4 返回的素材分析缺少摘要或情节节点。");
        }

        return new(
            payload.Summary.Trim(),
            payload.Characters,
            payload.Locations,
            payload.PlotBeats,
            payload.Relations,
            LlmChatClientFactory.GetModel(configuration!),
            "MAF HarnessAgent");
    }

    private static string ExtractJson(string? response)
    {
        var text = response?.Trim() ?? string.Empty;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidOperationException("GPT-5.4 未返回 JSON 素材分析。");
        return text[start..(end + 1)];
    }

    private sealed class StoryMaterialAnalysisPayload
    {
        public string Summary { get; set; } = string.Empty;
        public List<StoryCharacterMaterial> Characters { get; set; } = [];
        public List<StoryLocationMaterial> Locations { get; set; } = [];
        public List<StoryPlotBeatMaterial> PlotBeats { get; set; } = [];
        public List<StoryRelationMaterial> Relations { get; set; } = [];
    }
}
#pragma warning restore MAAI001

public static class StoryMaterialAnalysisEndpoints
{
    public static IEndpointRouteBuilder MapStoryMaterialAnalysis(this IEndpointRouteBuilder app)
    {
        var route = "/api/v2/projects/{projectId:guid}/sources/{sourceResourceId:guid}/analysis";
        app.MapGet(route, async (
            Guid projectId,
            Guid sourceResourceId,
            IQueryDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            var analysis = await dispatcher.QueryAsync(
                new GetStoryMaterialAnalysisQuery(projectId, sourceResourceId),
                cancellationToken);
            return analysis is null ? Results.NotFound() : Results.Ok(analysis);
        });
        app.MapPost(route, async (
            Guid projectId,
            Guid sourceResourceId,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var analysis = await dispatcher.SendAsync(
                    new AnalyzeStoryMaterialCommand(projectId, sourceResourceId),
                    cancellationToken);
                return analysis is null ? Results.NotFound() : Results.Ok(analysis);
            }
            catch (ProjectGenerationConfigurationException error)
            {
                return Results.Conflict(new { error = error.Message });
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return Results.Problem(
                    title: "原文素材分析失败",
                    detail: error.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });
        app.MapPost(
            "/api/v2/projects/{projectId:guid}/sources/{sourceResourceId:guid}/chapters/{chapterId:guid}/analysis",
            async (
                Guid projectId,
                Guid sourceResourceId,
                Guid chapterId,
                ICommandDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var analysis = await dispatcher.SendAsync(
                        new AnalyzeStoryMaterialCommand(projectId, sourceResourceId, chapterId),
                        cancellationToken);
                    return analysis is null ? Results.NotFound() : Results.Ok(analysis);
                }
                catch (ProjectGenerationConfigurationException error)
                {
                    return Results.Conflict(new { error = error.Message });
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    return Results.Problem(
                        title: "原文章节素材分析失败",
                        detail: error.Message,
                        statusCode: StatusCodes.Status502BadGateway);
                }
            });
        return app;
    }
}
