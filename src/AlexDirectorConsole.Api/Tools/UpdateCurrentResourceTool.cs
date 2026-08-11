using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class UpdateCurrentResourceTool : IDirectorTool
{
    public string Name => "update_current_resource";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, CancellationToken, Task<string>>)(async (markdownContent, cancellationToken) =>
        {
            var currentAsset = context.CurrentAsset;
            if (currentAsset is null || context.CurrentAssetContent is null)
            {
                throw new InvalidOperationException("update_current_resource 需要界面当前资源为可读取的文本资源。");
            }
            var revisedContent = markdownContent.Trim();
            if (string.IsNullOrWhiteSpace(revisedContent) || revisedContent.Length > 200000)
            {
                throw new ArgumentException("完整 Markdown 正文不能为空且不能超过 200,000 个字符。", nameof(markdownContent));
            }

            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "tool.started",
                message = $"Agent 正在创建资源新版本：{currentAsset.Name}"
            }, cancellationToken);
            var assetId = CreateRevisionId(context.ProjectId, context.Content, currentAsset.Type, currentAsset.Name);
            var newVersion = await context.DbContext.Assets
                .SingleOrDefaultAsync(asset => asset.Id == assetId, cancellationToken);
            if (newVersion is null)
            {
                var version = await context.DbContext.Assets
                    .Where(asset => asset.ResourceId == currentAsset.ResourceId)
                    .MaxAsync(asset => asset.Version, cancellationToken) + 1;
                var extension = Path.GetExtension(currentAsset.FileName);
                var bytes = Encoding.UTF8.GetBytes(revisedContent + Environment.NewLine);
                var now = DateTimeOffset.UtcNow;
                newVersion = new Asset
                {
                    Id = assetId,
                    ResourceId = currentAsset.ResourceId,
                    Version = version,
                    ProjectId = context.ProjectId,
                    Type = currentAsset.Type,
                    Name = currentAsset.Name,
                    BlobKey = $"{context.ProjectId:N}/{currentAsset.Type}/{assetId:N}{extension}",
                    FileName = $"{Path.GetFileNameWithoutExtension(currentAsset.FileName)}-v{version}{extension}",
                    ContentType = currentAsset.ContentType,
                    SizeBytes = bytes.LongLength,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                await using var stream = new MemoryStream(bytes, writable: false);
                await context.BlobStorage.SaveAsync(newVersion.BlobKey, stream, cancellationToken);
                try
                {
                    context.DbContext.Assets.Add(newVersion);
                    await context.DbContext.SaveChangesAsync(cancellationToken);
                }
                catch
                {
                    await context.BlobStorage.DeleteAsync(newVersion.BlobKey, CancellationToken.None);
                    throw;
                }
            }

            var versionCount = await context.DbContext.Assets.CountAsync(
                asset => asset.ResourceId == newVersion.ResourceId,
                cancellationToken);
            context.UpdatedAsset = newVersion;
            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "tool.completed",
                message = $"Agent 已创建资源版本：{newVersion.Name} v{newVersion.Version}",
                data = new { asset = AssetResponse.FromAsset(newVersion, versionCount) }
            }, cancellationToken);
            return JsonSerializer.Serialize(AssetResponse.FromAsset(newVersion, versionCount), context.JsonOptions);
        }),
        name: Name,
        description: "将 Agent 修改后的完整 Markdown 正文保存为当前逻辑资源的新版本，保留旧版本。导演要求修改、补充、删减或重写当前资源时调用。参数必须是完整正文，不是补丁。",
        serializerOptions: context.JsonOptions);

    private static Guid CreateRevisionId(Guid projectId, string instruction, string resourceType, string resourceName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"v3:{projectId:N}:{instruction.Trim()}:{resourceType}:{resourceName.Trim().ToUpperInvariant()}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
