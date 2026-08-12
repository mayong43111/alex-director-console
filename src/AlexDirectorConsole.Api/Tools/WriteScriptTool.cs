using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlexDirectorConsole.Api.Application.Assets;
using AlexDirectorConsole.Api.Contracts;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class WriteScriptTool(
    IAssetReader assetReader,
    IAssetWriter assetWriter) : IDirectorTool
{
    public string Name => "write_script";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, string, CancellationToken, Task<string>>)(async (
            scriptName,
            markdownContent,
            cancellationToken) =>
        {
            var name = scriptName.Trim();
            var content = markdownContent.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
                throw new ArgumentException("剧本名称不能为空且不能超过 160 个字符。", nameof(scriptName));
            if (string.IsNullOrWhiteSpace(content) || content.Length > 300000)
                throw new ArgumentException("剧本正文必须是完整 Markdown 且不能超过 300,000 个字符。", nameof(markdownContent));

            await context.ResourceLock.WaitAsync(cancellationToken);
            try
            {
                await context.WriteEventAsync(new
                {
                    type = "process",
                    stage = "tool.started",
                    message = $"Agent 正在保存剧本：{name}"
                }, cancellationToken);

                var assetId = CreateScriptId(context.ProjectId, context.Content, name);
                var scriptAsset = await assetWriter.WriteVersionAsync(
                    new AssetWriteRequest(
                        context.ProjectId,
                        "script",
                        name,
                        name,
                        ".md",
                        "text/markdown; charset=utf-8",
                        Encoding.UTF8.GetBytes(content + Environment.NewLine),
                        AssetVersionTarget.ExactName,
                        AssetId: assetId,
                        FileNameFallback: "script"),
                    cancellationToken);
                var versionCount = await assetReader.CountVersionsAsync(
                    context.ProjectId,
                    scriptAsset.ResourceId,
                    cancellationToken);
                if (context.RevisedAssets.All(asset => asset.Id != scriptAsset.Id))
                    context.RevisedAssets.Add(scriptAsset);
                context.UpdatedAsset = scriptAsset;

                await context.WriteEventAsync(new
                {
                    type = "process",
                    stage = "tool.completed",
                    message = $"Agent 已保存剧本：{scriptAsset.Name} v{scriptAsset.Version}",
                    data = new { asset = AssetResponse.FromAsset(scriptAsset, versionCount) }
                }, cancellationToken);
                return JsonSerializer.Serialize(
                    AssetResponse.FromAsset(scriptAsset, versionCount),
                    context.JsonOptions);
            }
            finally
            {
                context.ResourceLock.Release();
            }
        }),
        name: Name,
        description: "创建新的完整剧本 Markdown 资源。用于从零创作剧本；若同名剧本已存在则创建该逻辑资源的新版本。改写明确指定的已有剧本时优先调用 update_project_resource。只在工具返回 Asset 后才算写作完成。",
        serializerOptions: context.JsonOptions);

    private static Guid CreateScriptId(Guid projectId, string instruction, string scriptName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"script-v1:{projectId:N}:{instruction.Trim()}:{scriptName.Trim().ToUpperInvariant()}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}