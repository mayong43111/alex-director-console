using System.Text.Json;
using AlexDirectorConsole.Api.Application.Assets;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class PurgeProjectResourceVersionsTool(IAssetWriter assetWriter) : IDirectorTool
{
    public string Name => "purge_project_resource_versions";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, CancellationToken, Task<string>>)(async (resourceIds, cancellationToken) =>
        {
            var rawIds = resourceIds.Split(
                [',', '，', ';', '；', '、', '\n', '\r'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (rawIds.Length == 0 || rawIds.Length > 100)
            {
                throw new ArgumentException("resourceIds 必须包含 1 到 100 个 shot 资源 ID。", nameof(resourceIds));
            }

            var assetIds = new List<Guid>(rawIds.Length);
            foreach (var rawId in rawIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Guid.TryParse(rawId, out var assetId))
                {
                    throw new ArgumentException($"无效的 shot 资源 ID：{rawId}", nameof(resourceIds));
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
                    message = $"Agent 正在清理 {assetIds.Count} 个镜头的历史版本"
                }, cancellationToken);

                var purged = await assetWriter.PurgeOlderVersionsAsync(
                    context.ProjectId,
                    assetIds,
                    "shot",
                    cancellationToken);
                if (purged.Count == 0)
                {
                    throw new InvalidOperationException(
                        "至少一个资源不是当前项目中的 shot 或已被删除，本次未清理任何版本。请重新列出 shot 后用准确 ID 重试。");
                }

                var deletedVersionCount = purged.Sum(item => item.DeletedVersionCount);
                await context.WriteEventAsync(new
                {
                    type = "process",
                    stage = "tool.completed",
                    message = $"已保留 {purged.Count} 个镜头的最新版，删除 {deletedVersionCount} 个历史版本"
                }, cancellationToken);
                return JsonSerializer.Serialize(new
                {
                    resources = purged,
                    keptResourceCount = purged.Count,
                    deletedVersionCount
                }, context.JsonOptions);
            }
            finally
            {
                context.ResourceLock.Release();
            }
        }),
        name: Name,
        description: "清理当前项目 shot 的历史版本，同时保留每个逻辑镜头的最新版本。用于导演要求删除旧分镜、旧记录或旧版本但保留当前分镜时；不得用于删除整个镜头。resourceIds 接收 1 到 100 个当前 shot 资产 ID，调用前后必须 list_project_resources 核验。",
        serializerOptions: context.JsonOptions);
}
