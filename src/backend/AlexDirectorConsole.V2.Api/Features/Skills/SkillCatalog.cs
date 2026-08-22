using YamlDotNet.Serialization;

namespace AlexDirectorConsole.V2.Api.Features.Skills;

public sealed record SkillMetadata(
    string Id,
    string Title,
    string Description,
    string Version,
    IReadOnlyList<string> AllowedTools,
    string Content,
    string SourcePath);

public interface ISkillCatalog
{
    IReadOnlyList<SkillMetadata> List();
    SkillMetadata? Get(string skillId);
}

public sealed class SkillCatalog(IWebHostEnvironment environment) : ISkillCatalog
{
    private readonly Lazy<IReadOnlyList<SkillMetadata>> skills = new(
        () => LoadSkills(environment));

    public IReadOnlyList<SkillMetadata> List() => skills.Value;

    public SkillMetadata? Get(string skillId) => skills.Value.FirstOrDefault(
        skill => skill.Id.Equals(skillId.Trim(), StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<SkillMetadata> LoadSkills(IWebHostEnvironment environment)
    {
        var root = Path.Combine(environment.ContentRootPath, "Skills");
        if (!Directory.Exists(root)) return [];

        var deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();
        var loaded = new List<SkillMetadata>();
        foreach (var filePath in Directory.EnumerateFiles(root, "skill.yaml", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(filePath);
            var definition = deserializer.Deserialize<SkillYamlDefinition>(content)
                ?? throw new InvalidOperationException($"Skill YAML 无效：{filePath}");
            var directoryName = Path.GetFileName(Path.GetDirectoryName(filePath));
            if (definition.SchemaVersion != 1 || definition.Kind != "skill")
            {
                throw new InvalidOperationException($"Skill YAML schemaVersion/kind 无效：{filePath}");
            }
            if (string.IsNullOrWhiteSpace(definition.Id)
                || !definition.Id.Equals(directoryName, StringComparison.Ordinal)
                || !definition.Id.All(character => char.IsLower(character)
                    || char.IsDigit(character)
                    || character == '-'))
            {
                throw new InvalidOperationException($"Skill id 必须是与目录名一致的小写短横线标识：{filePath}");
            }
            if (string.IsNullOrWhiteSpace(definition.Description)
                || string.IsNullOrWhiteSpace(definition.Instructions))
            {
                throw new InvalidOperationException($"Skill description/instructions 不能为空：{filePath}");
            }

            loaded.Add(new SkillMetadata(
                definition.Id,
                string.IsNullOrWhiteSpace(definition.Name) ? definition.Id : definition.Name.Trim(),
                definition.Description.Trim(),
                string.IsNullOrWhiteSpace(definition.Version) ? "1.0.0" : definition.Version.Trim(),
                definition.AllowedTools
                    .Where(tool => !string.IsNullOrWhiteSpace(tool))
                    .Select(tool => tool.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                definition.Instructions.Trim(),
                Path.GetRelativePath(root, filePath).Replace('\\', '/')));
        }

        return loaded.OrderBy(skill => skill.Title, StringComparer.Ordinal).ToArray();
    }

    private sealed class SkillYamlDefinition
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
        [YamlMember(Alias = "allowedTools")]
        public string[] AllowedTools { get; set; } = [];
        [YamlMember(Alias = "instructions")]
        public string Instructions { get; set; } = string.Empty;
    }
}