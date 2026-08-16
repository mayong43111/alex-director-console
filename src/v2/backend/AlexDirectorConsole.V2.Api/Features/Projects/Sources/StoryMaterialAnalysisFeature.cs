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

public sealed record StoryCharacterMaterial(
    string Name,
    string Role,
    string Goal,
    IReadOnlyList<string> Traits,
    IReadOnlyList<int> ChapterNumbers);

public sealed record StoryLocationMaterial(
    string Name,
    string Function,
    string Atmosphere,
    IReadOnlyList<int> ChapterNumbers);

public sealed record StoryPlotBeatMaterial(
    int Order,
    string Title,
    string Summary,
    IReadOnlyList<int> ChapterNumbers,
    IReadOnlyList<string> CharacterNames,
    string? LocationName);

public sealed record StoryRelationMaterial(
    string Source,
    string Target,
    string Type,
    string Evidence);

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
    string Runtime);

public interface IStoryMaterialAnalyzer
{
    Task<StoryMaterialAnalysisResult> AnalyzeAsync(
        string projectName,
        IReadOnlyList<SourceChapterView> chapters,
        CancellationToken cancellationToken);
}

public sealed record GetStoryMaterialAnalysisQuery(Guid ProjectId, Guid SourceResourceId)
    : IQuery<StoryMaterialAnalysisView?>;

public sealed record AnalyzeStoryMaterialCommand(Guid ProjectId, Guid SourceResourceId)
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
        var result = await analyzer.AnalyzeAsync(project.Name, source.Chapters, cancellationToken);
        var document = new StoryMaterialAnalysisDocument(
            source.Id,
            source.AssetId,
            source.Version,
            result.Summary,
            result.Characters.Take(16).ToArray(),
            result.Locations.Take(12).ToArray(),
            result.PlotBeats.Take(16).ToArray(),
            result.Relations.Take(30).ToArray(),
            result.Model,
            result.Runtime);
        var documentJson = JsonSerializer.Serialize(document, ProjectSourceDefaults.JsonOptions);
        var previous = await StoryMaterialAnalysisQueries.FindCurrentAssetAsync(
            dbContext,
            command.ProjectId,
            command.SourceResourceId,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        var number = previous.Asset?.Number
            ?? (await dbContext.Assets
                .Where(item => item.ProjectId == command.ProjectId)
                .Select(item => (int?)item.Number)
                .MaxAsync(cancellationToken) ?? 0) + 1;
        var asset = new Asset
        {
            ProjectId = command.ProjectId,
            ProductionEpisodeId = null,
            ResourceId = previous.Asset?.ResourceId ?? Guid.NewGuid(),
            Version = (previous.Asset?.Version ?? 0) + 1,
            Number = number,
            Type = StoryMaterialAnalysisQueries.AssetType,
            Name = $"{source.Title} · 素材分析",
            DocumentJson = documentJson,
            ContentType = "application/json",
            SizeBytes = Encoding.UTF8.GetByteCount(documentJson),
            GenerationMetadataJson = JsonSerializer.Serialize(
                new { result.Model, result.Runtime },
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

        return StoryMaterialAnalysisQueries.ToView(asset, state, document);
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
        return ToView(current.Asset, current.State, document);
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

    public static StoryMaterialAnalysisView ToView(
        Asset asset,
        ResourceState state,
        StoryMaterialAnalysisDocument document) => new(
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
        if (configuration is null
            || string.IsNullOrWhiteSpace(configuration.Endpoint)
            || string.IsNullOrWhiteSpace(configuration.ProtectedApiKey))
        {
            throw new ProjectGenerationConfigurationException("请先在系统设置中配置 GPT-5.4。");
        }

        var apiKey = dataProtectionProvider
            .CreateProtector("FoundryApiKeys.v1")
            .Unprotect(configuration.ProtectedApiKey);
        var agent = AzureFoundryChatClientFactory
            .Create(configuration.Endpoint, configuration.Deployment, apiKey)
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
                        Instructions = """
                            你是影视改编前期的故事编辑。只为剧本写作准备素材，不做学术型全文解析。
                            从给定章节提取：主要人物、关键场景、按叙事顺序排列的情节节点，以及必要的人物/事件关系。
                            原文人物和地点只是候选素材，不得写成已确定的美术设定。不要虚构原文中没有的事实。
                            控制规模：人物不超过 16，场景不超过 12，情节不超过 16，关系不超过 30。
                            全部说明使用简体中文；专有名称可保留常用译名并在必要时附原文。
                            只返回一个 JSON 对象，不要 Markdown 围栏或解释。结构必须为：
                            {"summary":"...","characters":[{"name":"...","role":"...","goal":"...","traits":["..."],"chapterNumbers":[1]}],"locations":[{"name":"...","function":"...","atmosphere":"...","chapterNumbers":[1]}],"plotBeats":[{"order":1,"title":"...","summary":"...","chapterNumbers":[1],"characterNames":["..."],"locationName":"...或null"}],"relations":[{"source":"...","target":"...","type":"...","evidence":"..."}]}
                            """,
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
            configuration.Deployment,
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
        return app;
    }
}
