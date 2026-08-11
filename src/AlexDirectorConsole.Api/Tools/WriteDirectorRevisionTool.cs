using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class WriteDirectorRevisionTool : IDirectorTool
{
    public string Name => "write_director_revision";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, string, string, string, CancellationToken, Task<string>>)(async (
            resourceType,
            resourceName,
            markdownContent,
            sourceAssetIds,
            cancellationToken) =>
        {
            await context.ResourceLock.WaitAsync(cancellationToken);
            try
            {
                var type = resourceType.Trim().ToLowerInvariant();
                var labels = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["character"] = "人物设定稿",
                    ["scene"] = "场景设定稿",
                    ["prop"] = "道具设定稿"
                };
                if (!labels.TryGetValue(type, out var label))
                {
                    throw new ArgumentException("导演修订只支持 character、scene 或 prop。", nameof(resourceType));
                }

                var name = resourceName.Trim();
                var revision = markdownContent.Trim();
                if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
                {
                    throw new ArgumentException("resourceName 不能为空且不能超过 160 个字符。", nameof(resourceName));
                }
                if (string.IsNullOrWhiteSpace(revision) || revision.Length > 200000)
                {
                    throw new ArgumentException("修订正文必须是完整 Markdown 且不能超过 200,000 个字符。", nameof(markdownContent));
                }

                var subject = GetResourceSubject(name);
                var resourceVersions = (await context.DbContext.Assets
                        .Where(asset => asset.ProjectId == context.ProjectId && asset.Type == type)
                        .ToListAsync(cancellationToken))
                    .Where(asset => GetResourceSubject(asset.Name).Equals(subject, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(asset => asset.Version)
                    .ToList();
                var latestVersion = resourceVersions.LastOrDefault();

                var sourceIds = SplitAssetIds(sourceAssetIds);
                var sourceAssets = await context.DbContext.Assets
                    .AsNoTracking()
                    .Where(asset => asset.ProjectId == context.ProjectId && sourceIds.Contains(asset.Id))
                    .ToListAsync(cancellationToken);
                if (sourceIds.Count == 0 || sourceAssets.Count != sourceIds.Count)
                {
                    throw new ArgumentException("sourceAssetIds 必须全部引用当前项目中的真实资源。", nameof(sourceAssetIds));
                }

                var sourceSubjects = sourceAssets
                    .Select(asset => GetResourceSubject(asset.Name))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var rootAssets = (await context.DbContext.Assets
                        .AsNoTracking()
                        .Where(asset => asset.ProjectId == context.ProjectId)
                        .ToListAsync(cancellationToken))
                    .Where(asset =>
                        !asset.Name.Contains("导演修订", StringComparison.Ordinal)
                        && sourceSubjects.Contains(GetResourceSubject(asset.Name)));
                var provenanceAssets = sourceAssets.Concat(rootAssets).DistinctBy(asset => asset.Id).ToList();
                var provenance = string.Join(
                    Environment.NewLine,
                    provenanceAssets
                        .OrderBy(asset => asset.Type, StringComparer.Ordinal)
                        .ThenBy(asset => asset.Name, StringComparer.Ordinal)
                        .Select(asset => $"- `{asset.Id}` · {asset.Name}"));
                var provenanceHeading = revision.Contains("## 修订来源", StringComparison.Ordinal)
                    ? string.Empty
                    : $"## 修订来源{Environment.NewLine}";
                var persistedRevision = $"{revision}{Environment.NewLine}{Environment.NewLine}{provenanceHeading}{provenance}";

                await context.WriteEventAsync(new
                {
                    type = "process",
                    stage = "tool.started",
                    message = $"Agent 正在创建导演修订：{name}"
                }, cancellationToken);
                var assetId = CreateRevisionId(context.ProjectId, context.Content, type, name);
                var revisionAsset = await context.DbContext.Assets
                    .SingleOrDefaultAsync(asset => asset.Id == assetId, cancellationToken);
                if (revisionAsset is null)
                {
                    var version = (latestVersion?.Version ?? 0) + 1;
                    var bytes = Encoding.UTF8.GetBytes(persistedRevision + Environment.NewLine);
                    var now = DateTimeOffset.UtcNow;
                    revisionAsset = new Asset
                    {
                        Id = assetId,
                        ResourceId = latestVersion?.ResourceId ?? assetId,
                        Version = version,
                        ProjectId = context.ProjectId,
                        Type = type,
                        Name = latestVersion?.Name ?? $"{subject} · {label}",
                        BlobKey = $"{context.ProjectId:N}/{type}/{assetId:N}.md",
                        FileName = $"{SanitizeFileName(subject)}-{label}-v{version}.md",
                        ContentType = "text/markdown; charset=utf-8",
                        SizeBytes = bytes.LongLength,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    };
                    await using var stream = new MemoryStream(bytes, writable: false);
                    await context.BlobStorage.SaveAsync(revisionAsset.BlobKey, stream, cancellationToken);
                    try
                    {
                        context.DbContext.Assets.Add(revisionAsset);
                        await context.DbContext.SaveChangesAsync(cancellationToken);
                    }
                    catch
                    {
                        await context.BlobStorage.DeleteAsync(revisionAsset.BlobKey, CancellationToken.None);
                        throw;
                    }
                }

                if (context.RevisedAssets.All(asset => asset.Id != revisionAsset.Id))
                {
                    context.RevisedAssets.Add(revisionAsset);
                }
                var versionCount = resourceVersions.Count
                    + (resourceVersions.Any(asset => asset.Id == revisionAsset.Id) ? 0 : 1);
                await context.WriteEventAsync(new
                {
                    type = "process",
                    stage = "tool.completed",
                    message = $"Agent 已创建资源版本：{revisionAsset.Name} v{revisionAsset.Version}",
                    data = new { asset = AssetResponse.FromAsset(revisionAsset, versionCount) }
                }, cancellationToken);
                return JsonSerializer.Serialize(AssetResponse.FromAsset(revisionAsset, versionCount), context.JsonOptions);
            }
            catch (Exception exception)
            {
                await context.WriteEventAsync(new
                {
                    type = "process",
                    stage = "tool.failed",
                    message = $"创建导演修订失败：{exception.GetType().Name}: {exception.Message}"
                }, CancellationToken.None);
                throw;
            }
            finally
            {
                context.ResourceLock.Release();
            }
        }),
        name: Name,
        description: "在导演明确确认跨资源纠正后，创建不可变的修订资源。每个受影响对象单独调用；sourceAssetIds 用逗号分隔并引用读取工具返回的真实 ID。不得覆盖原稿。",
        serializerOptions: context.JsonOptions);

    private static IReadOnlyList<Guid> SplitAssetIds(string value) =>
        value.Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => Guid.TryParse(item, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(20)
            .ToList();

    private static string GetResourceSubject(string value) =>
        value.Split('·', StringSplitOptions.TrimEntries)[0];

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "未命名" : sanitized.Trim();
    }

    private static Guid CreateRevisionId(Guid projectId, string instruction, string resourceType, string resourceName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"v3:{projectId:N}:{instruction.Trim()}:{resourceType}:{resourceName.Trim().ToUpperInvariant()}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
