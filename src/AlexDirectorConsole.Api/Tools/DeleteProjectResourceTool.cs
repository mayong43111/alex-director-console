using System.Text.Json;
using AlexDirectorConsole.Api.Application.Assets;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class DeleteProjectResourceTool(IAssetWriter assetWriter) : IDirectorTool
{
    public string Name => "delete_project_resource";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, CancellationToken, Task<string>>)(async (resourceIds, cancellationToken) =>
        {
            var rawIds = resourceIds.Split(
                [',', '，', ';', '；', '、', '\n', '\r'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (rawIds.Length == 0 || rawIds.Length > 100)
            {
                throw new ArgumentException("resourceIds 必须包含 1 到 100 个项目资源 ID。", nameof(resourceIds));
            }
            var assetIds = new List<Guid>(rawIds.Length);
            foreach (var rawId in rawIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Guid.TryParse(rawId, out var assetId))
                {
                    throw new ArgumentException($"无效的项目资源 ID：{rawId}", nameof(resourceIds));
                }
                assetIds.Add(assetId);
            }

            await context.ResourceLock.WaitAsync(cancellationToken);
            try
            {
                await context.WriteEventAsync(new
                {
                    type = "process",
                    stage = "tool.started",
                    message = $"Agent 正在删除 {assetIds.Count} 个项目资源"
                }, cancellationToken);

                var deleted = await assetWriter.DeleteResourcesAsync(
                    context.ProjectId,
                    assetIds,
                    cancellationToken);
                if (deleted.Count == 0)
                {
                    throw new InvalidOperationException(
                    "至少一个资源不属于当前项目或已被删除，本次未删除任何资源。请重新列出当前项目资源后，用准确 ID 重试。");
                }

                await context.WriteEventAsync(new
                {
                    type = "process",
                    stage = "tool.completed",
                    message = $"已删除 {deleted.Count} 个项目资源，共 {deleted.Sum(item => item.VersionCount)} 个版本"
                }, cancellationToken);
                return JsonSerializer.Serialize(deleted, context.JsonOptions);
            }
            finally
            {
                context.ResourceLock.Release();
            }
        }),
        name: Name,
        description: "批量永久删除当前项目中的逻辑资源、全部版本和镜头绑定。resourceIds 接收 1 到 100 个由逗号或换行分隔的资源 id。导演说‘V1 的都删除了’‘旧版都删了’等省略‘把’字的口语时，按删除命令处理。必须先调用 list_project_resources 确认范围，调用后再次列出资源复查；只有工具成功且复查目标为零才能声称已删除。此操作不可撤销。",
        serializerOptions: context.JsonOptions);
}