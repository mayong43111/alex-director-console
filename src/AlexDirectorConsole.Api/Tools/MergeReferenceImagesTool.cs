using System.Text.Json;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AlexDirectorConsole.Api.Tools;

public sealed class MergeReferenceImagesTool : IDirectorTool
{
    private const int CanvasSize = 1024;
    private const int Gap = 12;

    public string Name => "merge_reference_images";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, string, string, CancellationToken, Task<string>>)(async (
            referenceImageAssetIds,
            referenceDescriptions,
            resourceName,
            cancellationToken) =>
        {
            var ids = referenceImageAssetIds
                .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .Take(12)
                .ToArray();
            var descriptions = ParseDescriptions(referenceDescriptions);
            var name = resourceName.Trim();
            if (ids.Length < 2)
            {
                throw new ArgumentException("合并参考图至少需要两个有效图片资产 ID。", nameof(referenceImageAssetIds));
            }
            if (descriptions.Length != ids.Length)
            {
                throw new ArgumentException("referenceDescriptions 必须是与图片 ID 同序、同数量的 JSON 字符串数组。", nameof(referenceDescriptions));
            }
            if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
            {
                throw new ArgumentException("资源名称不能为空且不能超过 160 个字符。", nameof(resourceName));
            }

            var assets = await context.DbContext.Assets
                .AsNoTracking()
                .Where(asset => asset.ProjectId == context.ProjectId && ids.Contains(asset.Id))
                .ToListAsync(cancellationToken);
            if (assets.Count != ids.Length || assets.Any(asset => !asset.ContentType.StartsWith("image/")))
            {
                throw new ArgumentException("所有参考 ID 都必须是当前项目中的真实图片资产。", nameof(referenceImageAssetIds));
            }

            var columns = (int)Math.Ceiling(Math.Sqrt(ids.Length));
            var rows = (int)Math.Ceiling(ids.Length / (double)columns);
            var cellWidth = (CanvasSize - Gap * (columns + 1)) / columns;
            var cellHeight = (CanvasSize - Gap * (rows + 1)) / rows;
            using var canvas = new Image<Rgba32>(CanvasSize, CanvasSize, Color.White);
            var layout = new List<object>(ids.Length);

            for (var index = 0; index < ids.Length; index++)
            {
                var asset = assets.Single(item => item.Id == ids[index]);
                await using var source = await context.BlobStorage.OpenReadAsync(asset.BlobKey, cancellationToken)
                    ?? throw new InvalidOperationException($"参考图 Blob 不存在：{asset.Name}");
                using var image = await Image.LoadAsync<Rgba32>(source, cancellationToken);
                var scale = Math.Min(cellWidth / (double)image.Width, cellHeight / (double)image.Height);
                var width = Math.Max(1, (int)Math.Round(image.Width * scale));
                var height = Math.Max(1, (int)Math.Round(image.Height * scale));
                using var resized = image.Clone(operation => operation.Resize(width, height));
                var column = index % columns;
                var row = index / columns;
                var x = Gap + column * (cellWidth + Gap) + (cellWidth - width) / 2;
                var y = Gap + row * (cellHeight + Gap) + (cellHeight - height) / 2;
                canvas.Mutate(operation => operation.DrawImage(resized, new Point(x, y), 1f));
                layout.Add(new
                {
                    index = index + 1,
                    asset = AssetResponse.FromAsset(asset),
                    description = descriptions[index],
                    region = new { x, y, width, height }
                });
            }

            await using var output = new MemoryStream();
            await canvas.SaveAsPngAsync(output, cancellationToken);
            var mergedImage = new GeneratedImage(
                output.ToArray(),
                "image/png",
                ".png",
                "deterministic-contact-sheet",
                "source-preserving",
                null);
            var mergedAsset = await ImageAssetWriter.SaveAsync(context, name, mergedImage, cancellationToken);
            var versionCount = await context.DbContext.Assets.CountAsync(
                asset => asset.ResourceId == mergedAsset.ResourceId,
                cancellationToken);
            context.RevisedAssets.Add(mergedAsset);

            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "references.merged",
                message = $"已将 {ids.Length} 张参考图无重绘合并为：{mergedAsset.Name}",
                data = new { asset = AssetResponse.FromAsset(mergedAsset, versionCount), layout }
            }, cancellationToken);
            return JsonSerializer.Serialize(new
            {
                asset = AssetResponse.FromAsset(mergedAsset, versionCount),
                layout
            }, context.JsonOptions);
        }),
        name: Name,
        description: "把 2 至 12 张当前项目图片无重绘地拼成一张 1024x1024 参考合并图，适合将同一道具的多角度/多状态图片合并，或人物过多时先合并再用于镜头生成。referenceImageAssetIds 用逗号分隔；referenceDescriptions 必须是同序 JSON 字符串数组，逐项说明每张图是什么及要继承的内容；返回合并资产 ID 和每张源图的区域坐标。",
        serializerOptions: context.JsonOptions);

    private static string[] ParseDescriptions(string value)
    {
        try
        {
            var descriptions = JsonSerializer.Deserialize<string[]>(value) ?? [];
            if (descriptions.Any(description => string.IsNullOrWhiteSpace(description) || description.Length > 500))
            {
                throw new ArgumentException("每条参考图说明必须非空且不能超过 500 个字符。", nameof(value));
            }
            return descriptions.Select(description => description.Trim()).ToArray();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("referenceDescriptions 必须是 JSON 字符串数组。", nameof(value), exception);
        }
    }
}