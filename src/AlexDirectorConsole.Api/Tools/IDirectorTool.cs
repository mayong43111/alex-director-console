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
}

public sealed class DirectorToolRegistry(IEnumerable<IDirectorTool> tools) : IDirectorToolRegistry
{
    public IReadOnlyList<AITool> CreateTools(DirectorToolContext context) => tools
        .Where(tool => tool.IsAvailable(context))
        .Select(tool => tool.Create(context))
        .ToArray();
}
