using System.Text.Json;
using AlexDirectorConsole.Api.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class ListProjectResourcesTool : IDirectorTool
{
    public string Name => "list_project_resources";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, string, CancellationToken, Task<string>>)(async (
            resourceType,
            nameContains,
            cancellationToken) =>
        {
            var normalizedType = resourceType.Trim();
            var normalizedName = nameContains.Trim();
            var assets = await context.DbContext.Assets
                .AsNoTracking()
                .Where(asset => asset.ProjectId == context.ProjectId)
                .ToListAsync(cancellationToken);
            var latestResources = assets
                .GroupBy(asset => asset.ResourceId)
                .Select(group => group
                    .OrderByDescending(asset => asset.Version)
                    .ThenByDescending(asset => asset.CreatedAtUtc)
                    .First())
                .Where(asset => string.IsNullOrWhiteSpace(normalizedType)
                    || normalizedType.Equals("all", StringComparison.OrdinalIgnoreCase)
                    || asset.Type.Equals(normalizedType, StringComparison.OrdinalIgnoreCase))
                .Where(asset => string.IsNullOrWhiteSpace(normalizedName)
                    || asset.Name.Contains(normalizedName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(asset => asset.Type)
                .ThenBy(asset => asset.Name)
                .Take(100)
                .Select(AssetResponse.FromAsset)
                .ToArray();

            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "resources.listed",
                message = $"已找到 {latestResources.Length} 个项目资源"
            }, cancellationToken);
            return JsonSerializer.Serialize(latestResources, context.JsonOptions);
        }),
        name: Name,
        description: "列出当前项目中的最新资源版本，用于在未选中目标资源时自主发现剧本、分镜、shot、人物、场景、道具和媒体。resourceType 可传 shot、script、character、scene、prop、media 或 all；nameContains 用于名称筛选，可传空字符串。返回的 id 可交给 read_project_resource_contents 或其他工具。",
        serializerOptions: context.JsonOptions);
}