using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using System.Text.Json;

namespace AlexDirectorConsole.V2.Api.Features.Agents;

public sealed record SaveAgentRequest(string? Name, string? SystemPrompt, IReadOnlyList<string>? SkillIds);
public sealed record InvokeAgentRequest(string? Input, JsonElement Context, int? MaxLength);

public static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgents(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/agents");

        group.MapGet("/", async (IQueryDispatcher queries, CancellationToken cancellationToken) =>
            Results.Ok(await queries.QueryAsync(new ListAgentsQuery(), cancellationToken)));

        group.MapGet("/{agentId:guid}", async (
            Guid agentId,
            IQueryDispatcher queries,
            CancellationToken cancellationToken) =>
        {
            var agent = await queries.QueryAsync(new GetAgentQuery(agentId), cancellationToken);
            return agent is null ? Results.NotFound() : Results.Ok(agent);
        });

        group.MapPost("/{agentId:guid}/invoke", async (
            Guid agentId,
            InvokeAgentRequest request,
            IQueryDispatcher queries,
            IAgentTextInvoker invoker,
            CancellationToken cancellationToken) =>
        {
            if (request.Input?.Length > 100000)
            {
                return Results.BadRequest(new { error = "输入内容不能超过 100000 个字符。" });
            }
            if (request.MaxLength is <= 0 or > 100000)
            {
                return Results.BadRequest(new { error = "最大长度必须在 1 到 100000 之间。" });
            }

            var agent = await queries.QueryAsync(new GetAgentQuery(agentId), cancellationToken);
            if (agent is null) return Results.NotFound();

            try
            {
                var result = await invoker.InvokeAsync(
                    new AgentTextInvocation(
                        agent,
                        request.Input?.Trim() ?? string.Empty,
                        request.Context,
                        request.MaxLength),
                    cancellationToken);
                return Results.Ok(result);
            }
            catch (ProjectGenerationConfigurationException error)
            {
                return Results.Conflict(new { error = error.Message });
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return Results.Problem(
                    title: "Agent 调用失败",
                    detail: error.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });

        group.MapPost("/", async (
            SaveAgentRequest request,
            ICommandDispatcher commands,
            CancellationToken cancellationToken) =>
        {
            var result = await commands.SendAsync(
                new CreateAgentCommand(new(request.Name, request.SystemPrompt, request.SkillIds)),
                cancellationToken);
            return MapSaveResult(result, true);
        });

        group.MapPut("/{agentId:guid}", async (
            Guid agentId,
            SaveAgentRequest request,
            ICommandDispatcher commands,
            CancellationToken cancellationToken) =>
        {
            var result = await commands.SendAsync(
                new UpdateAgentCommand(agentId, new(request.Name, request.SystemPrompt, request.SkillIds)),
                cancellationToken);
            return MapSaveResult(result, false);
        });

        group.MapDelete("/{agentId:guid}", async (
            Guid agentId,
            ICommandDispatcher commands,
            CancellationToken cancellationToken) =>
            await commands.SendAsync(new DeleteAgentCommand(agentId), cancellationToken)
                ? Results.NoContent()
                : Results.NotFound());

        return app;
    }

    private static IResult MapSaveResult(SaveAgentResult result, bool created) => result.Status switch
    {
        "not-found" => Results.NotFound(),
        "conflict" => Results.Conflict(new { errors = result.Errors }),
        "invalid" => Results.ValidationProblem(result.Errors.ToDictionary()),
        _ when created => Results.Created($"/api/v2/agents/{result.Agent!.Id}", result.Agent),
        _ => Results.Ok(result.Agent)
    };
}