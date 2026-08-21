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

        var assistantDirectorSkillIds = new[]
        {
            "project-management",
            "script-writing",
            "script-breakdown",
            "storyboard-design",
            "shot-first-frame"
        };
        if (await dbContext.AgentDefinitions.AnyAsync(
            agent => agent.Id == BuiltInAgents.AssistantDirectorId,
            cancellationToken))
        {
            var existingSkillIds = (await dbContext.AgentSkills
                .Where(link => link.AgentId == BuiltInAgents.AssistantDirectorId)
                .Select(link => link.SkillId)
                .ToArrayAsync(cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var skillId in assistantDirectorSkillIds
                .Where(catalogIds.Contains)
                .Where(skillId => !existingSkillIds.Contains(skillId)))
            {
                dbContext.AgentSkills.Add(new AgentSkill
                {
                    AgentId = BuiltInAgents.AssistantDirectorId,
                    SkillId = skillId
                });
            }
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}