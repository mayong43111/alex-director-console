using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Skills;

public interface ISkillCatalogSynchronizer
{
    Task SynchronizeAsync(CancellationToken cancellationToken = default);
}

public sealed class SkillCatalogSynchronizer(
    V2DbContext dbContext,
    ISkillCatalog catalog,
    TimeProvider timeProvider) : ISkillCatalogSynchronizer
{
    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var catalogSkills = catalog.List();
        var definitions = await dbContext.SkillDefinitions
            .ToDictionaryAsync(skill => skill.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var metadata in catalogSkills)
        {
            if (!definitions.TryGetValue(metadata.Id, out var definition))
            {
                definition = new SkillDefinition
                {
                    Id = metadata.Id,
                    IsEnabled = true,
                    IsSystem = true,
                    CreatedAtUtc = now
                };
                dbContext.SkillDefinitions.Add(definition);
            }

            definition.Name = metadata.Title;
            definition.Description = metadata.Description;
            definition.Version = metadata.Version;
            definition.SourcePath = metadata.SourcePath;
            definition.UpdatedAtUtc = now;
        }

        var catalogIds = catalogSkills.Select(skill => skill.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removedSystemSkills = definitions.Values
            .Where(skill => skill.IsSystem && !catalogIds.Contains(skill.Id));
        dbContext.SkillDefinitions.RemoveRange(removedSystemSkills);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}