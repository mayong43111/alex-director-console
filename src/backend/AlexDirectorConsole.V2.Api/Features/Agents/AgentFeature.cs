using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Agents;

public sealed record AgentView(
    Guid Id,
    string Name,
    string SystemPrompt,
    IReadOnlyList<string> SkillIds,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record SaveAgentInput(string? Name, string? SystemPrompt, IReadOnlyList<string>? SkillIds);
public sealed record SaveAgentResult(AgentView? Agent, string Status, IReadOnlyDictionary<string, string[]> Errors);
public sealed record ListAgentsQuery : IQuery<IReadOnlyList<AgentView>>;
public sealed record GetAgentQuery(Guid AgentId) : IQuery<AgentView?>;
public sealed record CreateAgentCommand(SaveAgentInput Input) : ICommand<SaveAgentResult>;
public sealed record UpdateAgentCommand(Guid AgentId, SaveAgentInput Input) : ICommand<SaveAgentResult>;
public sealed record DeleteAgentCommand(Guid AgentId) : ICommand<bool>;

public sealed class ListAgentsQueryHandler(V2DbContext dbContext)
    : IQueryHandler<ListAgentsQuery, IReadOnlyList<AgentView>>
{
    public async Task<IReadOnlyList<AgentView>> HandleAsync(
        ListAgentsQuery query,
        CancellationToken cancellationToken)
    {
        var agents = await dbContext.AgentDefinitions
            .AsNoTracking()
            .OrderBy(agent => agent.Name)
            .ToListAsync(cancellationToken);
        var links = await dbContext.AgentSkills
            .AsNoTracking()
            .OrderBy(link => link.SkillId)
            .ToListAsync(cancellationToken);
        var skillIdsByAgent = links
            .GroupBy(link => link.AgentId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Select(link => link.SkillId).ToArray());
        return agents.Select(agent => Map(agent, skillIdsByAgent.GetValueOrDefault(agent.Id, []))).ToArray();
    }

    internal static AgentView Map(AgentDefinition agent, IReadOnlyList<string> skillIds) => new(
        agent.Id,
        agent.Name,
        agent.SystemPrompt,
        skillIds,
        agent.CreatedAtUtc,
        agent.UpdatedAtUtc);
}

public sealed class GetAgentQueryHandler(V2DbContext dbContext) : IQueryHandler<GetAgentQuery, AgentView?>
{
    public async Task<AgentView?> HandleAsync(GetAgentQuery query, CancellationToken cancellationToken)
    {
        var agent = await dbContext.AgentDefinitions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == query.AgentId, cancellationToken);
        if (agent is null) return null;

        var skillIds = await dbContext.AgentSkills
            .AsNoTracking()
            .Where(link => link.AgentId == query.AgentId)
            .OrderBy(link => link.SkillId)
            .Select(link => link.SkillId)
            .ToArrayAsync(cancellationToken);
        return ListAgentsQueryHandler.Map(agent, skillIds);
    }
}

