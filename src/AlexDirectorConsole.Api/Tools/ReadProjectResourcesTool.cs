using System.Text.Json;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class ReadProjectResourcesTool : IDirectorTool
{
    public string Name => "read_project_resources";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, CancellationToken, Task<string>>)(async (resourceNames, cancellationToken) =>
        {
            await context.ResourceLock.WaitAsync(cancellationToken);
            try
            {
                var names = SplitResourceNames(resourceNames);
                if (names.Count == 0)
                {
                    throw new ArgumentException("至少需要一个资源名称。", nameof(resourceNames));
                }

                await context.WriteEventAsync(new
                {
                    type = "process",
                    stage = "tool.started",
                    message = $"Agent 正在读取项目资源：{string.Join("、", names)}"
                }, cancellationToken);
                var candidates = await context.DbContext.Assets
                    .AsNoTracking()
                    .Where(asset => asset.ProjectId == context.ProjectId)
                    .ToListAsync(cancellationToken);
                var matches = candidates
                    .Where(IsTextAsset)
                    .OrderByDescending(asset => asset.CreatedAtUtc)
                    .Where(asset => names.Any(name =>
                        asset.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
                    .GroupBy(asset => names.First(name =>
                        asset.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
                    .Select(group => group.First())
                    .ToList();
                var results = new List<object>();
                foreach (var match in matches)
                {
                    await using var source = await context.BlobStorage.OpenReadAsync(
                        match.BlobKey,
                        cancellationToken);
                    if (source is null || !IsTextAsset(match))
                    {
                        continue;
                    }
                    using var reader = new StreamReader(source, detectEncodingFromByteOrderMarks: true);
                    results.Add(new
                    {
                        asset = AssetResponse.FromAsset(match),
                        content = await reader.ReadToEndAsync(cancellationToken)
                    });
                }
                await context.WriteEventAsync(new
                {
                    type = "process",
                    stage = "tool.completed",
                    message = $"已读取 {results.Count} 个项目资源"
                }, cancellationToken);
                return JsonSerializer.Serialize(results, context.JsonOptions);
            }
            catch (Exception exception)
            {
                await context.WriteEventAsync(new
                {
                    type = "process",
                    stage = "tool.failed",
                    message = $"读取项目资源失败：{exception.GetType().Name}: {exception.Message}"
                }, CancellationToken.None);
                throw;
            }
            finally
            {
                context.ResourceLock.Release();
            }
        }),
        name: Name,
        description: "按名称读取当前项目中匹配的最新文本资源及完整正文；不要求 Agent 指定内部资源类型。resourceNames 用顿号或逗号分隔。使用其他工具操作已有对象前调用。",
        serializerOptions: context.JsonOptions);

    private static IReadOnlyList<string> SplitResourceNames(string value) =>
        value.Split(['、', ',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

    private static bool IsTextAsset(Asset asset) =>
        asset.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || asset.ContentType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(asset.FileName).Equals(".md", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(asset.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(asset.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase);
}
