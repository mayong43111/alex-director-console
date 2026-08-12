using AlexDirectorConsole.Api.Services;
using Microsoft.Agents.AI;

namespace AlexDirectorConsole.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", () => Results.Ok(new
        {
            status = "ok",
            service = "AlexDirectorConsole.Api",
            timestamp = DateTimeOffset.UtcNow
        }))
        .WithName("GetHealth")
        .WithOpenApi();

        app.MapGet("/api/agent/status", (
            IDirectorAgent directorAgent,
            IAzureFoundryImageGenerator imageGenerator) =>
        {
            return Results.Ok(new
            {
                framework = "Microsoft Agent Framework",
                frameworkVersion = typeof(AIAgent).Assembly.GetName().Version?.ToString(),
                runtime = directorAgent.Runtime,
                skillsRuntime = directorAgent.SkillsRuntime,
                deployment = directorAgent.Deployment,
                configured = directorAgent.IsConfigured,
                imageDeployment = imageGenerator.Deployment,
                imageQuality = imageGenerator.Quality,
                imageConfigured = imageGenerator.IsConfigured
            });
        })
        .WithName("GetAgentStatus")
        .WithOpenApi();

        return app;
    }
}