using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public interface IDirectorTool
{
    string Name { get; }
    bool IsAvailable(DirectorToolContext context) => true;
    AITool Create(DirectorToolContext context);
}

public interface IDirectorToolRegistry
{
    IReadOnlyList<AITool> CreateTools(DirectorToolContext context);
    IReadOnlyList<AITool> CreateTools(
        DirectorToolContext context,
        IReadOnlySet<string> allowedNames);
}

public sealed class DirectorToolRegistry(IEnumerable<IDirectorTool> tools) : IDirectorToolRegistry
{
    private readonly IDirectorTool[] registeredTools = tools.ToArray();

    public IReadOnlyList<AITool> CreateTools(DirectorToolContext context) => registeredTools
        .Where(tool => tool.IsAvailable(context))
        .Select(tool => tool.Create(context))
        .ToArray();

    public IReadOnlyList<AITool> CreateTools(
        DirectorToolContext context,
        IReadOnlySet<string> allowedNames)
    {
        var selected = registeredTools
            .Where(tool => allowedNames.Contains(tool.Name))
            .ToArray();
        var missing = allowedNames
            .Where(name => selected.All(tool => !tool.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"生产阶段缺少工具：{string.Join("、", missing)}。");
        }
        var unavailable = selected.Where(tool => !tool.IsAvailable(context)).Select(tool => tool.Name).ToArray();
        if (unavailable.Length > 0)
        {
            throw new InvalidOperationException($"生产阶段工具未配置：{string.Join("、", unavailable)}。");
        }
        return selected.Select(tool => tool.Create(context)).ToArray();
    }
}
