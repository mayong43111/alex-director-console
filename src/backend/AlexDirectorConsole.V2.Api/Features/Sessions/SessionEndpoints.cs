using AlexDirectorConsole.V2.Api.Application.Cqrs;

namespace AlexDirectorConsole.V2.Api.Features.Sessions;

public sealed record SendSessionMessageRequest(
    Guid AgentId,
    string? ScopeKey,
    Guid? SessionId,
    Guid? ProjectId,
    string? Title,
    string? Content,
    string? Page,
    string? Episode);

public sealed record RetrySessionMessageRequest(string? Page, string? Episode);

public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessions(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/sessions");

        group.MapGet("/", async (
            IQueryDispatcher queryDispatcher,
            CancellationToken cancellationToken) => Results.Ok(
                await queryDispatcher.QueryAsync(new ListSessionsQuery(), cancellationToken)));

        group.MapGet("/scoped", async (
            Guid agentId,
            string scopeKey,
            IQueryDispatcher queryDispatcher,
            CancellationToken cancellationToken) =>
        {
            var session = await queryDispatcher.QueryAsync(
                new GetScopedSessionQuery(agentId, scopeKey),
                cancellationToken);
            return session is null ? Results.NotFound() : Results.Ok(session);
        });

        group.MapGet("/{sessionId:guid}", async (
            Guid sessionId,
            IQueryDispatcher queryDispatcher,
            CancellationToken cancellationToken) =>
        {
            var session = await queryDispatcher.QueryAsync(
                new GetSessionQuery(sessionId),
                cancellationToken);
            return session is null ? Results.NotFound() : Results.Ok(session);
        });

        group.MapPost("/messages", async (
            SendSessionMessageRequest request,
            ICommandDispatcher commandDispatcher,
            CancellationToken cancellationToken) =>
        {
            var result = await commandDispatcher.SendAsync(
                new SendSessionMessageCommand(
                    request.AgentId,
                    request.ScopeKey,
                    request.SessionId,
                    request.ProjectId,
                    request.Title,
                    request.Content,
                    request.Page,
                    request.Episode),
                cancellationToken);
            return result.Status switch
            {
                SendSessionMessageStatus.Success => Results.Ok(result.Session),
                SendSessionMessageStatus.AgentNotFound or
                    SendSessionMessageStatus.ProjectNotFound or
                    SendSessionMessageStatus.SessionNotFound => Results.NotFound(),
                SendSessionMessageStatus.Invalid => Results.BadRequest(new { error = result.Error }),
                SendSessionMessageStatus.NotConfigured => Results.Conflict(new { error = result.Error }),
                _ => Results.Problem(result.Error, statusCode: StatusCodes.Status502BadGateway)
            };
        });

        group.MapPost("/{sessionId:guid}/messages/{messageId:guid}/retry", async (
            Guid sessionId,
            Guid messageId,
            RetrySessionMessageRequest request,
            ICommandDispatcher commandDispatcher,
            CancellationToken cancellationToken) =>
        {
            var result = await commandDispatcher.SendAsync(
                new RetrySessionMessageCommand(
                    sessionId,
                    messageId,
                    request.Page,
                    request.Episode),
                cancellationToken);
            return result.Status switch
            {
                SendSessionMessageStatus.Success => Results.Ok(result.Session),
                SendSessionMessageStatus.AgentNotFound or
                    SendSessionMessageStatus.ProjectNotFound or
                    SendSessionMessageStatus.SessionNotFound => Results.NotFound(),
                SendSessionMessageStatus.Invalid => Results.BadRequest(new { error = result.Error }),
                SendSessionMessageStatus.NotConfigured => Results.Conflict(new { error = result.Error }),
                _ => Results.Problem(result.Error, statusCode: StatusCodes.Status502BadGateway)
            };
        });

        group.MapDelete("/{sessionId:guid}/messages", async (
            Guid sessionId,
            ICommandDispatcher commandDispatcher,
            CancellationToken cancellationToken) =>
        {
            var deleted = await commandDispatcher.SendAsync(
                new ClearSessionMessagesCommand(sessionId),
                cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        return app;
    }
}
