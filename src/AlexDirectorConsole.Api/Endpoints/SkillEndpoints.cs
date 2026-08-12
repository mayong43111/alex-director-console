using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Data;
using AlexDirectorConsole.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.Api.Endpoints;

public static class SkillEndpoints
{
    public static IEndpointRouteBuilder MapSkillEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/skills",
            async (
                AppDbContext dbContext,
                IProjectSkillCatalog skillCatalog,
                CancellationToken cancellationToken) =>
            {
                var definitions = await dbContext.SkillDefinitions
                    .AsNoTracking()
                    .OrderBy(skill => skill.Name)
                    .ToListAsync(cancellationToken);
                var skills = definitions.Select(skill =>
                {
                    var catalogSkill = skillCatalog.Get(skill.Id);
                    return new SkillDefinitionResponse(
                        skill.Id,
                        skill.Name,
                        skill.Description,
                        skill.Version,
                        skill.IsEnabled,
                        skill.IsSystem,
                        catalogSkill?.Title ?? skill.Name,
                        catalogSkill?.AllowedTools ?? [],
                        catalogSkill?.Content ?? string.Empty);
                });
                return Results.Ok(skills);
            })
            .WithName("GetSkills")
            .WithOpenApi();

        app.MapPatch(
            "/api/skills/{skillId}",
            async (
                string skillId,
                UpdateSkillRequest request,
                AppDbContext dbContext,
                IProjectSkillCatalog skillCatalog,
                CancellationToken cancellationToken) =>
            {
                var skill = await dbContext.SkillDefinitions
                    .SingleOrDefaultAsync(item => item.Id == skillId, cancellationToken);
                if (skill is null)
                {
                    return Results.NotFound();
                }

                skill.IsEnabled = request.IsEnabled;
                skill.UpdatedAtUtc = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                var catalogSkill = skillCatalog.Get(skill.Id);
                return Results.Ok(new SkillDefinitionResponse(
                    skill.Id,
                    skill.Name,
                    skill.Description,
                    skill.Version,
                    skill.IsEnabled,
                    skill.IsSystem,
                    catalogSkill?.Title ?? skill.Name,
                    catalogSkill?.AllowedTools ?? [],
                    catalogSkill?.Content ?? string.Empty));
            })
            .WithName("UpdateSkill")
            .WithOpenApi();

        app.MapGet(
            "/api/projects/{projectId:guid}/skill-runs",
            async (Guid projectId, AppDbContext dbContext, CancellationToken cancellationToken) =>
            {
                var runs = await dbContext.SkillRuns
                    .AsNoTracking()
                    .Where(run => run.ProjectId == projectId)
                    .OrderByDescending(run => run.StartedAtUtc)
                    .Take(50)
                    .ToListAsync(cancellationToken);
                return Results.Ok(runs.Select(SkillRunResponse.FromRun));
            })
            .WithName("GetProjectSkillRuns")
            .WithOpenApi();

        return app;
    }
}