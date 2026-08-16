using AlexDirectorConsole.V2.Api.Application.Cqrs;

namespace AlexDirectorConsole.V2.Api.Features.Skills;

public sealed record UpdateSkillRequest(bool IsEnabled);

public static class SkillEndpoints
{
    public static IEndpointRouteBuilder MapSkills(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/skills");

        group.MapGet("/", async (
            IQueryDispatcher queryDispatcher,
            CancellationToken cancellationToken) => Results.Ok(
                await queryDispatcher.QueryAsync(new ListSkillsQuery(), cancellationToken)));

        group.MapGet("/{skillId}", async (
            string skillId,
            IQueryDispatcher queryDispatcher,
            CancellationToken cancellationToken) =>
        {
            var skill = await queryDispatcher.QueryAsync(new GetSkillQuery(skillId), cancellationToken);
            return skill is null ? Results.NotFound() : Results.Ok(skill);
        });

        group.MapPatch("/{skillId}", async (
            string skillId,
            UpdateSkillRequest request,
            ICommandDispatcher commandDispatcher,
            CancellationToken cancellationToken) =>
        {
            var skill = await commandDispatcher.SendAsync(
                new UpdateSkillCommand(skillId, request.IsEnabled),
                cancellationToken);
            return skill is null ? Results.NotFound() : Results.Ok(skill);
        });

        return app;
    }
}