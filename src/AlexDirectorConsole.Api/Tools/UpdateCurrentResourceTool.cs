using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlexDirectorConsole.Api.Application.Assets;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Models;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class UpdateCurrentResourceTool(
    IAssetReader assetReader,
    IAssetWriter assetWriter) : IDirectorTool
{
    public string Name => "update_project_resource";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, string, CancellationToken, Task<string>>)(async (
            assetId,
            markdownContent,
            cancellationToken) =>
        {
            if (!Guid.TryParse(assetId, out var parsedAssetId))
            {
                throw new ArgumentException("assetId 必须是有效 UUID。", nameof(assetId));
            }
            var targetAsset = await assetReader.GetAsync(context.ProjectId, parsedAssetId, cancellationToken)
                ?? throw new InvalidOperationException("找不到当前项目中的目标资源。");
            if (!IsTextAsset(targetAsset))
                throw new InvalidOperationException("目标资源必须是可读取的文本资源。");
            var revisedContent = markdownContent.Trim();
            if (string.IsNullOrWhiteSpace(revisedContent) || revisedContent.Length > 200000)
            {
                throw new ArgumentException("完整 Markdown 正文不能为空且不能超过 200,000 个字符。", nameof(markdownContent));
            }

            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "tool.started",
                message = $"Agent 正在创建资源新版本：{targetAsset.Name}"
            }, cancellationToken);
            var revisionAssetId = CreateRevisionId(context.ProjectId, context.Content, targetAsset.Type, targetAsset.Name);
            var extension = Path.GetExtension(targetAsset.FileName);
            var newVersion = await assetWriter.WriteVersionAsync(
                new AssetWriteRequest(
                    context.ProjectId,
                    targetAsset.Type,
                    targetAsset.Name,
                    Path.GetFileNameWithoutExtension(targetAsset.FileName),
                    extension,
                    targetAsset.ContentType,
                    Encoding.UTF8.GetBytes(revisedContent + Environment.NewLine),
                    AssetVersionTarget.ExistingResource,
                    targetAsset.ResourceId,
                    revisionAssetId),
                cancellationToken);

            var versionCount = await assetReader.CountVersionsAsync(
                context.ProjectId,
                newVersion.ResourceId,
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
        description: "将 Agent 修改后的完整 Markdown 正文保存为指定项目文本资源的新版本并保留旧版本。assetId 必须来自当前资源，或先通过 list_project_resources 和 read_project_resource_contents 自主发现并读取。导演要求修改、补充、删减或重写剧本等文本资源时必须实际调用本工具，不能只在聊天中输出正文。markdownContent 必须是完整正文，不是补丁。",
        serializerOptions: context.JsonOptions);

    private static bool IsTextAsset(Asset asset) =>
        asset.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(asset.FileName).Equals(".md", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(asset.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase);

    private static Guid CreateRevisionId(Guid projectId, string instruction, string resourceType, string resourceName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"v3:{projectId:N}:{instruction.Trim()}:{resourceType}:{resourceName.Trim().ToUpperInvariant()}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