public sealed class CreateAgentCommandHandler(V2DbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<CreateAgentCommand, SaveAgentResult>
{
    public async Task<SaveAgentResult> HandleAsync(CreateAgentCommand command, CancellationToken cancellationToken)
    {
        var validated = await AgentInputValidator.ValidateAsync(dbContext, command.Input, null, cancellationToken);
        if (validated.Result is not null) return validated.Result;

        var now = timeProvider.GetUtcNow();
        var agent = new AgentDefinition
        {
            Name = validated.Name,
            SystemPrompt = validated.SystemPrompt,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.AgentDefinitions.Add(agent);
        dbContext.AgentSkills.AddRange(validated.SkillIds.Select(skillId => new AgentSkill
        {
            AgentId = agent.Id,
            SkillId = skillId
        }));
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(ListAgentsQueryHandler.Map(agent, validated.SkillIds), "saved", EmptyErrors);
    }

    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>();
}

public sealed class UpdateAgentCommandHandler(V2DbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<UpdateAgentCommand, SaveAgentResult>
{
    public async Task<SaveAgentResult> HandleAsync(UpdateAgentCommand command, CancellationToken cancellationToken)
    {
        var agent = await dbContext.AgentDefinitions
            .SingleOrDefaultAsync(item => item.Id == command.AgentId, cancellationToken);
        if (agent is null) return new(null, "not-found", EmptyErrors);

        var validated = await AgentInputValidator.ValidateAsync(
            dbContext,
            command.Input,
            command.AgentId,
            cancellationToken);
        if (validated.Result is not null) return validated.Result;

        agent.Name = validated.Name;
        agent.SystemPrompt = validated.SystemPrompt;
        agent.UpdatedAtUtc = timeProvider.GetUtcNow();
        var existingLinks = await dbContext.AgentSkills
            .Where(link => link.AgentId == command.AgentId)
            .ToListAsync(cancellationToken);
        dbContext.AgentSkills.RemoveRange(existingLinks);
        dbContext.AgentSkills.AddRange(validated.SkillIds.Select(skillId => new AgentSkill
        {
            AgentId = agent.Id,
            SkillId = skillId
        }));
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(ListAgentsQueryHandler.Map(agent, validated.SkillIds), "saved", EmptyErrors);
    }

    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>();
}

public sealed class DeleteAgentCommandHandler(V2DbContext dbContext) : ICommandHandler<DeleteAgentCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteAgentCommand command, CancellationToken cancellationToken)
    {
        var agent = await dbContext.AgentDefinitions
            .SingleOrDefaultAsync(item => item.Id == command.AgentId, cancellationToken);
        if (agent is null) return false;
        dbContext.AgentDefinitions.Remove(agent);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}

internal sealed record ValidatedAgentInput(
    string Name,
    string SystemPrompt,
    IReadOnlyList<string> SkillIds,
    SaveAgentResult? Result = null);

internal static class AgentInputValidator
{
    public static async Task<ValidatedAgentInput> ValidateAsync(
        V2DbContext dbContext,
        SaveAgentInput input,
        Guid? currentAgentId,
        CancellationToken cancellationToken)
    {
        var name = input.Name?.Trim() ?? string.Empty;
        var systemPrompt = input.SystemPrompt?.Trim() ?? string.Empty;
        var skillIds = (input.SkillIds ?? [])
            .Where(skillId => !string.IsNullOrWhiteSpace(skillId))
            .Select(skillId => skillId.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(skillId => skillId, StringComparer.Ordinal)
            .ToArray();
        var errors = new Dictionary<string, string[]>();
        if (name.Length == 0) errors["name"] = ["Agent 名称不能为空。"];
        else if (name.Length > 200) errors["name"] = ["Agent 名称不能超过 200 个字符。"];
        if (systemPrompt.Length == 0) errors["systemPrompt"] = ["系统提示词不能为空。"];
        else if (systemPrompt.Length > 100000) errors["systemPrompt"] = ["系统提示词不能超过 100000 个字符。"];
        if (errors.Count > 0) return Invalid(name, systemPrompt, skillIds, errors);

        var duplicateName = await dbContext.AgentDefinitions.AsNoTracking().AnyAsync(
            agent => agent.Name == name && (!currentAgentId.HasValue || agent.Id != currentAgentId.Value),
            cancellationToken);
        if (duplicateName) return Invalid(name, systemPrompt, skillIds, new Dictionary<string, string[]>
        {
            ["name"] = ["Agent 名称已存在。"]
        }, "conflict");

        var knownSkillIds = await dbContext.SkillDefinitions
            .AsNoTracking()
            .Where(skill => skillIds.Contains(skill.Id))
            .Select(skill => skill.Id)
            .ToArrayAsync(cancellationToken);
        var unknownSkillIds = skillIds.Except(knownSkillIds, StringComparer.Ordinal).ToArray();
        if (unknownSkillIds.Length > 0) return Invalid(name, systemPrompt, skillIds, new Dictionary<string, string[]>
        {
            ["skillIds"] = [$"以下技能不存在：{string.Join("、", unknownSkillIds)}。"]
        });
        return new(name, systemPrompt, skillIds);
    }

    private static ValidatedAgentInput Invalid(
        string name,
        string systemPrompt,
        IReadOnlyList<string> skillIds,
        IReadOnlyDictionary<string, string[]> errors,
        string status = "invalid") => new(name, systemPrompt, skillIds, new(null, status, errors));
}