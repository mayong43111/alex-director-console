using AlexDirectorConsole.V2.Api.Application.Cqrs;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Sources;

public sealed record SourceEpisodeScriptResult(
    ProjectSourceView Source,
    AdaptationScriptView Adaptation,
    ProductionScriptPackageView Script);

public interface IStoryProductionToolService
{
    Task<ProjectSourceView> CreateStorySourceAsync(
        Guid projectId,
        string title,
        string? description,
        string content,
        CancellationToken cancellationToken);

    Task<SourceEpisodeScriptResult> GenerateSourceEpisodeScriptAsync(
        Guid projectId,
        Guid sourceResourceId,
        int episodeNumber,
        CancellationToken cancellationToken);
}

public sealed class StoryProductionToolService(
    ICommandDispatcher commandDispatcher,
    IQueryDispatcher queryDispatcher) : IStoryProductionToolService
{
    public async Task<ProjectSourceView> CreateStorySourceAsync(
        Guid projectId,
        string title,
        string? description,
        string content,
        CancellationToken cancellationToken)
    {
        var result = await commandDispatcher.SendAsync(
            new CreateProjectSourceCommand(projectId, title, description, content, null),
            cancellationToken);
        return result.Status switch
        {
            CreateProjectSourceStatus.Success => result.Source!,
            CreateProjectSourceStatus.ProjectNotFound => throw new KeyNotFoundException("项目不存在。"),
            _ => throw new InvalidOperationException(string.Join(
                " ",
                result.Errors.SelectMany(error => error.Value)))
        };
    }

    public async Task<SourceEpisodeScriptResult> GenerateSourceEpisodeScriptAsync(
        Guid projectId,
        Guid sourceResourceId,
        int episodeNumber,
        CancellationToken cancellationToken)
    {
        if (episodeNumber < 1)
        {
            throw new InvalidOperationException("集号必须大于或等于 1。");
        }

        var source = await queryDispatcher.QueryAsync(
            new GetProjectSourceQuery(projectId, sourceResourceId),
            cancellationToken)
            ?? throw new KeyNotFoundException("故事来源不存在。");
        if (episodeNumber > source.Chapters.Count)
        {
            throw new InvalidOperationException("指定集号超出原分集数量。");
        }

        var adaptation = await commandDispatcher.SendAsync(
            new GenerateAdaptationScriptCommand(
                projectId,
                sourceResourceId,
                AdaptationModes.SourceChapters,
                null,
                null),
            cancellationToken)
            ?? throw new KeyNotFoundException("故事来源不存在。");
        var confirmed = await commandDispatcher.SendAsync(
            new ConfirmAdaptationScriptCommand(projectId, sourceResourceId, episodeNumber),
            cancellationToken)
            ?? throw new KeyNotFoundException("原分集不存在。");
        if (confirmed.ProductionEpisodeMap?.TryGetValue(episodeNumber, out var productionEpisodeId) != true)
        {
            throw new InvalidOperationException("正式剧本已生成，但未找到对应的生产集。");
        }

        var script = await queryDispatcher.QueryAsync(
            new GetProductionScriptPackageQuery(projectId, productionEpisodeId),
            cancellationToken)
            ?? throw new InvalidOperationException("正式剧本包读取失败。");
        return new SourceEpisodeScriptResult(source, confirmed, script);
    }
}