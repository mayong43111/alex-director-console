using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Sources;

public sealed record SourceChapterView(
    Guid Id,
    int Number,
    string Title,
    string Content,
    int CharacterCount);

public sealed record ProjectSourceView(
    Guid Id,
    Guid AssetId,
    int Version,
    int Number,
    string Title,
    string? Description,
    string? FileName,
    int CharacterCount,
    int ChapterCount,
    IReadOnlyList<SourceChapterView> Chapters,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ListProjectSourcesQuery(Guid ProjectId)
    : IQuery<IReadOnlyList<ProjectSourceView>>;

public sealed record GetProjectSourceQuery(Guid ProjectId, Guid ResourceId)
    : IQuery<ProjectSourceView?>;

public sealed record CreateProjectSourceCommand(
    Guid ProjectId,
    string Title,
    string? Description,
    string Content,
    string? FileName) : ICommand<CreateProjectSourceResult>;

public enum CreateProjectSourceStatus
{
    Success,
    Invalid,
    ProjectNotFound
}

public sealed record CreateProjectSourceResult(
    CreateProjectSourceStatus Status,
    ProjectSourceView? Source,
    Dictionary<string, string[]> Errors);

public sealed record AppendProjectSourceChaptersCommand(
    Guid ProjectId,
    Guid ResourceId,
    string Content,
    string? FileName) : ICommand<CreateProjectSourceResult>;

public sealed record UpdateProjectSourceChapterCommand(
    Guid ProjectId,
    Guid ResourceId,
    Guid ChapterId,
    string Title,
    string Content) : ICommand<CreateProjectSourceResult>;

public sealed record DeleteProjectSourceChapterCommand(
    Guid ProjectId,
    Guid ResourceId,
    Guid ChapterId) : ICommand<CreateProjectSourceResult>;

internal static class ProjectSourceDefaults
{
    public const string AssetType = "source-document";
    public const int MaxContentCharacters = 5_000_000;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

internal sealed record SourceDocument(
    string Title,
    string? Description,
    int CharacterCount,
    IReadOnlyList<SourceChapterDocument> Chapters);

internal sealed record SourceChapterDocument(
    Guid Id,
    int Number,
    string Title,
    string Content,
    int CharacterCount);

public sealed class ListProjectSourcesQueryHandler(V2DbContext dbContext)
    : IQueryHandler<ListProjectSourcesQuery, IReadOnlyList<ProjectSourceView>>
{
    public async Task<IReadOnlyList<ProjectSourceView>> HandleAsync(
        ListProjectSourcesQuery query,
        CancellationToken cancellationToken)
    {
        var assets = await (
            from state in dbContext.ResourceStates.AsNoTracking()
            join asset in dbContext.Assets.AsNoTracking() on state.CurrentAssetId equals asset.Id
            where state.ProjectId == query.ProjectId
                && state.ResourceType == ProjectSourceDefaults.AssetType
                && asset.Type == ProjectSourceDefaults.AssetType
            orderby asset.Number
            select asset)
            .ToListAsync(cancellationToken);

        return assets.Select(ProjectSourceMapper.ToView).ToArray();
    }
}

public sealed class GetProjectSourceQueryHandler(V2DbContext dbContext)
    : IQueryHandler<GetProjectSourceQuery, ProjectSourceView?>
{
    public async Task<ProjectSourceView?> HandleAsync(
        GetProjectSourceQuery query,
        CancellationToken cancellationToken)
    {
        var asset = await (
            from state in dbContext.ResourceStates.AsNoTracking()
            join currentAsset in dbContext.Assets.AsNoTracking() on state.CurrentAssetId equals currentAsset.Id
            where state.ProjectId == query.ProjectId
                && state.ResourceId == query.ResourceId
                && state.ResourceType == ProjectSourceDefaults.AssetType
                && currentAsset.Type == ProjectSourceDefaults.AssetType
            select currentAsset)
            .SingleOrDefaultAsync(cancellationToken);

        return asset is null ? null : ProjectSourceMapper.ToView(asset);
    }
}

public sealed class CreateProjectSourceCommandHandler(
    V2DbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<CreateProjectSourceCommand, CreateProjectSourceResult>
{
    public async Task<CreateProjectSourceResult> HandleAsync(
        CreateProjectSourceCommand command,
        CancellationToken cancellationToken)
    {
        var title = command.Title.Trim();
        var description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim();
        var content = command.Content.Trim();
        var errors = Validate(title, description, content);
        if (errors.Count > 0)
        {
            return new(CreateProjectSourceStatus.Invalid, null, errors);
        }

        if (!await dbContext.Projects.AnyAsync(item => item.Id == command.ProjectId, cancellationToken))
        {
            return new(CreateProjectSourceStatus.ProjectNotFound, null, errors);
        }

        var chapters = ProjectSourceParser.ParseChapters(content);
        var document = new SourceDocument(title, description, content.Length, chapters);
        var documentJson = JsonSerializer.Serialize(document, ProjectSourceDefaults.JsonOptions);
        var now = timeProvider.GetUtcNow();
        var resourceId = Guid.NewGuid();
        var number = (await dbContext.Assets
            .Where(item => item.ProjectId == command.ProjectId)
            .Select(item => (int?)item.Number)
            .MaxAsync(cancellationToken) ?? 0) + 1;
        var asset = new Asset
        {
            ProjectId = command.ProjectId,
            ProductionEpisodeId = null,
            ResourceId = resourceId,
            Version = 1,
            Number = number,
            Type = ProjectSourceDefaults.AssetType,
            Name = title,
            DocumentJson = documentJson,
            FileName = string.IsNullOrWhiteSpace(command.FileName) ? null : Path.GetFileName(command.FileName),
            ContentType = "application/json",
            SizeBytes = Encoding.UTF8.GetByteCount(documentJson),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var state = new ResourceState
        {
            ProjectId = command.ProjectId,
            ResourceId = resourceId,
            ResourceType = ProjectSourceDefaults.AssetType,
            CurrentAssetId = asset.Id,
            LifecycleStatus = "draft",
            UpdatedAtUtc = now
        };

        dbContext.Assets.Add(asset);
        dbContext.ResourceStates.Add(state);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new(CreateProjectSourceStatus.Success, ProjectSourceMapper.ToView(asset), errors);
    }

    private static Dictionary<string, string[]> Validate(
        string title,
        string? description,
        string content)
    {
        var errors = new Dictionary<string, string[]>();
        if (title.Length is < 1 or > 200)
            errors["title"] = ["原文资料名称必须为 1 至 200 个字符。"]; 
        if (description?.Length > 2000)
            errors["description"] = ["说明不能超过 2000 个字符。"]; 
        if (content.Length < 1)
            errors["content"] = ["请粘贴或上传原文内容。"]; 
        else if (content.Length > ProjectSourceDefaults.MaxContentCharacters)
            errors["content"] = ["原文内容不能超过 500 万个字符。"]; 
        return errors;
    }
}

public sealed class AppendProjectSourceChaptersCommandHandler(
    V2DbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<AppendProjectSourceChaptersCommand, CreateProjectSourceResult>
{
    public async Task<CreateProjectSourceResult> HandleAsync(
        AppendProjectSourceChaptersCommand command,
        CancellationToken cancellationToken)
    {
        var content = command.Content.Trim();
        if (content.Length == 0)
        {
            return new(
                CreateProjectSourceStatus.Invalid,
                null,
                new Dictionary<string, string[]> { ["content"] = ["请粘贴或上传要追加的章节。"] });
        }

        var state = await dbContext.ResourceStates.SingleOrDefaultAsync(
            item => item.ProjectId == command.ProjectId
                && item.ResourceId == command.ResourceId
                && item.ResourceType == ProjectSourceDefaults.AssetType,
            cancellationToken);
        if (state is null)
        {
            return new(CreateProjectSourceStatus.ProjectNotFound, null, []);
        }

        var previousAsset = await dbContext.Assets.SingleAsync(
            item => item.Id == state.CurrentAssetId
                && item.ProjectId == command.ProjectId
                && item.Type == ProjectSourceDefaults.AssetType,
            cancellationToken);
        var previousDocument = ProjectSourceMapper.ReadDocument(previousAsset);
        if (previousDocument.CharacterCount + content.Length > ProjectSourceDefaults.MaxContentCharacters)
        {
            return new(
                CreateProjectSourceStatus.Invalid,
                null,
                new Dictionary<string, string[]> { ["content"] = ["追加后原文内容不能超过 500 万个字符。"] });
        }

        var appendedChapters = ProjectSourceParser.ParseChapters(content)
            .Select((chapter, index) => chapter with
            {
                Number = previousDocument.Chapters.Count + index + 1
            })
            .ToArray();
        var document = previousDocument with
        {
            CharacterCount = previousDocument.CharacterCount + content.Length,
            Chapters = previousDocument.Chapters.Concat(appendedChapters).ToArray()
        };
        var documentJson = JsonSerializer.Serialize(document, ProjectSourceDefaults.JsonOptions);
        var now = timeProvider.GetUtcNow();
        var asset = new Asset
        {
            ProjectId = command.ProjectId,
            ProductionEpisodeId = null,
            ResourceId = command.ResourceId,
            Version = previousAsset.Version + 1,
            Number = previousAsset.Number,
            Type = ProjectSourceDefaults.AssetType,
            Name = previousAsset.Name,
            DocumentJson = documentJson,
            FileName = string.IsNullOrWhiteSpace(command.FileName)
                ? previousAsset.FileName
                : Path.GetFileName(command.FileName),
            ContentType = "application/json",
            SizeBytes = Encoding.UTF8.GetByteCount(documentJson),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.Assets.Add(asset);
        state.CurrentAssetId = asset.Id;
        state.LifecycleStatus = "draft";
        state.UpdatedAtUtc = now;

        var analysisResourceIds = await (
            from dependency in dbContext.AssetDependencies
            join consumer in dbContext.Assets on dependency.ConsumerAssetId equals consumer.Id
            where dependency.ProjectId == command.ProjectId
                && dependency.SourceAssetId == previousAsset.Id
                && dependency.Role == "derived-from"
                && consumer.Type == "story-material-analysis"
            select consumer.ResourceId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var analysisStates = await dbContext.ResourceStates
            .Where(item => item.ProjectId == command.ProjectId
                && analysisResourceIds.Contains(item.ResourceId))
            .ToListAsync(cancellationToken);
        foreach (var analysisState in analysisStates)
        {
            analysisState.IsStale = true;
            analysisState.StaleReason = $"原文资料已从 v{previousAsset.Version} 更新到 v{asset.Version}，可按需重新分析。";
            analysisState.StaleSinceUtc = now;
            analysisState.UpdatedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new(CreateProjectSourceStatus.Success, ProjectSourceMapper.ToView(asset), []);
    }
}

public sealed class UpdateProjectSourceChapterCommandHandler(
    V2DbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<UpdateProjectSourceChapterCommand, CreateProjectSourceResult>
{
    public async Task<CreateProjectSourceResult> HandleAsync(
        UpdateProjectSourceChapterCommand command,
        CancellationToken cancellationToken)
    {
        var title = command.Title.Trim();
        var content = command.Content.Trim();
        var errors = new Dictionary<string, string[]>();
        if (title.Length is < 1 or > 300)
            errors["title"] = ["章节标题必须为 1 至 300 个字符。"];
        if (content.Length < 1)
            errors["content"] = ["章节正文不能为空。"];
        if (errors.Count > 0)
            return new(CreateProjectSourceStatus.Invalid, null, errors);

        return await ProjectSourceVersioning.UpdateAsync(
            dbContext,
            timeProvider,
            command.ProjectId,
            command.ResourceId,
            document =>
            {
                if (!document.Chapters.Any(chapter => chapter.Id == command.ChapterId)) return null;
                var chapters = document.Chapters.Select(chapter => chapter.Id == command.ChapterId
                    ? chapter with { Title = title, Content = content, CharacterCount = content.Length }
                    : chapter).ToArray();
                return document with
                {
                    CharacterCount = chapters.Sum(chapter => chapter.CharacterCount),
                    Chapters = chapters
                };
            },
            cancellationToken);
    }
}

public sealed class DeleteProjectSourceChapterCommandHandler(
    V2DbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<DeleteProjectSourceChapterCommand, CreateProjectSourceResult>
{
    public async Task<CreateProjectSourceResult> HandleAsync(
        DeleteProjectSourceChapterCommand command,
        CancellationToken cancellationToken) => await ProjectSourceVersioning.UpdateAsync(
            dbContext,
            timeProvider,
            command.ProjectId,
            command.ResourceId,
            document =>
            {
                if (!document.Chapters.Any(chapter => chapter.Id == command.ChapterId)) return null;
                if (document.Chapters.Count == 1) throw new InvalidOperationException("原文资料至少需要保留一个章节。");
                var chapters = document.Chapters
                    .Where(chapter => chapter.Id != command.ChapterId)
                    .Select((chapter, index) => chapter with { Number = index + 1 })
                    .ToArray();
                return document with
                {
                    CharacterCount = chapters.Sum(chapter => chapter.CharacterCount),
                    Chapters = chapters
                };
            },
            cancellationToken);
}

internal static class ProjectSourceVersioning
{
    public static async Task<CreateProjectSourceResult> UpdateAsync(
        V2DbContext dbContext,
        TimeProvider timeProvider,
        Guid projectId,
        Guid resourceId,
        Func<SourceDocument, SourceDocument?> update,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.ResourceStates.SingleOrDefaultAsync(
            item => item.ProjectId == projectId
                && item.ResourceId == resourceId
                && item.ResourceType == ProjectSourceDefaults.AssetType,
            cancellationToken);
        if (state is null) return new(CreateProjectSourceStatus.ProjectNotFound, null, []);

        var previousAsset = await dbContext.Assets.SingleAsync(
            item => item.Id == state.CurrentAssetId
                && item.ProjectId == projectId
                && item.Type == ProjectSourceDefaults.AssetType,
            cancellationToken);
        var document = update(ProjectSourceMapper.ReadDocument(previousAsset));
        if (document is null) return new(CreateProjectSourceStatus.ProjectNotFound, null, []);
        if (document.CharacterCount > ProjectSourceDefaults.MaxContentCharacters)
        {
            return new(
                CreateProjectSourceStatus.Invalid,
                null,
                new Dictionary<string, string[]> { ["content"] = ["原文内容不能超过 500 万个字符。"] });
        }

        var documentJson = JsonSerializer.Serialize(document, ProjectSourceDefaults.JsonOptions);
        var now = timeProvider.GetUtcNow();
        var asset = new Asset
        {
            ProjectId = projectId,
            ProductionEpisodeId = null,
            ResourceId = resourceId,
            Version = previousAsset.Version + 1,
            Number = previousAsset.Number,
            Type = ProjectSourceDefaults.AssetType,
            Name = previousAsset.Name,
            DocumentJson = documentJson,
            FileName = previousAsset.FileName,
            ContentType = "application/json",
            SizeBytes = Encoding.UTF8.GetByteCount(documentJson),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.Assets.Add(asset);
        state.CurrentAssetId = asset.Id;
        state.LifecycleStatus = "draft";
        state.UpdatedAtUtc = now;

        var analysisResourceIds = await (
            from dependency in dbContext.AssetDependencies
            join consumer in dbContext.Assets on dependency.ConsumerAssetId equals consumer.Id
            where dependency.ProjectId == projectId
                && dependency.SourceAssetId == previousAsset.Id
                && dependency.Role == "derived-from"
                && consumer.Type == "story-material-analysis"
            select consumer.ResourceId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var analysisStates = await dbContext.ResourceStates
            .Where(item => item.ProjectId == projectId && analysisResourceIds.Contains(item.ResourceId))
            .ToListAsync(cancellationToken);
        foreach (var analysisState in analysisStates)
        {
            analysisState.IsStale = true;
            analysisState.StaleReason = $"原文资料已从 v{previousAsset.Version} 更新到 v{asset.Version}，可按需重新分析。";
            analysisState.StaleSinceUtc = now;
            analysisState.UpdatedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new(CreateProjectSourceStatus.Success, ProjectSourceMapper.ToView(asset), []);
    }
}

internal static class ProjectSourceParser
{
    private static readonly Regex MarkdownHeading = new("^#{1,6}\\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex ChineseChapterHeading = new(
        "^第[0-9０-９一二三四五六七八九十百千万两〇零]+[章节回卷部篇](?:$|\\s+.+$|[:：].+$)",
        RegexOptions.Compiled);

    public static IReadOnlyList<SourceChapterDocument> ParseChapters(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var chapters = new List<SourceChapterDocument>();
        var body = new List<string>();
        var currentTitle = "正文";
        var foundHeading = false;

        foreach (var line in lines)
        {
            var markdownMatch = MarkdownHeading.Match(line.Trim());
            var isChineseHeading = ChineseChapterHeading.IsMatch(line.Trim());
            if (markdownMatch.Success || isChineseHeading)
            {
                AddChapter(chapters, currentTitle, body);
                currentTitle = markdownMatch.Success ? markdownMatch.Groups[1].Value.Trim() : line.Trim();
                foundHeading = true;
                continue;
            }

            body.Add(line);
        }

        AddChapter(chapters, currentTitle, body);
        if (chapters.Count == 0)
        {
            AddChapter(chapters, "正文", [content]);
        }
        else if (foundHeading && chapters.Count > 1 && chapters[0].Title == "正文")
        {
            chapters[0] = chapters[0] with { Title = "前言" };
        }

        return chapters;
    }

    private static void AddChapter(
        List<SourceChapterDocument> chapters,
        string title,
        List<string> body)
    {
        var chapterContent = string.Join('\n', body).Trim();
        body.Clear();
        if (chapterContent.Length == 0) return;

        chapters.Add(new SourceChapterDocument(
            Guid.NewGuid(),
            chapters.Count + 1,
            title,
            chapterContent,
            chapterContent.Length));
    }
}

internal static class ProjectSourceMapper
{
    public static SourceDocument ReadDocument(Asset asset) =>
        JsonSerializer.Deserialize<SourceDocument>(
            asset.DocumentJson ?? throw new InvalidOperationException("原文资料缺少文档内容。"),
            ProjectSourceDefaults.JsonOptions)
        ?? throw new InvalidOperationException("原文资料内容无效。");

    public static ProjectSourceView ToView(Asset asset)
    {
        var document = ReadDocument(asset);

        return new ProjectSourceView(
            asset.ResourceId,
            asset.Id,
            asset.Version,
            asset.Number,
            document.Title,
            document.Description,
            asset.FileName,
            document.CharacterCount,
            document.Chapters.Count,
            document.Chapters.Select(item => new SourceChapterView(
                item.Id,
                item.Number,
                item.Title,
                item.Content,
                item.CharacterCount)).ToArray(),
            asset.CreatedAtUtc,
            asset.UpdatedAtUtc);
    }
}

public static class ProjectSourceEndpoints
{
    public static IEndpointRouteBuilder MapProjectSources(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/projects/{projectId:guid}/sources");

        group.MapGet("/", async (
            Guid projectId,
            IQueryDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            var sources = await dispatcher.QueryAsync(
                new ListProjectSourcesQuery(projectId),
                cancellationToken);
            return Results.Ok(sources);
        });

        group.MapGet("/{resourceId:guid}", async (
            Guid projectId,
            Guid resourceId,
            IQueryDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            var source = await dispatcher.QueryAsync(
                new GetProjectSourceQuery(projectId, resourceId),
                cancellationToken);
            return source is null ? Results.NotFound() : Results.Ok(source);
        });

        group.MapPost("/", async (
            Guid projectId,
            CreateProjectSourceRequest request,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            var result = await dispatcher.SendAsync(
                new CreateProjectSourceCommand(
                    projectId,
                    request.Title,
                    request.Description,
                    request.Content,
                    request.FileName),
                cancellationToken);
            return result.Status switch
            {
                CreateProjectSourceStatus.Success => Results.Created(
                    $"/api/v2/projects/{projectId}/sources/{result.Source!.Id}",
                    result.Source),
                CreateProjectSourceStatus.Invalid => Results.ValidationProblem(result.Errors),
                _ => Results.NotFound()
            };
        });

        group.MapPost("/{resourceId:guid}/chapters", async (
            Guid projectId,
            Guid resourceId,
            AppendProjectSourceChaptersRequest request,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            var result = await dispatcher.SendAsync(
                new AppendProjectSourceChaptersCommand(
                    projectId,
                    resourceId,
                    request.Content,
                    request.FileName),
                cancellationToken);
            return result.Status switch
            {
                CreateProjectSourceStatus.Success => Results.Ok(result.Source),
                CreateProjectSourceStatus.Invalid => Results.ValidationProblem(result.Errors),
                _ => Results.NotFound()
            };
        });

        group.MapPut("/{resourceId:guid}/chapters/{chapterId:guid}", async (
            Guid projectId,
            Guid resourceId,
            Guid chapterId,
            UpdateProjectSourceChapterRequest request,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            var result = await dispatcher.SendAsync(
                new UpdateProjectSourceChapterCommand(projectId, resourceId, chapterId, request.Title, request.Content),
                cancellationToken);
            return result.Status switch
            {
                CreateProjectSourceStatus.Success => Results.Ok(result.Source),
                CreateProjectSourceStatus.Invalid => Results.ValidationProblem(result.Errors),
                _ => Results.NotFound()
            };
        });

        group.MapDelete("/{resourceId:guid}/chapters/{chapterId:guid}", async (
            Guid projectId,
            Guid resourceId,
            Guid chapterId,
            ICommandDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await dispatcher.SendAsync(
                    new DeleteProjectSourceChapterCommand(projectId, resourceId, chapterId),
                    cancellationToken);
                return result.Status == CreateProjectSourceStatus.Success
                    ? Results.Ok(result.Source)
                    : Results.NotFound();
            }
            catch (InvalidOperationException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
        });

        return app;
    }
}

public sealed record CreateProjectSourceRequest(
    string Title,
    string? Description,
    string Content,
    string? FileName);

public sealed record AppendProjectSourceChaptersRequest(
    string Content,
    string? FileName);

public sealed record UpdateProjectSourceChapterRequest(
    string Title,
    string Content);