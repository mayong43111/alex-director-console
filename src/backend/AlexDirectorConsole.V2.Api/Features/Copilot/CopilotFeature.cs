using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Sessions;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Copilot;

public sealed record CopilotMessageView(
    Guid Id,
    long Sequence,
    string Role,
    string Content,
    string? Model,
    DateTimeOffset CreatedAtUtc);

public sealed record CopilotConversationView(
    Guid? ConversationId,
    Guid ProjectId,
    string Runtime,
    IReadOnlyList<CopilotMessageView> Messages);

public sealed record GetCopilotConversationQuery(Guid ProjectId)
    : IQuery<CopilotConversationView?>;

public sealed class GetCopilotConversationQueryHandler(
    V2DbContext dbContext,
    IQueryDispatcher queryDispatcher)
    : IQueryHandler<GetCopilotConversationQuery, CopilotConversationView?>
{
    public async Task<CopilotConversationView?> HandleAsync(
        GetCopilotConversationQuery query,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Projects.AnyAsync(project => project.Id == query.ProjectId, cancellationToken))
        {
            return null;
        }

        var session = await queryDispatcher.QueryAsync(
            new GetScopedSessionQuery(
                BuiltInAgents.AssistantDirectorId,
                SessionScopes.ProjectAssistantDirector(query.ProjectId)),
            cancellationToken);
        if (session is null)
        {
            return new CopilotConversationView(null, query.ProjectId, "MAF HarnessAgent", []);
        }

        return new CopilotConversationView(
            session.Id,
            query.ProjectId,
            session.Runtime,
            session.Messages.Select(message => new CopilotMessageView(
                message.Id,
                message.Sequence,
                message.Role,
                message.Content,
                message.Model,
                message.CreatedAtUtc)).ToArray());
    }
}

public enum SendCopilotMessageStatus
{
    Success,
    ProjectNotFound,
    Invalid,
    NotConfigured,
    AgentFailed
}

public sealed record SendCopilotMessageCommand(
    Guid ProjectId,
    string? Content,
    string? Page,
    string? Episode)
    : ICommand<SendCopilotMessageResult>;

public sealed record SendCopilotMessageResult(
    SendCopilotMessageStatus Status,
    CopilotConversationView? Conversation,
    string? Error)
{
    public static SendCopilotMessageResult Failed(SendCopilotMessageStatus status, string error) =>
        new(status, null, error);
}

public sealed class SendCopilotMessageCommandHandler(
    ICommandDispatcher commandDispatcher)
    : ICommandHandler<SendCopilotMessageCommand, SendCopilotMessageResult>
{
    public async Task<SendCopilotMessageResult> HandleAsync(
        SendCopilotMessageCommand command,
        CancellationToken cancellationToken)
    {
        var result = await commandDispatcher.SendAsync(
            new SendSessionMessageCommand(
                BuiltInAgents.AssistantDirectorId,
                SessionScopes.ProjectAssistantDirector(command.ProjectId),
                null,
                command.ProjectId,
                null,
                command.Content,
                command.Page,
                command.Episode),
            cancellationToken);
        if (result.Session is not null)
        {
            return new SendCopilotMessageResult(
                SendCopilotMessageStatus.Success,
                new CopilotConversationView(
                    result.Session.Id,
                    command.ProjectId,
                    result.Session.Runtime,
                    result.Session.Messages.Select(message => new CopilotMessageView(
                        message.Id,
                        message.Sequence,
                        message.Role,
                        message.Content,
                        message.Model,
                        message.CreatedAtUtc)).ToArray()),
                null);
        }
        return SendCopilotMessageResult.Failed(
            result.Status switch
            {
                SendSessionMessageStatus.ProjectNotFound => SendCopilotMessageStatus.ProjectNotFound,
                SendSessionMessageStatus.Invalid => SendCopilotMessageStatus.Invalid,
                SendSessionMessageStatus.NotConfigured => SendCopilotMessageStatus.NotConfigured,
                _ => SendCopilotMessageStatus.AgentFailed
            },
            result.Error ?? "Agent 暂时无法回复。");
    }
}

public sealed record ResetCopilotConversationCommand(Guid ProjectId) : ICommand<bool>;

public sealed class ResetCopilotConversationCommandHandler(
    V2DbContext dbContext,
    ICommandDispatcher commandDispatcher)
    : ICommandHandler<ResetCopilotConversationCommand, bool>
{
    public async Task<bool> HandleAsync(
        ResetCopilotConversationCommand command,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Projects.AnyAsync(project => project.Id == command.ProjectId, cancellationToken))
        {
            return false;
        }

        var sessionId = await dbContext.Sessions
            .AsNoTracking()
            .Where(item => item.AgentId == BuiltInAgents.AssistantDirectorId
                && item.ScopeKey == SessionScopes.ProjectAssistantDirector(command.ProjectId))
            .Select(item => (Guid?)item.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (sessionId is Guid id)
        {
            await commandDispatcher.SendAsync(new ClearSessionMessagesCommand(id), cancellationToken);
        }
        return true;
    }
}