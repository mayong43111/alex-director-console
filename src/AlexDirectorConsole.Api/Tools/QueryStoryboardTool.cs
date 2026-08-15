using System.Text.Json;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class QueryStoryboardTool(AppDbContext dbContext) : IDirectorTool
{
    public string Name => "query_storyboard";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, int, int, bool, CancellationToken, Task<string>>)(async (
            scriptAssetId,
            sceneNumber,
            shotNumber,
            includeTakes,
            cancellationToken) =>
        {
            if (sceneNumber < 0 || shotNumber < 0)
                throw new ArgumentException("sceneNumber 和 shotNumber 必须为 0 或正整数；0 表示不过滤。");

            Guid? scriptResourceId = null;
            if (!string.IsNullOrWhiteSpace(scriptAssetId))
            {
                if (!Guid.TryParse(scriptAssetId.Trim(), out var parsedScriptAssetId))
                    throw new ArgumentException("scriptAssetId 必须为空或有效 GUID。", nameof(scriptAssetId));
                var script = await dbContext.Assets
                    .AsNoTracking()
                    .SingleOrDefaultAsync(asset => asset.ProjectId == context.ProjectId
                        && asset.Id == parsedScriptAssetId
                        && asset.Type == "script", cancellationToken)
                    ?? throw new ArgumentException("scriptAssetId 不是当前项目中的剧本资产。", nameof(scriptAssetId));
                scriptResourceId = script.ResourceId;
            }

            var definitionQuery = dbContext.ShotDefinitions
                .AsNoTracking()
                .Where(shot => shot.ProjectId == context.ProjectId);
            if (scriptResourceId is not null)
                definitionQuery = definitionQuery.Where(shot => shot.ScriptResourceId == scriptResourceId);
            if (sceneNumber > 0)
                definitionQuery = definitionQuery.Where(shot => shot.SceneNumber == sceneNumber);
            if (shotNumber > 0)
                definitionQuery = definitionQuery.Where(shot => shot.ShotNumber == shotNumber);
            var definitions = await definitionQuery
                .OrderBy(shot => shot.SceneNumber)
                .ThenBy(shot => shot.ShotNumber)
                .ToListAsync(cancellationToken);

            var allAssets = await dbContext.Assets
                .AsNoTracking()
                .Where(asset => asset.ProjectId == context.ProjectId)
                .ToListAsync(cancellationToken);
            var latestByResource = allAssets
                .GroupBy(asset => asset.ResourceId)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(asset => asset.Version).First());
            var structuredShotResourceIds = definitions.Select(shot => shot.ShotResourceId).ToHashSet();
            var links = structuredShotResourceIds.Count == 0
                ? []
                : await dbContext.ShotAssetLinks
                    .AsNoTracking()
                    .Where(link => link.ProjectId == context.ProjectId
                        && structuredShotResourceIds.Contains(link.ShotResourceId))
                    .ToListAsync(cancellationToken);
            links = links
                .OrderBy(link => link.Role, StringComparer.Ordinal)
                .ThenByDescending(link => link.CreatedAtUtc)
                .ToList();
            var assetsById = allAssets.ToDictionary(asset => asset.Id);

            var shots = definitions.Select(definition =>
            {
                latestByResource.TryGetValue(definition.ShotResourceId, out var shotAsset);
                latestByResource.TryGetValue(definition.ScriptResourceId, out var scriptAsset);
                var media = links
                    .Where(link => link.ShotResourceId == definition.ShotResourceId)
                    .Where(link => assetsById.ContainsKey(link.AssetId))
                    .Select(link => new { Link = link, Asset = assetsById[link.AssetId] })
                    .GroupBy(item => new { item.Link.Role, item.Asset.ResourceId })
                    .Select(group => group.OrderByDescending(item => item.Link.CreatedAtUtc).First())
                    .Select(item => new
                    {
                        role = item.Link.Role,
                        selectedAsset = AssetResponse.FromAsset(item.Asset),
                        takeResourceId = item.Asset.ResourceId,
                        takeCount = allAssets.Count(asset => asset.ResourceId == item.Asset.ResourceId),
                        takes = includeTakes
                            ? allAssets
                                .Where(asset => asset.ResourceId == item.Asset.ResourceId)
                                .OrderByDescending(asset => asset.Version)
                                .Select(AssetResponse.FromAsset)
                                .ToArray()
                            : []
                    })
                    .ToArray();
                return new
                {
                    shotCode = $"S{definition.SceneNumber:D2}-{definition.ShotNumber:D2}",
                    definition.SceneNumber,
                    definition.ShotNumber,
                    definition.DurationSeconds,
                    definition.ScriptResourceId,
                    scriptAsset = scriptAsset is null ? null : AssetResponse.FromAsset(scriptAsset),
                    definition.ShotResourceId,
                    shotAsset = shotAsset is null ? null : AssetResponse.FromAsset(shotAsset),
                    media
                };
            }).ToArray();
            var unstructuredShots = latestByResource.Values
                .Where(asset => asset.Type == "shot" && !structuredShotResourceIds.Contains(asset.ResourceId))
                .OrderBy(asset => asset.Name, StringComparer.Ordinal)
                .Select(AssetResponse.FromAsset)
                .ToArray();

            return JsonSerializer.Serialize(new
            {
                shotCount = shots.Length,
                totalDurationSeconds = shots.Sum(shot => shot.DurationSeconds),
                shots,
                unstructuredShotCount = unstructuredShots.Length,
                unstructuredShots
            }, context.JsonOptions);
        }),
        name: Name,
        description: "查询当前项目结构化分镜。scriptAssetId 可为空或传剧本版本 ID；sceneNumber、shotNumber 传 0 表示不过滤。返回稳定的 scene/shot 编号、时长、来源剧本、镜头文本版本、各角色当前绑定素材及素材 take 版本。includeTakes=true 时展开所有版本。也会报告未结构化旧镜头，供去重和清理。",
        serializerOptions: context.JsonOptions);
}