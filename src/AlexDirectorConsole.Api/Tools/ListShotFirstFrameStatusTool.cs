using System.Text.Json;
using AlexDirectorConsole.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class ListShotFirstFrameStatusTool(AppDbContext dbContext) : IDirectorTool
{
    public string Name => "list_shot_first_frame_status";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, CancellationToken, Task<string>>)(async (
            nameContains,
            cancellationToken) =>
        {
            var normalizedName = nameContains.Trim();
            var shotVersions = await dbContext.Assets
                .AsNoTracking()
                .Where(asset => asset.ProjectId == context.ProjectId && asset.Type == "shot")
                .ToListAsync(cancellationToken);
            var shots = shotVersions
                .GroupBy(asset => asset.ResourceId)
                .Select(group => group
                    .OrderByDescending(asset => asset.Version)
                    .ThenByDescending(asset => asset.CreatedAtUtc)
                    .First())
                .Where(asset => string.IsNullOrWhiteSpace(normalizedName)
                    || asset.Name.Contains(normalizedName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(asset => asset.Name, StringComparer.Ordinal)
                .Take(100)
                .ToArray();

            var shotResourceIds = shots.Select(shot => shot.ResourceId).ToArray();
            var links = await dbContext.ShotAssetLinks
                .AsNoTracking()
                .Where(link => link.ProjectId == context.ProjectId
                    && shotResourceIds.Contains(link.ShotResourceId)
                    && link.Role == "first-frame")
                .ToListAsync(cancellationToken);
            var linkedAssetIds = links.Select(link => link.AssetId).Distinct().ToArray();
            var validImageIds = (await dbContext.Assets
                .AsNoTracking()
                .Where(asset => asset.ProjectId == context.ProjectId
                    && linkedAssetIds.Contains(asset.Id)
                    && asset.Type == "media"
                    && asset.ContentType.StartsWith("image/"))
                .Select(asset => asset.Id)
                .ToArrayAsync(cancellationToken))
                .ToHashSet();
            var completedShotIds = links
                .Where(link => validImageIds.Contains(link.AssetId))
                .Select(link => link.ShotResourceId)
                .ToHashSet();
            var statuses = shots.Select(shot => new
            {
                shotAssetId = shot.Id,
                shotResourceId = shot.ResourceId,
                shotName = shot.Name,
                hasFirstFrame = completedShotIds.Contains(shot.ResourceId)
            }).ToArray();
            var missing = statuses.Where(status => !status.hasFirstFrame).ToArray();

            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "shot-first-frame.status-listed",
                message = $"已核验 {statuses.Length} 个镜头：{statuses.Length - missing.Length} 个已有首帧，{missing.Length} 个缺失"
            }, cancellationToken);

            return JsonSerializer.Serialize(new
            {
                total = statuses.Length,
                completed = statuses.Length - missing.Length,
                missing = missing.Length,
                missingShots = missing,
                shots = statuses
            }, context.JsonOptions);
        }),
        name: Name,
        description: "查询当前项目镜头是否已有有效首帧绑定。状态来自持久化的 shot 资源、first-frame 绑定和仍存在的图片素材，不得用最近生成记录代替。nameContains 可按镜头资源名称筛选，查询全部时传空字符串。返回总数、完成数、缺失数、缺失镜头及全部镜头状态。",
        serializerOptions: context.JsonOptions);
}