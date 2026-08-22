using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Generation;
using AlexDirectorConsole.V2.Api.Features.Projects;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AlexDirectorConsole.V2.Api.Features.Projects.CreateProject;

public static class CreateProjectEndpoint
{
    public static IEndpointRouteBuilder MapCreateProject(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v2/projects",
                async (
                    CreateProjectRequest request,
                    ICommandDispatcher dispatcher,
                    CancellationToken cancellationToken) =>
                {
                    var result = await dispatcher.SendAsync(
                        new CreateProjectCommand(request.Name, request.Description),
                        cancellationToken);

                    if (!result.IsSuccess)
                    {
                        return Results.ValidationProblem(result.Errors);
                    }

                    var project = result.Project!;
                    return Results.Created($"/api/v2/projects/{project.Id}", project);
                })
            .WithName("CreateV2Project")
            .Produces<ProjectView>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        endpoints.MapPost(
                "/api/v2/projects/assist-description",
                async (
                    AssistProjectDescriptionRequest request,
                    IGenerationTaskScheduler scheduler,
                    V2DbContext dbContext,
                    CancellationToken cancellationToken) =>
                {
                    var name = request.Name?.Trim();
                    var description = request.Description?.Trim();
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
                    {
                        return Results.BadRequest(new { error = "请先填写项目名称和描述。" });
                    }

                    var descriptionAgent = await dbContext.AgentDefinitions
                        .AsNoTracking()
                        .SingleOrDefaultAsync(
                            agent => agent.Id == BuiltInAgents.ProjectDescriptionWriterId,
                            cancellationToken);
                    if (descriptionAgent is null)
                    {
                        return Results.Conflict(new { error = "项目介绍助手未配置，请先在 Agent 管理中创建或恢复该 Agent。" });
                    }
                    var context = JsonSerializer.SerializeToElement(new { projectName = name });
                    var assistRequest = new ProjectSettingsAssistRequest(
                        "description",
                        description,
                        null,
                        context,
                        descriptionAgent.SystemPrompt);
                    return Results.Accepted(value: await scheduler.EnqueueAsync(
                        GenerationTaskTypes.ProjectDescriptionAssist,
                        "优化项目描述",
                        new(Guid.Empty, RequestJson: JsonSerializer.Serialize(assistRequest)),
                        cancellationToken));
                })
            .WithName("AssistV2ProjectDescription")
            .Produces<ProjectSettingsAssistView>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        return endpoints;
    }

    public sealed record CreateProjectRequest(string? Name, string? Description);

    public sealed record AssistProjectDescriptionRequest(string? Name, string? Description);
}