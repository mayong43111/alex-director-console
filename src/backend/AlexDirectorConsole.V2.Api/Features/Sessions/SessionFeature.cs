using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Agents;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Sessions;

public static class SessionScopes
{
    public const string GlobalProjectCenter = "global:project-center:assistant-director";

    public static string ProjectAssistantDirector(Guid projectId) =>
        $"project:{projectId:D}:assistant-director";
}

public sealed record SessionMessageView(
    Guid Id,
    long Sequence,
    string Role,
    string Content,
    string? Model,
    DateTimeOffset CreatedAtUtc);

public sealed record SessionSummaryView(
    Guid Id,
    Guid AgentId,
    string AgentName,
    string ScopeKey,
    Guid? ProjectId,
    string? ProjectName,
    string Title,
    string Runtime,
    int MessageCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record SessionView(
    Guid Id,
    Guid AgentId,
    string AgentName,
    string ScopeKey,
    Guid? ProjectId,
    string? ProjectName,
    string Title,
    string Runtime,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<SessionMessageView> Messages);

public sealed record ListSessionsQuery : IQuery<IReadOnlyList<SessionSummaryView>>;
public sealed record GetSessionQuery(Guid SessionId) : IQuery<SessionView?>;
public sealed record GetScopedSessionQuery(Guid AgentId, string ScopeKey) : IQuery<SessionView?>;

public sealed class ListSessionsQueryHandler(V2DbContext dbContext)
    : IQueryHandler<ListSessionsQuery, IReadOnlyList<SessionSummaryView>>
{
    public async Task<IReadOnlyList<SessionSummaryView>> HandleAsync(
        ListSessionsQuery query,
        CancellationToken cancellationToken)
    {
        var sessions = await (
            from session in dbContext.Sessions.AsNoTracking()
            join agent in dbContext.AgentDefinitions.AsNoTracking() on session.AgentId equals agent.Id
            join project in dbContext.Projects.AsNoTracking() on session.ProjectId equals project.Id into projects
            from project in projects.DefaultIfEmpty()
            select new
            {
                Session = session,
                AgentName = agent.Name,
                ProjectName = project == null ? null : project.Name
            })
            .ToListAsync(cancellationToken);
        var counts = await dbContext.SessionMessages
            .AsNoTracking()
            .GroupBy(message => message.SessionId)
            .Select(group => new { SessionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.SessionId, item => item.Count, cancellationToken);

        return sessions
            .OrderByDescending(item => item.Session.UpdatedAtUtc)
            .Select(item => new SessionSummaryView(
                item.Session.Id,
                item.Session.AgentId,
                item.AgentName,
                item.Session.ScopeKey,
                item.Session.ProjectId,
                item.ProjectName,
                item.Session.Title,
                item.Session.Runtime,
                counts.GetValueOrDefault(item.Session.Id),
                item.Session.CreatedAtUtc,
                item.Session.UpdatedAtUtc))
            .ToArray();
    }
}

public sealed class GetSessionQueryHandler(V2DbContext dbContext)
    : IQueryHandler<GetSessionQuery, SessionView?>,
      IQueryHandler<GetScopedSessionQuery, SessionView?>
{
    public async Task<SessionView?> HandleAsync(
        GetSessionQuery query,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.Sessions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == query.SessionId, cancellationToken);
        return session is null ? null : await MapAsync(session, cancellationToken);
    }

    public async Task<SessionView?> HandleAsync(
        GetScopedSessionQuery query,
        CancellationToken cancellationToken)
    {
        var scopeKey = query.ScopeKey.Trim();
        var session = await dbContext.Sessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.AgentId == query.AgentId && item.ScopeKey == scopeKey,
                cancellationToken);
        return session is null ? null : await MapAsync(session, cancellationToken);
    }

    private async Task<SessionView> MapAsync(Session session, CancellationToken cancellationToken)
    {
        var agentName = await dbContext.AgentDefinitions
            .AsNoTracking()
            .Where(agent => agent.Id == session.AgentId)
            .Select(agent => agent.Name)
            .SingleAsync(cancellationToken);
        var projectName = session.ProjectId is Guid projectId
            ? await dbContext.Projects
                .AsNoTracking()
                .Where(project => project.Id == projectId)
                .Select(project => project.Name)
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        var messages = await dbContext.SessionMessages
            .AsNoTracking()
            .Where(message => message.SessionId == session.Id)
            .OrderBy(message => message.Sequence)
            .Select(message => new SessionMessageView(
                message.Id,
                message.Sequence,
                message.Role,
                message.Content,
                message.Model,
                message.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new SessionView(
            session.Id,
            session.AgentId,
            agentName,
            session.ScopeKey,
            session.ProjectId,
            projectName,
            session.Title,
            session.Runtime,
            session.CreatedAtUtc,
            session.UpdatedAtUtc,
            messages);
    }
}

public sealed record SessionAgentContext(
    string ScopeKey,
    Guid? ProjectId,
    string? ProjectName,
    string Page,
    string Episode);

public sealed record SessionAgentReply(string Content, string Model, string Runtime);
public sealed record SessionHistoryMessage(string Role, string Content);

public interface ISessionAgent
{
    Task<SessionAgentReply> ReplyAsync(
        AgentView agent,
        SessionAgentContext context,
        IReadOnlyList<SessionHistoryMessage> history,
        string message,
        CancellationToken cancellationToken);
}

public enum SendSessionMessageStatus
{
    Success,
    AgentNotFound,
    ProjectNotFound,
    SessionNotFound,
    Invalid,
    NotConfigured,
    AgentFailed
}

public sealed record SendSessionMessageCommand(
    Guid AgentId,
    string? ScopeKey,
    Guid? SessionId,
    Guid? ProjectId,
    string? Title,
    string? Content,
    string? Page,
    string? Episode)
    : ICommand<SendSessionMessageResult>;

public sealed record SendSessionMessageResult(
    SendSessionMessageStatus Status,
    SessionView? Session,
    string? Error)
{
    public static SendSessionMessageResult Failed(SendSessionMessageStatus status, string error) =>
        new(status, null, error);
}

public sealed class SendSessionMessageCommandHandler(
    V2DbContext dbContext,
    ISessionAgent agentRuntime,
    TimeProvider timeProvider)
    : ICommandHandler<SendSessionMessageCommand, SendSessionMessageResult>
{
    public async Task<SendSessionMessageResult> HandleAsync(
        SendSessionMessageCommand command,
        CancellationToken cancellationToken)
    {
        var content = command.Content?.Trim() ?? string.Empty;
        var scopeKey = command.ScopeKey?.Trim() ?? string.Empty;
        if (content.Length is 0 or > 10_000)
        {
            return SendSessionMessageResult.Failed(
                SendSessionMessageStatus.Invalid,
                "消息不能为空且不能超过 10000 个字符。");
        }
        if (scopeKey.Length is 0 or > 500)
        {
            return SendSessionMessageResult.Failed(
                SendSessionMessageStatus.Invalid,
                "Session scopeKey 不能为空且不能超过 500 个字符。");
        }

        var agentDefinition = await dbContext.AgentDefinitions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == command.AgentId, cancellationToken);
        if (agentDefinition is null)
        {
            return SendSessionMessageResult.Failed(
                SendSessionMessageStatus.AgentNotFound,
                "Agent 不存在。");
        }
        var skillIds = await dbContext.AgentSkills
            .AsNoTracking()
            .Where(link => link.AgentId == command.AgentId)
            .OrderBy(link => link.SkillId)
            .Select(link => link.SkillId)
            .ToArrayAsync(cancellationToken);
        var agent = ListAgentsQueryHandler.Map(agentDefinition, skillIds);

        string? projectName = null;
        if (command.ProjectId is Guid projectId)
        {
            projectName = await dbContext.Projects
                .AsNoTracking()
                .Where(project => project.Id == projectId)
                .Select(project => project.Name)
                .SingleOrDefaultAsync(cancellationToken);
            if (projectName is null)
            {
                return SendSessionMessageResult.Failed(
                    SendSessionMessageStatus.ProjectNotFound,
                    "项目不存在。");
            }
        }

        Session? session;
        if (command.SessionId is Guid sessionId)
        {
            session = await dbContext.Sessions
                .SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken);
            if (session is null
                || session.AgentId != command.AgentId
                || !session.ScopeKey.Equals(scopeKey, StringComparison.Ordinal)
                || session.ProjectId != command.ProjectId)
            {
                return SendSessionMessageResult.Failed(
                    SendSessionMessageStatus.SessionNotFound,
                    "Session 不存在或与当前 Agent、scope 不匹配。");
            }
        }
        else
        {
            session = await dbContext.Sessions
                .SingleOrDefaultAsync(
                    item => item.AgentId == command.AgentId && item.ScopeKey == scopeKey,
                    cancellationToken);
            if (session is not null && session.ProjectId != command.ProjectId)
            {
                return SendSessionMessageResult.Failed(
                    SendSessionMessageStatus.Invalid,
                    "scopeKey 已绑定到其它项目。");
            }
        }

        var previousMessages = session is null
            ? []
            : await dbContext.SessionMessages
                .AsNoTracking()
                .Where(message => message.SessionId == session.Id)
                .OrderBy(message => message.Sequence)
                .ToListAsync(cancellationToken);

        SessionAgentReply reply;
        try
        {
            reply = await agentRuntime.ReplyAsync(
                agent,
                new SessionAgentContext(
                    scopeKey,
                    command.ProjectId,
                    projectName,
                    NormalizeContext(command.Page, "项目中心"),
                    NormalizeContext(command.Episode, "未选择")),
                previousMessages
                    .Select(message => new SessionHistoryMessage(message.Role, message.Content))
                    .ToArray(),
                content,
                cancellationToken);
        }
        catch (SessionsConfigurationException error)
        {
            return SendSessionMessageResult.Failed(SendSessionMessageStatus.NotConfigured, error.Message);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return SendSessionMessageResult.Failed(
                SendSessionMessageStatus.AgentFailed,
                "Agent 暂时无法回复，请检查语言模型连接后重试。");
        }

        var now = timeProvider.GetUtcNow();
        if (session is null)
        {
            session = new Session
            {
                AgentId = command.AgentId,
                ScopeKey = scopeKey,
                ProjectId = command.ProjectId,
                Title = NormalizeTitle(command.Title, projectName, agent.Name),
                Runtime = reply.Runtime,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            dbContext.Sessions.Add(session);
        }

        var nextSequence = previousMessages.Count == 0 ? 1 : previousMessages[^1].Sequence + 1;
        var userMessage = new SessionMessage
        {
            SessionId = session.Id,
            Sequence = nextSequence,
            Role = "user",
            Content = content,
            CreatedAtUtc = now
        };
        var assistantMessage = new SessionMessage
        {
            SessionId = session.Id,
            Sequence = nextSequence + 1,
            Role = "assistant",
            Content = reply.Content,
            Model = reply.Model,
            CreatedAtUtc = now
        };
        session.Runtime = reply.Runtime;
        session.UpdatedAtUtc = now;
        dbContext.SessionMessages.AddRange(userMessage, assistantMessage);
        await dbContext.SaveChangesAsync(cancellationToken);

        var messages = previousMessages
            .Select(ToView)
            .Append(ToView(userMessage))
            .Append(ToView(assistantMessage))
            .ToArray();
        return new SendSessionMessageResult(
            SendSessionMessageStatus.Success,
            new SessionView(
                session.Id,
                session.AgentId,
                agent.Name,
                session.ScopeKey,
                session.ProjectId,
                projectName,
                session.Title,
                session.Runtime,
                session.CreatedAtUtc,
                session.UpdatedAtUtc,
                messages),
            null);
    }

    private static SessionMessageView ToView(SessionMessage message) => new(
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

    private static string NormalizeTitle(string? value, string? projectName, string agentName)
    {
        var normalized = value?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized[..Math.Min(normalized.Length, 500)];
        }
        return projectName is null ? agentName : $"项目：{projectName}";
    }
}

public sealed record RetrySessionMessageCommand(
    Guid SessionId,
    Guid MessageId,
    string? Page,
    string? Episode)
    : ICommand<SendSessionMessageResult>;

public sealed class RetrySessionMessageCommandHandler(
    V2DbContext dbContext,
    ISessionAgent agentRuntime,
    TimeProvider timeProvider)
    : ICommandHandler<RetrySessionMessageCommand, SendSessionMessageResult>
{
    public async Task<SendSessionMessageResult> HandleAsync(
        RetrySessionMessageCommand command,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.Sessions
            .SingleOrDefaultAsync(item => item.Id == command.SessionId, cancellationToken);
        if (session is null)
        {
            return SendSessionMessageResult.Failed(
                SendSessionMessageStatus.SessionNotFound,
                "Session 不存在。");
        }

        var messages = await dbContext.SessionMessages
            .Where(message => message.SessionId == session.Id)
            .OrderBy(message => message.Sequence)
            .ToListAsync(cancellationToken);
        var target = messages.SingleOrDefault(message => message.Id == command.MessageId);
        if (target is null)
        {
            return SendSessionMessageResult.Failed(
                SendSessionMessageStatus.SessionNotFound,
                "消息不存在或不属于当前 Session。");
        }
        if (!target.Role.Equals("user", StringComparison.Ordinal))
        {
            return SendSessionMessageResult.Failed(
                SendSessionMessageStatus.Invalid,
                "只能从用户消息重试。");
        }

        var agentDefinition = await dbContext.AgentDefinitions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == session.AgentId, cancellationToken);
        if (agentDefinition is null)
        {
            return SendSessionMessageResult.Failed(
                SendSessionMessageStatus.AgentNotFound,
                "Agent 不存在。");
        }
        var skillIds = await dbContext.AgentSkills
            .AsNoTracking()
            .Where(link => link.AgentId == session.AgentId)
            .OrderBy(link => link.SkillId)
            .Select(link => link.SkillId)
            .ToArrayAsync(cancellationToken);
        var agent = ListAgentsQueryHandler.Map(agentDefinition, skillIds);

        string? projectName = null;
        if (session.ProjectId is Guid projectId)
        {
            projectName = await dbContext.Projects
                .AsNoTracking()
                .Where(project => project.Id == projectId)
                .Select(project => project.Name)
                .SingleOrDefaultAsync(cancellationToken);
            if (projectName is null)
            {
                return SendSessionMessageResult.Failed(
                    SendSessionMessageStatus.ProjectNotFound,
                    "项目不存在。");
            }
        }

        var retainedMessages = messages
            .Where(message => message.Sequence < target.Sequence)
            .ToArray();
        SessionAgentReply reply;
        try
        {
            reply = await agentRuntime.ReplyAsync(
                agent,
                new SessionAgentContext(
                    session.ScopeKey,
                    session.ProjectId,
                    projectName,
                    NormalizeContext(command.Page, "项目中心"),
                    NormalizeContext(command.Episode, "未选择")),
                retainedMessages
                    .Select(message => new SessionHistoryMessage(message.Role, message.Content))
                    .ToArray(),
                target.Content,
                cancellationToken);
        }
        catch (SessionsConfigurationException error)
        {
            return SendSessionMessageResult.Failed(SendSessionMessageStatus.NotConfigured, error.Message);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return SendSessionMessageResult.Failed(
                SendSessionMessageStatus.AgentFailed,
                "Agent 暂时无法回复，请检查语言模型连接后重试。");
        }

        var now = timeProvider.GetUtcNow();
        dbContext.SessionMessages.RemoveRange(
            messages.Where(message => message.Sequence >= target.Sequence));
        var userMessage = new SessionMessage
        {
            SessionId = session.Id,
            Sequence = target.Sequence,
            Role = "user",
            Content = target.Content,
            CreatedAtUtc = now
        };
        var assistantMessage = new SessionMessage
        {
            SessionId = session.Id,
            Sequence = target.Sequence + 1,
            Role = "assistant",
            Content = reply.Content,
            Model = reply.Model,
            CreatedAtUtc = now
        };
        dbContext.SessionMessages.AddRange(userMessage, assistantMessage);
        session.Runtime = reply.Runtime;
        session.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new SendSessionMessageResult(
            SendSessionMessageStatus.Success,
            new SessionView(
                session.Id,
                session.AgentId,
                agent.Name,
                session.ScopeKey,
                session.ProjectId,
                projectName,
                session.Title,
                session.Runtime,
                session.CreatedAtUtc,
                session.UpdatedAtUtc,
                retainedMessages
                    .Select(ToView)
                    .Append(ToView(userMessage))
                    .Append(ToView(assistantMessage))
                    .ToArray()),
            null);
    }

    private static SessionMessageView ToView(SessionMessage message) => new(
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

public sealed class SessionsConfigurationException(string message) : InvalidOperationException(message);

public sealed record ClearSessionMessagesCommand(Guid SessionId) : ICommand<bool>;

public sealed class ClearSessionMessagesCommandHandler(V2DbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<ClearSessionMessagesCommand, bool>
{
    public async Task<bool> HandleAsync(
        ClearSessionMessagesCommand command,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.Sessions
            .SingleOrDefaultAsync(item => item.Id == command.SessionId, cancellationToken);
        if (session is null) return false;

        var messages = await dbContext.SessionMessages
            .Where(message => message.SessionId == session.Id)
            .ToListAsync(cancellationToken);
        dbContext.SessionMessages.RemoveRange(messages);
        session.UpdatedAtUtc = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
