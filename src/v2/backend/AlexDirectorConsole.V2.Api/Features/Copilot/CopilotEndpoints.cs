using AlexDirectorConsole.V2.Api.Application.Cqrs;

namespace AlexDirectorConsole.V2.Api.Features.Copilot;

public sealed record SendCopilotMessageRequest(
    string? Content,
    string? Page,
    string? Episode);

public static class CopilotEndpoints
{
    public static IEndpointRouteBuilder MapCopilot(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/projects/{projectId:guid}/copilot");

        group.MapGet("/messages", async (
            Guid projectId,
            IQueryDispatcher queryDispatcher,
            CancellationToken cancellationToken) =>
        {
            var conversation = await queryDispatcher.QueryAsync(
                new GetCopilotConversationQuery(projectId),
                cancellationToken);
            return conversation is null ? Results.NotFound() : Results.Ok(conversation);
        });

        group.MapPost("/messages", async (
            Guid projectId,
            SendCopilotMessageRequest request,
            ICommandDispatcher commandDispatcher,
            CancellationToken cancellationToken) =>
        {
            var result = await commandDispatcher.SendAsync(
                new SendCopilotMessageCommand(
                    projectId,
                    request.Content,
                    request.Page,
                    request.Episode),
                cancellationToken);
            return result.Status switch
            {
                SendCopilotMessageStatus.Success => Results.Ok(result.Conversation),
                SendCopilotMessageStatus.ProjectNotFound => Results.NotFound(),
                SendCopilotMessageStatus.Invalid => Results.BadRequest(new { error = result.Error }),
                SendCopilotMessageStatus.NotConfigured => Results.Conflict(new { error = result.Error }),
                _ => Results.Problem(result.Error, statusCode: StatusCodes.Status502BadGateway)
            };
        });

        group.MapDelete("/messages", async (
            Guid projectId,
            ICommandDispatcher commandDispatcher,
            CancellationToken cancellationToken) =>
        {
            var projectExists = await commandDispatcher.SendAsync(
                new ResetCopilotConversationCommand(projectId),
                cancellationToken);
            return projectExists ? Results.NoContent() : Results.NotFound();
        });

        return app;
    }
}