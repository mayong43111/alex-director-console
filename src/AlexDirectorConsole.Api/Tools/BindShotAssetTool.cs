using System.Text.Json;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class BindShotAssetTool : IDirectorTool
{
    private static readonly HashSet<string> ExclusiveRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "first-frame",
        "last-frame",
        "video"
    };

    private static readonly HashSet<string> ValidRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "first-frame",
        "last-frame",
        "reference",
        "video",
        "other"
    };

    public string Name => "bind_shot_asset";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, string, string, CancellationToken, Task<string>>)(async (
            assetId,
            role,
            shotAssetId,
            cancellationToken) =>
        {
            if (!Guid.TryParse(assetId.Trim(), out var parsedAssetId))
            {
                throw new ArgumentException("assetId 必须是有效的素材资产 ID。", nameof(assetId));
            }

            var normalizedRole = role.Trim().ToLowerInvariant();
            if (!ValidRoles.Contains(normalizedRole))
            {
                throw new ArgumentException(
                    "role 只能是 first-frame、last-frame、reference、video 或 other。",
                    nameof(role));
            }

            Asset? shot;
            if (string.IsNullOrWhiteSpace(shotAssetId))
            {
                shot = context.CurrentAsset?.Type == "shot" ? context.CurrentAsset : null;
            }
            else
            {
                if (!Guid.TryParse(shotAssetId.Trim(), out var parsedShotAssetId))
                {
                    throw new ArgumentException("shotAssetId 必须是有效的镜头资产 ID。", nameof(shotAssetId));
                }
                shot = await context.DbContext.Assets
                    .AsNoTracking()
                    .SingleOrDefaultAsync(item =>
                        item.Id == parsedShotAssetId
                        && item.ProjectId == context.ProjectId
                        && item.Type == "shot",
                        cancellationToken);
            }
            if (shot is null)
            {
                throw new InvalidOperationException("找不到当前项目中的目标镜头；请传入 shotAssetId 或在界面选择镜头。");
            }
            var asset = await context.DbContext.Assets
                .AsNoTracking()
                .SingleOrDefaultAsync(item =>
                    item.Id == parsedAssetId && item.ProjectId == context.ProjectId,
                    cancellationToken)
                ?? throw new InvalidOperationException("找不到当前项目中的目标素材。");
            if (asset.Type != "media" || (!asset.ContentType.StartsWith("image/")
                && !asset.ContentType.StartsWith("video/")
                && !asset.ContentType.StartsWith("audio/")))
            {
                throw new ArgumentException("只能把当前项目中的图片、视频或音频素材绑定到镜头。", nameof(assetId));
            }

            var existing = await context.DbContext.ShotAssetLinks
                .SingleOrDefaultAsync(link =>
                    link.ShotResourceId == shot.ResourceId
                    && link.AssetId == asset.Id
                    && link.Role == normalizedRole,
                    cancellationToken);
            if (existing is null)
            {
                if (ExclusiveRoles.Contains(normalizedRole))
                {
                    var replacedLinks = await context.DbContext.ShotAssetLinks
                        .Where(link => link.ShotResourceId == shot.ResourceId
                            && link.Role == normalizedRole)
                        .ToListAsync(cancellationToken);
                    context.DbContext.ShotAssetLinks.RemoveRange(replacedLinks);
                }
                existing = new ShotAssetLink
                {
                    Id = Guid.NewGuid(),
                    ProjectId = context.ProjectId,
                    ShotResourceId = shot.ResourceId,
                    AssetId = asset.Id,
                    Role = normalizedRole,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                context.DbContext.ShotAssetLinks.Add(existing);
                await context.DbContext.SaveChangesAsync(cancellationToken);
            }

            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "tool.completed",
                message = $"已将 {asset.Name} 绑定到镜头 {shot.Name}（{normalizedRole}）"
            }, cancellationToken);

            return JsonSerializer.Serialize(new
            {
                link = new ShotAssetLinkResponse(
                    existing.Id,
                    existing.Role,
                    existing.CreatedAtUtc,
                    AssetResponse.FromAsset(asset)),
                shot = AssetResponse.FromAsset(shot)
            }, context.JsonOptions);
        }),
        name: Name,
        description: "把已生成或已存在的媒体素材绑定到目标 shot。assetId 使用图片/视频/音频素材资产 ID；role 使用 first-frame（首帧）、last-frame（尾帧）、reference（参考素材）、video（镜头视频）或 other（其他）。first-frame、last-frame、video 每个镜头只能各绑定一个，新绑定会替换旧绑定；reference 和 other 可绑定多个不同逻辑资源。shotAssetId 使用 list_project_resources 返回的 shot 资产 ID，传空字符串时使用界面当前选中的 shot。批量处理时必须为每个目标镜头显式传 shotAssetId。生成镜头相关素材后必须调用，不能只在回复中声称关联。",
        serializerOptions: context.JsonOptions);
}