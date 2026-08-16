using AlexDirectorConsole.V2.Api.Application.Cqrs;
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

public sealed class GetCopilotConversationQueryHandler(V2DbContext dbContext)
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

        var conversation = await dbContext.CopilotConversations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ProjectId == query.ProjectId, cancellationToken);
        if (conversation is null)
        {
            return new CopilotConversationView(null, query.ProjectId, "MAF HarnessAgent", []);
        }

        var messages = await dbContext.CopilotMessages
            .AsNoTracking()
            .Where(message => message.ConversationId == conversation.Id)
            .OrderBy(message => message.Sequence)
            .Select(message => new CopilotMessageView(
                message.Id,
                message.Sequence,
                message.Role,
                message.Content,
                message.Model,
                message.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        return new CopilotConversationView(conversation.Id, query.ProjectId, "MAF HarnessAgent", messages);
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
    V2DbContext dbContext,
    IProjectCopilotAgent agent,
    TimeProvider timeProvider)
    : ICommandHandler<SendCopilotMessageCommand, SendCopilotMessageResult>
{
    public async Task<SendCopilotMessageResult> HandleAsync(
        SendCopilotMessageCommand command,
        CancellationToken cancellationToken)
    {
        var content = command.Content?.Trim() ?? string.Empty;
        if (content.Length is 0 or > 10_000)
        {
            return SendCopilotMessageResult.Failed(
                SendCopilotMessageStatus.Invalid,
                "消息不能为空且不能超过 10000 个字符。");
        }
        var page = NormalizeContext(command.Page, "项目工作区");
        var episode = NormalizeContext(command.Episode, "未选择");

        var project = await dbContext.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == command.ProjectId, cancellationToken);
        if (project is null)
        {
            return SendCopilotMessageResult.Failed(
                SendCopilotMessageStatus.ProjectNotFound,
                "项目不存在。");
        }

        var conversation = await dbContext.CopilotConversations
            .SingleOrDefaultAsync(item => item.ProjectId == command.ProjectId, cancellationToken);
        var previousMessages = conversation is null
            ? []
            : await dbContext.CopilotMessages
                .AsNoTracking()
                .Where(message => message.ConversationId == conversation.Id)
                .OrderBy(message => message.Sequence)
                .ToListAsync(cancellationToken);

        CopilotAgentReply reply;
        try
        {
            reply = await agent.ReplyAsync(
                command.ProjectId,
                project.Name,
                page,
                episode,
                previousMessages
                    .Select(message => new CopilotHistoryMessage(message.Role, message.Content))
                    .ToArray(),
                content,
                cancellationToken);
        }
        catch (CopilotConfigurationException error)
        {
            return SendCopilotMessageResult.Failed(
                SendCopilotMessageStatus.NotConfigured,
                error.Message);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return SendCopilotMessageResult.Failed(
                SendCopilotMessageStatus.AgentFailed,
                "GPT-5.4 暂时无法回复，请检查 Foundry 连接后重试。");
        }

        var now = timeProvider.GetUtcNow();
        if (conversation is null)
        {
            conversation = new CopilotConversation
            {
                ProjectId = command.ProjectId,
                CreatedAtUtc = now
            };
            dbContext.CopilotConversations.Add(conversation);
        }

        var nextSequence = previousMessages.Count == 0
            ? 1
            : previousMessages[^1].Sequence + 1;
        var userMessage = new CopilotMessage
        {
            ConversationId = conversation.Id,
            Sequence = nextSequence,
            Role = "user",
            Content = content,
            CreatedAtUtc = now
        };
        var assistantMessage = new CopilotMessage
        {
            ConversationId = conversation.Id,
            Sequence = nextSequence + 1,
            Role = "assistant",
            Content = reply.Content,
            Model = reply.Model,
            CreatedAtUtc = now
        };
        conversation.UpdatedAtUtc = now;
        dbContext.CopilotMessages.AddRange(userMessage, assistantMessage);
        await dbContext.SaveChangesAsync(cancellationToken);

        var messages = previousMessages
            .Select(ToView)
            .Append(ToView(userMessage))
            .Append(ToView(assistantMessage))
            .ToArray();
        return new SendCopilotMessageResult(
            SendCopilotMessageStatus.Success,
            new CopilotConversationView(conversation.Id, command.ProjectId, reply.Runtime, messages),
            null);
    }

    private static CopilotMessageView ToView(CopilotMessage message) => new(
        message.Id,
        message.Sequence,
        message.Role,
        message.Content,
        message.Model,
        message.CreatedAtUtc);

    private static string NormalizeContext(string? value, string fallback)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? fallback
            : normalized[..Math.Min(normalized.Length, 100)];
    }
}

public sealed record ResetCopilotConversationCommand(Guid ProjectId) : ICommand<bool>;

public sealed class ResetCopilotConversationCommandHandler(V2DbContext dbContext)
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

        var conversation = await dbContext.CopilotConversations
            .SingleOrDefaultAsync(item => item.ProjectId == command.ProjectId, cancellationToken);
        if (conversation is not null)
        {
            dbContext.CopilotConversations.Remove(conversation);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return true;
    }
}