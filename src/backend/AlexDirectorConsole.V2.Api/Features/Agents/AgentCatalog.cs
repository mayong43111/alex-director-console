using AlexDirectorConsole.V2.Api.Features.Skills;
using AlexDirectorConsole.V2.Database.Data;
using Microsoft.EntityFrameworkCore;
using YamlDotNet.Serialization;

namespace AlexDirectorConsole.V2.Api.Features.Agents;

public sealed record AgentMetadata(
    Guid Id,
    string Name,
    string Description,
    string Version,
    string Prompt,
    IReadOnlyList<string> SkillIds,
    IReadOnlyList<string> AllowedTools,
    string SourcePath);

public interface IAgentCatalog
{
    IReadOnlyList<AgentMetadata> List();
    AgentMetadata? Get(Guid agentId);
}

public static class BuiltInAgentPromptLoader
{
    public static async Task<string> LoadAsync(
        V2DbContext dbContext,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        var prompt = await dbContext.AgentDefinitions.AsNoTracking()
            .Where(agent => agent.Id == agentId)
            .Select(agent => agent.SystemPrompt)
            .SingleOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(prompt)
            ? throw new InvalidOperationException($"内置 Agent 定义缺失：{agentId:D}。")
            : prompt;
    }
}

public sealed class AgentCatalog(IWebHostEnvironment environment, ISkillCatalog skillCatalog) : IAgentCatalog
{
    private readonly Lazy<IReadOnlyList<AgentMetadata>> agents = new(
        () => LoadAgents(environment, skillCatalog));

    public IReadOnlyList<AgentMetadata> List() => agents.Value;

    public AgentMetadata? Get(Guid agentId) => agents.Value.FirstOrDefault(agent => agent.Id == agentId);

    private static IReadOnlyList<AgentMetadata> LoadAgents(
        IWebHostEnvironment environment,
        ISkillCatalog skillCatalog)
    {
        var root = Path.Combine(environment.ContentRootPath, "Definitions", "agents");
        if (!Directory.Exists(root)) return [];

        var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
        var skills = skillCatalog.List().ToDictionary(skill => skill.Id, StringComparer.Ordinal);
        var loaded = new List<AgentMetadata>();
        foreach (var filePath in Directory.EnumerateFiles(root, "*.yaml", SearchOption.TopDirectoryOnly))
        {
            var definition = deserializer.Deserialize<AgentYamlDefinition>(File.ReadAllText(filePath))
                ?? throw new InvalidOperationException($"Agent YAML 无效：{filePath}");
            if (definition.SchemaVersion != 1 || definition.Kind != "agent")
            {
                throw new InvalidOperationException($"Agent YAML schemaVersion/kind 无效：{filePath}");
            }
            if (!Guid.TryParse(definition.Id, out var id)
                || string.IsNullOrWhiteSpace(definition.Name)
                || string.IsNullOrWhiteSpace(definition.Prompt))
            {
                throw new InvalidOperationException($"Agent id/name/prompt 无效：{filePath}");
            }

            var skillIds = definition.Skills
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var unknownSkills = skillIds.Where(skillId => !skills.ContainsKey(skillId)).ToArray();
            if (unknownSkills.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Agent {definition.Name} 引用了未知 Skill：{string.Join("、", unknownSkills)}。");
            }

            var allowedBySkills = skillIds
                .SelectMany(skillId => skills[skillId].AllowedTools)
                .ToHashSet(StringComparer.Ordinal);
            var allowedTools = definition.AllowedTools
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var unknownTools = allowedTools.Where(tool => !allowedBySkills.Contains(tool)).ToArray();
            if (unknownTools.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Agent {definition.Name} 引用了绑定 Skill 未授权的工具：{string.Join("、", unknownTools)}。");
            }

            loaded.Add(new AgentMetadata(
                id,
                definition.Name.Trim(),
                definition.Description.Trim(),
                string.IsNullOrWhiteSpace(definition.Version) ? "1.0.0" : definition.Version.Trim(),
                definition.Prompt.Trim(),
                skillIds,
                allowedTools,
                Path.GetRelativePath(environment.ContentRootPath, filePath).Replace('\\', '/')));
        }

        var duplicateId = loaded.GroupBy(agent => agent.Id).FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null) throw new InvalidOperationException($"Agent id 重复：{duplicateId.Key}");
        var duplicateName = loaded.GroupBy(agent => agent.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateName is not null) throw new InvalidOperationException($"Agent name 重复：{duplicateName.Key}");
        return loaded.OrderBy(agent => agent.Name, StringComparer.Ordinal).ToArray();
    }

    private sealed class AgentYamlDefinition
    {
        [YamlMember(Alias = "schemaVersion")]
        public int SchemaVersion { get; set; }
        [YamlMember(Alias = "kind")]
        public string Kind { get; set; } = string.Empty;
        [YamlMember(Alias = "id")]
        public string Id { get; set; } = string.Empty;
        [YamlMember(Alias = "name")]
        public string Name { get; set; } = string.Empty;
        [YamlMember(Alias = "description")]
        public string Description { get; set; } = string.Empty;
        [YamlMember(Alias = "version")]
        public string Version { get; set; } = string.Empty;
        [YamlMember(Alias = "prompt")]
        public string Prompt { get; set; } = string.Empty;
        [YamlMember(Alias = "skills")]
        public string[] Skills { get; set; } = [];
        [YamlMember(Alias = "allowedTools")]
        public string[] AllowedTools { get; set; } = [];
    }
}
