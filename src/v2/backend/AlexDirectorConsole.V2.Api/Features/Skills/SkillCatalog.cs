using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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
            .WithNamingConvention(HyphenatedNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var loaded = new List<SkillMetadata>();
        foreach (var filePath in Directory.EnumerateFiles(root, "SKILL.md", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(filePath);
            var parts = content.Split("---", 3, StringSplitOptions.None);
            if (parts.Length != 3 || !string.IsNullOrWhiteSpace(parts[0]))
            {
                throw new InvalidOperationException($"Skill 文件缺少 YAML frontmatter：{filePath}");
            }

            var frontmatter = deserializer.Deserialize<SkillFrontmatter>(parts[1])
                ?? throw new InvalidOperationException($"Skill frontmatter 无效：{filePath}");
            var directoryName = Path.GetFileName(Path.GetDirectoryName(filePath));
            if (string.IsNullOrWhiteSpace(frontmatter.Name)
                || !frontmatter.Name.Equals(directoryName, StringComparison.Ordinal)
                || !frontmatter.Name.All(character => char.IsLower(character)
                    || char.IsDigit(character)
                    || character == '-'))
            {
                throw new InvalidOperationException($"Skill name 必须是与目录名一致的小写短横线标识：{filePath}");
            }
            if (string.IsNullOrWhiteSpace(frontmatter.Description))
            {
                throw new InvalidOperationException($"Skill description 不能为空：{filePath}");
            }

            loaded.Add(new SkillMetadata(
                frontmatter.Name,
                string.IsNullOrWhiteSpace(frontmatter.Title) ? frontmatter.Name : frontmatter.Title.Trim(),
                frontmatter.Description.Trim(),
                string.IsNullOrWhiteSpace(frontmatter.Version) ? "1.0.0" : frontmatter.Version.Trim(),
                (frontmatter.AllowedTools ?? string.Empty)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                content,
                Path.GetRelativePath(root, filePath).Replace('\\', '/')));
        }

        return loaded.OrderBy(skill => skill.Title, StringComparer.Ordinal).ToArray();
    }

    private sealed class SkillFrontmatter
    {
        public string Name { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string? AllowedTools { get; set; }
    }
}