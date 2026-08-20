using AlexDirectorConsole.V2.Api.Application.Cqrs;
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
                    IProjectSettingsAssistant assistant,
                    V2DbContext dbContext,
                    CancellationToken cancellationToken) =>
                {
                    var name = request.Name?.Trim();
                    var description = request.Description?.Trim();
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
                    {
                        return Results.BadRequest(new { error = "请先填写项目名称和描述。" });
                    }

                    try
                    {
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
                        var result = await assistant.WriteAsync(
                            new ProjectSettingsAssistRequest(
                                "description",
                                description,
                                null,
                                context,
                                descriptionAgent.SystemPrompt),
                            cancellationToken);
                        return Results.Ok(result);
                    }
                    catch (ArgumentException error)
                    {
                        return Results.BadRequest(new { error = error.Message });
                    }
                    catch (ProjectGenerationConfigurationException error)
                    {
                        return Results.Conflict(new { error = error.Message });
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        return Results.Problem(
                            title: "项目描述优化失败",
                            detail: error.Message,
                            statusCode: StatusCodes.Status502BadGateway);
                    }
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