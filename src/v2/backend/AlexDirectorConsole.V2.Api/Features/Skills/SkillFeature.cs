using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Database.Data;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Skills;

public sealed record SkillView(
    string Id,
    string Name,
    string Description,
    string Version,
    bool IsEnabled,
    bool IsSystem,
    IReadOnlyList<string> AllowedTools,
    string Content,
    string SourcePath);

public sealed record ListSkillsQuery : IQuery<IReadOnlyList<SkillView>>;
public sealed record GetSkillQuery(string SkillId) : IQuery<SkillView?>;
public sealed record UpdateSkillCommand(string SkillId, bool IsEnabled) : ICommand<SkillView?>;

public sealed class ListSkillsQueryHandler(V2DbContext dbContext, ISkillCatalog catalog)
    : IQueryHandler<ListSkillsQuery, IReadOnlyList<SkillView>>
{
    public async Task<IReadOnlyList<SkillView>> HandleAsync(
        ListSkillsQuery query,
        CancellationToken cancellationToken)
    {
        var definitions = await dbContext.SkillDefinitions
            .AsNoTracking()
            .OrderBy(skill => skill.Name)
            .ToListAsync(cancellationToken);
        return definitions
            .Select(definition => Map(definition, catalog.Get(definition.Id)))
            .ToArray();
    }

    internal static SkillView Map(
        AlexDirectorConsole.V2.Database.Models.SkillDefinition definition,
        SkillMetadata? metadata) => new(
        definition.Id,
        definition.Name,
        definition.Description,
        definition.Version,
        definition.IsEnabled,
        definition.IsSystem,
        metadata?.AllowedTools ?? [],
        metadata?.Content ?? string.Empty,
        definition.SourcePath);
}

public sealed class GetSkillQueryHandler(V2DbContext dbContext, ISkillCatalog catalog)
    : IQueryHandler<GetSkillQuery, SkillView?>
{
    public async Task<SkillView?> HandleAsync(GetSkillQuery query, CancellationToken cancellationToken)
    {
        var definition = await dbContext.SkillDefinitions
            .AsNoTracking()
            .SingleOrDefaultAsync(skill => skill.Id == query.SkillId, cancellationToken);
        return definition is null ? null : ListSkillsQueryHandler.Map(definition, catalog.Get(definition.Id));
    }
}

public sealed class UpdateSkillCommandHandler(
    V2DbContext dbContext,
    ISkillCatalog catalog,
    TimeProvider timeProvider) : ICommandHandler<UpdateSkillCommand, SkillView?>
{
    public async Task<SkillView?> HandleAsync(UpdateSkillCommand command, CancellationToken cancellationToken)
    {
        var definition = await dbContext.SkillDefinitions
            .SingleOrDefaultAsync(skill => skill.Id == command.SkillId, cancellationToken);
        if (definition is null) return null;

        definition.IsEnabled = command.IsEnabled;
        definition.UpdatedAtUtc = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return ListSkillsQueryHandler.Map(definition, catalog.Get(definition.Id));
    }
}