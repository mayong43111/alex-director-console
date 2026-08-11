using System.Text.Json;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class ReadProjectResourceContentsTool : IDirectorTool
{
    public string Name => "read_project_resource_contents";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, CancellationToken, Task<string>>)(async (assetIds, cancellationToken) =>
        {
            var ids = assetIds
                .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .Take(20)
                .ToArray();
            if (ids.Length == 0)
            {
                throw new ArgumentException("至少需要一个有效的资源资产 ID。", nameof(assetIds));
            }

            var assets = await context.DbContext.Assets
                .AsNoTracking()
                .Where(asset => asset.ProjectId == context.ProjectId && ids.Contains(asset.Id))
                .ToListAsync(cancellationToken);
            if (assets.Count != ids.Length)
            {
                throw new ArgumentException("部分资源不存在或不属于当前项目。", nameof(assetIds));
            }

            var results = new List<object>();
            foreach (var id in ids)
            {
                var asset = assets.Single(item => item.Id == id);
                if (!IsTextAsset(asset))
                {
                    throw new ArgumentException($"资源不是可读取的文本：{asset.Name}", nameof(assetIds));
                }

                await using var source = await context.BlobStorage.OpenReadAsync(asset.BlobKey, cancellationToken)
                    ?? throw new InvalidOperationException($"资源内容不存在：{asset.Name}");
                using var reader = new StreamReader(source, detectEncodingFromByteOrderMarks: true);
                results.Add(new
                {
                    asset = AssetResponse.FromAsset(asset),
                    content = await reader.ReadToEndAsync(cancellationToken)
                });
            }

            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "resources.read",
                message = $"已读取 {results.Count} 个项目资源的完整正文"
            }, cancellationToken);
            return JsonSerializer.Serialize(results, context.JsonOptions);
        }),
        name: Name,
        description: "按 list_project_resources 返回的资产 ID 读取当前项目中文本资源的完整正文。assetIds 用逗号分隔，最多 20 个；可读取剧本、分镜、shot、人物、场景、道具等文本资源，不依赖界面当前选择。",
        serializerOptions: context.JsonOptions);

    private static bool IsTextAsset(Asset asset) =>
        asset.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || asset.ContentType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(asset.FileName).Equals(".md", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(asset.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(asset.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase);
}