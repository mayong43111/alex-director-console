using System.Text.Json;
using AlexDirectorConsole.Api.Application.Assets;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Models;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class InspectVisualReferencesTool(IAssetReader assetReader) : IDirectorTool
{
    public string Name => "inspect_visual_references";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, CancellationToken, Task<string>>)(async (resourceNames, cancellationToken) =>
        {
            var names = resourceNames
                .Split(['、', ',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToArray();
            if (names.Length == 0)
            {
                throw new ArgumentException("至少需要一个人物、场景或道具名称。", nameof(resourceNames));
            }

            var assets = await assetReader.ListAsync(context.ProjectId, cancellationToken: cancellationToken);
            var results = names.Select(name =>
            {
                var setup = assets
                    .Where(asset => IsSetupAsset(asset)
                        && asset.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(asset => asset.Version)
                    .ThenByDescending(asset => asset.CreatedAtUtc)
                    .FirstOrDefault();
                var imageCandidates = assets
                    .Where(asset => asset.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                        && asset.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(asset => asset.ResourceId)
                    .Select(group => group.OrderByDescending(asset => asset.Version).First())
                    .OrderByDescending(asset => asset.CreatedAtUtc)
                    .Take(10)
                    .Select(AssetResponse.FromAsset)
                    .ToArray();
                return new
                {
                    name,
                    setup = setup is null ? null : AssetResponse.FromAsset(setup),
                    imageCandidates,
                    hasReferenceImage = imageCandidates.Length > 0
                };
            }).ToArray();

            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "references.inspected",
                message = $"已检查 {results.Length} 个视觉对象，其中 {results.Count(result => result.hasReferenceImage)} 个已有参考图"
            }, cancellationToken);
            return JsonSerializer.Serialize(results, context.JsonOptions);
        }),
        name: Name,
        description: "检查人物、场景、道具的最新设定资源和已有图片候选。默认在生成 shot 首帧前调用；必要对象没有图片时首次需提示导演。导演随后允许不补缺失项，只豁免已列出的缺失对象，仍须返回并尽可能使用其他已有候选；只有导演明确要求所有参考图都不用时，才跳过本检查并改为文字直出。",
        serializerOptions: context.JsonOptions);

    private static bool IsSetupAsset(Asset asset) =>
        asset.Type is "character" or "scene" or "prop"
        && (asset.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(asset.FileName) is ".md" or ".txt");
}