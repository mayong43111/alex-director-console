using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AlexDirectorConsole.Api.Services;

public sealed record ProjectSkillMetadata(
    string Name,
    string Title,
    string Description,
    string Version,
    IReadOnlyList<string> AllowedTools,
    string Content,
    string FilePath);

public interface IProjectSkillCatalog
{
    IReadOnlyList<ProjectSkillMetadata> List();
    ProjectSkillMetadata? Get(string name);
}

public sealed class ProjectSkillCatalog(IWebHostEnvironment environment) : IProjectSkillCatalog
{
    private readonly Lazy<IReadOnlyList<ProjectSkillMetadata>> skills = new(() => LoadSkills(environment));

    public IReadOnlyList<ProjectSkillMetadata> List() => skills.Value;

    public ProjectSkillMetadata? Get(string name) => skills.Value.FirstOrDefault(
        skill => skill.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<ProjectSkillMetadata> LoadSkills(IWebHostEnvironment environment)
    {
        var root = Path.Combine(environment.ContentRootPath, "Skills");
        if (!Directory.Exists(root))
        {
            return [];
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(HyphenatedNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var loaded = new List<ProjectSkillMetadata>();
        foreach (var filePath in Directory.EnumerateFiles(root, "SKILL.md", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(filePath);
            var parts = content.Split("---", 3, StringSplitOptions.None);
            if (parts.Length != 3 || !string.IsNullOrWhiteSpace(parts[0]))
            {
                throw new InvalidOperationException($"技能文件缺少 YAML frontmatter：{filePath}");
            }

            var metadata = deserializer.Deserialize<SkillFrontmatter>(parts[1])
                ?? throw new InvalidOperationException($"技能 frontmatter 无效：{filePath}");
            var directoryName = Path.GetFileName(Path.GetDirectoryName(filePath));
            if (string.IsNullOrWhiteSpace(metadata.Name)
                || !metadata.Name.Equals(directoryName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"技能 name 必须与目录名一致：{filePath}");
            }
            if (string.IsNullOrWhiteSpace(metadata.Description))
            {
                throw new InvalidOperationException($"技能 description 不能为空：{filePath}");
            }

            loaded.Add(new ProjectSkillMetadata(
                metadata.Name,
                string.IsNullOrWhiteSpace(metadata.Title) ? metadata.Name : metadata.Title,
                metadata.Description,
                string.IsNullOrWhiteSpace(metadata.Version) ? "1.0.0" : metadata.Version,
                (metadata.AllowedTools ?? string.Empty)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                content,
                filePath));
        }

        return loaded.OrderBy(skill => skill.Name, StringComparer.Ordinal).ToArray();
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
