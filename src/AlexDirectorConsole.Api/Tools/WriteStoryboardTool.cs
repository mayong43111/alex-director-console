using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed partial class WriteStoryboardTool : IDirectorTool
{
    public string Name => "write_storyboard";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, string, CancellationToken, Task<string>>)(async (
            storyboardName,
            markdownContent,
            cancellationToken) =>
        {
            await context.ResourceLock.WaitAsync(cancellationToken);
            try
            {
                var name = storyboardName.Trim();
                var content = markdownContent.Trim();
                if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
                {
                    throw new ArgumentException("分镜名称不能为空且不能超过 160 个字符。", nameof(storyboardName));
                }
                if (string.IsNullOrWhiteSpace(content) || content.Length > 300000)
                {
                    throw new ArgumentException("分镜 Markdown 不能为空且不能超过 300,000 个字符。", nameof(markdownContent));
                }

                var shots = ParseShots(content);
                if (shots.Count == 0)
                {
                    throw new ArgumentException("分镜正文必须包含以“镜号”为首列的逐场分镜表。", nameof(markdownContent));
                }
                var duplicateShotIds = shots
                    .GroupBy(shot => shot.Id, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .ToArray();
                if (duplicateShotIds.Length > 0)
                {
                    throw new ArgumentException(
                        $"分镜正文包含重复镜号：{string.Join("、", duplicateShotIds)}。",
                        nameof(markdownContent));
                }

                var subject = GetResourceSubject(name);
                var existingShots = await context.DbContext.Assets
                    .Where(asset => asset.ProjectId == context.ProjectId && asset.Type == "shot")
                    .ToListAsync(cancellationToken);
                var visualRules = ExtractSection(content, "视觉总则");
                var persistedShots = new List<Asset>(shots.Count);
                var newShots = new List<(Asset Asset, byte[] Bytes)>();

                foreach (var shot in shots)
                {
                    var shotName = $"{subject} · {shot.Id}";
                    var versions = existingShots
                        .Where(asset => asset.Name.Equals(shotName, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(asset => asset.Version)
                        .ToList();
                    var latestVersion = versions.LastOrDefault();
                    var version = (latestVersion?.Version ?? 0) + 1;
                    var assetId = CreateAssetId(context.ProjectId, context.Content, shot.Id);
                    var existing = existingShots.SingleOrDefault(asset => asset.Id == assetId);
                    if (existing is not null)
                    {
                        persistedShots.Add(existing);
                        continue;
                    }

                    var bytes = Encoding.UTF8.GetBytes(BuildShotMarkdown(
                        subject,
                        shot,
                        visualRules));
                    var now = DateTimeOffset.UtcNow;
                    var asset = new Asset
                    {
                        Id = assetId,
                        ResourceId = latestVersion?.ResourceId ?? assetId,
                        Version = version,
                        ProjectId = context.ProjectId,
                        Type = "shot",
                        Name = shotName,
                        BlobKey = $"{context.ProjectId:N}/shot/{assetId:N}.md",
                        FileName = $"{SanitizeFileName(subject)}-{shot.Id}-v{version}.md",
                        ContentType = "text/markdown; charset=utf-8",
                        SizeBytes = bytes.LongLength,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    };
                    newShots.Add((asset, bytes));
                    persistedShots.Add(asset);
                }

                foreach (var (asset, bytes) in newShots)
                {
                    await using var stream = new MemoryStream(bytes, writable: false);
                    await context.BlobStorage.SaveAsync(asset.BlobKey, stream, cancellationToken);
                }
                try
                {
                    context.DbContext.Assets.AddRange(newShots.Select(item => item.Asset));
                    await context.DbContext.SaveChangesAsync(cancellationToken);
                }
                catch
                {
                    foreach (var (asset, _) in newShots)
                    {
                        await context.BlobStorage.DeleteAsync(asset.BlobKey, CancellationToken.None);
                    }
                    throw;
                }

                var legacyStoryboards = existingShots
                    .Where(asset => !ShotNamePattern().IsMatch(asset.Name)
                        && GetResourceSubject(asset.Name).Equals(subject, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (legacyStoryboards.Count > 0)
                {
                    context.DbContext.Assets.RemoveRange(legacyStoryboards);
                    await context.DbContext.SaveChangesAsync(cancellationToken);
                    foreach (var legacy in legacyStoryboards)
                    {
                        await context.BlobStorage.DeleteAsync(legacy.BlobKey, cancellationToken);
                    }
                }

                foreach (var asset in persistedShots)
                {
                    if (context.RevisedAssets.All(item => item.Id != asset.Id))
                    {
                        context.RevisedAssets.Add(asset);
                    }
                }
                context.UpdatedAsset = persistedShots[0];
                await context.WriteEventAsync(new
                {
                    type = "process",
                    stage = "tool.completed",
                    message = $"Agent 已创建 {persistedShots.Count} 个独立镜头资源：{subject}",
                    data = new
                    {
                        assets = persistedShots.Select(AssetResponse.FromAsset)
                    }
                }, cancellationToken);
                return JsonSerializer.Serialize(
                    new
                    {
                        assets = persistedShots.Select(AssetResponse.FromAsset),
                        shotCount = persistedShots.Count
                    },
                    context.JsonOptions);
            }
            catch (Exception exception)
            {
                await context.WriteEventAsync(new
                {
                    type = "process",
                    stage = "tool.failed",
                    message = $"创建独立镜头资源失败：{exception.GetType().Name}: {exception.Message}"
                }, CancellationToken.None);
                throw;
            }
            finally
            {
                context.ResourceLock.Release();
            }
        }),
        name: Name,
        description: "将完整 Markdown 分镜表按镜号拆分，每个镜号保存为独立且可版本化的 shot 资源。shot 仅保存镜头设计和视觉总则，不写入人物、场景、道具或来源资源快照；生成镜头图片前应动态查找项目最新资源。",
        serializerOptions: context.JsonOptions);

    private static IReadOnlyList<StoryboardShot> ParseShots(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var shots = new List<StoryboardShot>();
        var currentHeading = string.Empty;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.StartsWith('#'))
            {
                currentHeading = line.TrimStart('#').Trim();
            }
            var headers = ParseTableRow(line);
            if (headers.Count == 0 || !headers[0].Equals("镜号", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (index + 1 >= lines.Length || !IsTableSeparator(lines[index + 1]))
            {
                continue;
            }

            index += 2;
            while (index < lines.Length)
            {
                var values = ParseTableRow(lines[index]);
                if (values.Count == 0)
                {
                    index--;
                    break;
                }
                while (values.Count < headers.Count)
                {
                    values.Add(string.Empty);
                }
                var shotId = values[0].Trim().ToUpperInvariant();
                if (!ShotIdPattern().IsMatch(shotId))
                {
                    throw new ArgumentException($"无效镜号“{values[0]}”，必须使用 S场次号-镜头号 格式。", nameof(content));
                }
                shots.Add(new StoryboardShot(shotId, currentHeading, headers, values));
                index++;
            }
        }
        return shots;
    }

    private static List<string> ParseTableRow(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith('|') || !trimmed.EndsWith('|'))
        {
            return [];
        }

        var cells = new List<string>();
        var cell = new StringBuilder();
        for (var index = 1; index < trimmed.Length - 1; index++)
        {
            var character = trimmed[index];
            if (character == '\\' && index + 1 < trimmed.Length - 1 && trimmed[index + 1] == '|')
            {
                cell.Append('|');
                index++;
            }
            else if (character == '|')
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else
            {
                cell.Append(character);
            }
        }
        cells.Add(cell.ToString().Trim());
        return cells;
    }

    private static bool IsTableSeparator(string line)
    {
        var cells = ParseTableRow(line);
        return cells.Count > 0
            && cells.All(cell => cell.Trim(':', ' ').Length >= 3
                && cell.Trim(':', ' ').All(character => character == '-'));
    }

    private static string ExtractSection(string content, string heading)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var collected = new List<string>();
        var collecting = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (collecting)
                {
                    break;
                }
                collecting = line[3..].Trim().Equals(heading, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (collecting)
            {
                collected.Add(line);
            }
        }
        return string.Join(Environment.NewLine, collected).Trim();
    }

    private static string BuildShotMarkdown(
        string subject,
        StoryboardShot shot,
        string visualRules)
    {
        var details = shot.Headers
            .Skip(1)
            .Zip(shot.Values.Skip(1), (header, value) => $"- **{header}**：{value}");
        var builder = new StringBuilder()
            .AppendLine($"# {shot.Id} · {subject}")
            .AppendLine()
            .AppendLine($"- **镜号**：{shot.Id}")
            .AppendLine($"- **所属场次**：{shot.SceneHeading}")
            .AppendLine()
            .AppendLine("## 镜头设计")
            .AppendLine()
            .AppendLine(string.Join(Environment.NewLine, details));
        if (!string.IsNullOrWhiteSpace(visualRules))
        {
            builder.AppendLine().AppendLine("## 视觉总则").AppendLine().AppendLine(visualRules);
        }
        return builder.ToString();
    }

    private static string GetResourceSubject(string value)
    {
        var subject = value.Split('·', StringSplitOptions.TrimEntries)[0];
        return subject.EndsWith("分镜稿", StringComparison.Ordinal)
            ? subject[..^3].Trim()
            : subject;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "未命名" : sanitized.Trim();
    }

    private static Guid CreateAssetId(Guid projectId, string instruction, string shotId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"storyboard-shot-v1:{projectId:N}:{instruction.Trim()}:{shotId.ToUpperInvariant()}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    [GeneratedRegex("^S\\d{2,}-\\d{2,}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShotIdPattern();

    [GeneratedRegex("·\\s*S\\d{2,}-\\d{2,}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShotNamePattern();

    private sealed record StoryboardShot(
        string Id,
        string SceneHeading,
        IReadOnlyList<string> Headers,
        IReadOnlyList<string> Values);
}