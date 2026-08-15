using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlexDirectorConsole.Api.Application.Assets;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Data;
using AlexDirectorConsole.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed partial class WriteStoryboardTool(
    IAssetReader assetReader,
    IAssetWriter assetWriter,
    AppDbContext dbContext) : IDirectorTool
{
    public string Name => "write_storyboard";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, string, string, int?, int?, bool, CancellationToken, Task<string>>)(async (
            storyboardName,
            markdownContent,
            scriptAssetId,
            targetMinimumSeconds,
            targetMaximumSeconds,
            replaceExistingShots,
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
                if (targetMinimumSeconds.HasValue != targetMaximumSeconds.HasValue)
                {
                    throw new ArgumentException("目标总时长的最小值和最大值必须同时提供，或同时留空表示不限制。");
                }
                if (targetMinimumSeconds is < 1 or > 3600
                    || targetMaximumSeconds is < 1 or > 3600
                    || targetMinimumSeconds > targetMaximumSeconds)
                {
                    throw new ArgumentException("目标总时长必须是 1 到 3600 秒之间的有效区间。");
                }
                if (!Guid.TryParse(scriptAssetId.Trim(), out var parsedScriptAssetId))
                {
                    throw new ArgumentException("scriptAssetId 必须是当前项目的有效剧本资产 ID。", nameof(scriptAssetId));
                }
                var requestedScriptAsset = await assetReader.GetAsync(
                    context.ProjectId,
                    parsedScriptAssetId,
                    cancellationToken);
                if (requestedScriptAsset?.Type != "script")
                {
                    throw new ArgumentException("scriptAssetId 必须指向当前项目中的剧本资产。", nameof(scriptAssetId));
                }
                var scriptAsset = (await assetReader.ListAsync(
                        context.ProjectId,
                        "script",
                        cancellationToken))
                    .Where(asset => asset.ResourceId == requestedScriptAsset.ResourceId)
                    .OrderByDescending(asset => asset.Version)
                    .FirstOrDefault()
                    ?? requestedScriptAsset;
                var scriptSceneNumbers = await ReadScriptSceneNumbersAsync(
                    context.ProjectId,
                    scriptAsset,
                    cancellationToken);

                var shots = ParseShots(content);
                if (shots.Count == 0)
                {
                    throw new ArgumentException("分镜正文必须包含以“镜号”为首列的逐场分镜表。", nameof(markdownContent));
                }
                var requiredHeaders = new[]
                {
                    "角色/主体",
                    "场景/时空",
                    "景别",
                    "机位/角度",
                    "画面与动作",
                    "台词/声音",
                    "镜头运动",
                    "预计时长",
                    "连续性/制作备注"
                };
                var incompleteShots = shots
                    .Select(shot => new
                    {
                        shot.Id,
                        Missing = requiredHeaders
                            .Where(requiredHeader =>
                            {
                                var index = shot.Headers
                                    .Select((header, headerIndex) => new { Header = header, Index = headerIndex })
                                    .Where(item => item.Header.Trim().Equals(
                                        requiredHeader,
                                        StringComparison.OrdinalIgnoreCase))
                                    .Select(item => item.Index)
                                    .DefaultIfEmpty(-1)
                                    .First();
                                return index < 0
                                    || index >= shot.Values.Count
                                    || string.IsNullOrWhiteSpace(shot.Values[index]);
                            })
                            .ToArray()
                    })
                    .Where(item => item.Missing.Length > 0)
                    .Select(item => $"{item.Id} 缺少 {string.Join("、", item.Missing)}")
                    .ToArray();
                if (incompleteShots.Length > 0)
                {
                    throw new ArgumentException(
                        $"每个镜头必须填写完整制作字段：{string.Join("；", incompleteShots)}。",
                        nameof(markdownContent));
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

                var shotDurations = shots.Select(GetDurationSeconds).ToArray();
                var invalidDurationShots = shots
                    .Zip(shotDurations)
                    .Where(item => item.Second is < 3 or > 60)
                    .Select(item => $"{item.First.Id}={item.Second:0.##}s")
                    .ToArray();
                if (invalidDurationShots.Length > 0)
                {
                    throw new ArgumentException(
                        $"每个镜头必须为 3 到 60 秒：{string.Join("、", invalidDurationShots)}。",
                        nameof(markdownContent));
                }

                var existingDefinitions = await dbContext.ShotDefinitions
                    .Where(shot => shot.ProjectId == context.ProjectId
                        && shot.ScriptResourceId == scriptAsset.ResourceId)
                    .ToListAsync(cancellationToken);
                var incomingKeys = shots
                    .Select(shot => ParseShotNumbers(shot.Id))
                    .ToHashSet();
                var incomingSceneNumbers = incomingKeys.Select(key => key.SceneNumber).ToHashSet();
                if (scriptSceneNumbers.Count > 0)
                {
                    var invalidSceneNumbers = incomingSceneNumbers.Except(scriptSceneNumbers).Order().ToArray();
                    if (invalidSceneNumbers.Length > 0)
                    {
                        throw new ArgumentException(
                            $"分镜包含来源剧本中不存在的场号：{string.Join("、", invalidSceneNumbers.Select(number => $"S{number:D2}"))}。",
                            nameof(markdownContent));
                    }
                    if (replaceExistingShots && !incomingSceneNumbers.SetEquals(scriptSceneNumbers))
                    {
                        var missingSceneNumbers = scriptSceneNumbers.Except(incomingSceneNumbers).Order().ToArray();
                        throw new ArgumentException(
                            $"完整分镜必须逐场对应来源剧本；缺少场次：{string.Join("、", missingSceneNumbers.Select(number => $"S{number:D2}"))}。",
                            nameof(markdownContent));
                    }
                }
                var totalDurationSeconds = shotDurations.Sum()
                    + (replaceExistingShots
                        ? 0
                        : existingDefinitions
                            .Where(shot => !incomingKeys.Contains((shot.SceneNumber, shot.ShotNumber)))
                            .Sum(shot => shot.DurationSeconds));
                if (targetMinimumSeconds.HasValue
                    && targetMaximumSeconds.HasValue
                    && (totalDurationSeconds < targetMinimumSeconds.Value
                        || totalDurationSeconds > targetMaximumSeconds.Value))
                {
                    throw new ArgumentException(
                        $"分镜预计总时长 {totalDurationSeconds:0.##} 秒，不在目标区间 {targetMinimumSeconds}–{targetMaximumSeconds} 秒内；本次未保存任何镜头。",
                        nameof(markdownContent));
                }

                var subject = GetResourceSubject(name);
                var existingShots = await assetReader.ListAsync(
                    context.ProjectId,
                    "shot",
                    cancellationToken);
                var visualRules = ExtractSection(content, "视觉总则");
                var writeRequests = shots.Select(shot =>
                {
                    var (sceneNumber, shotNumber) = ParseShotNumbers(shot.Id);
                    var existingDefinition = existingDefinitions.SingleOrDefault(item =>
                        item.SceneNumber == sceneNumber && item.ShotNumber == shotNumber);
                    return new AssetWriteRequest(
                        context.ProjectId,
                        "shot",
                        $"{subject} · {shot.Id}",
                        $"{subject}-{shot.Id}",
                        ".md",
                        "text/markdown; charset=utf-8",
                        Encoding.UTF8.GetBytes(BuildShotMarkdown(subject, shot, visualRules)),
                        existingDefinition is null
                            ? AssetVersionTarget.CaseInsensitiveName
                            : AssetVersionTarget.ExistingResource,
                        ResourceId: existingDefinition?.ShotResourceId,
                        FileNameFallback: "未命名");
                }).ToArray();
                var persistedShots = await assetWriter.WriteVersionsAsync(
                    writeRequests,
                    cancellationToken);

                var now = DateTimeOffset.UtcNow;
                foreach (var (shot, persistedShot, duration) in shots.Zip(persistedShots, shotDurations))
                {
                    var (sceneNumber, shotNumber) = ParseShotNumbers(shot.Id);
                    var definition = existingDefinitions.SingleOrDefault(item =>
                        item.SceneNumber == sceneNumber && item.ShotNumber == shotNumber);
                    if (definition is null)
                    {
                        definition = new ShotDefinition
                        {
                            Id = Guid.NewGuid(),
                            ProjectId = context.ProjectId,
                            ShotResourceId = persistedShot.ResourceId,
                            ScriptResourceId = scriptAsset.ResourceId,
                            SceneNumber = sceneNumber,
                            ShotNumber = shotNumber
                        };
                        dbContext.ShotDefinitions.Add(definition);
                        existingDefinitions.Add(definition);
                    }
                    definition.DurationSeconds = duration;
                    definition.UpdatedAtUtc = now;
                }
                await dbContext.SaveChangesAsync(cancellationToken);

                var removedShots = replaceExistingShots
                    ? await assetWriter.DeleteResourcesAsync(
                        context.ProjectId,
                        existingDefinitions
                            .Where(shot => !incomingKeys.Contains((shot.SceneNumber, shot.ShotNumber)))
                            .Select(shot => shot.ShotResourceId)
                            .ToArray(),
                        cancellationToken)
                    : [];

                var legacyStoryboards = existingShots
                    .Where(asset => !ShotNamePattern().IsMatch(asset.Name)
                        && GetResourceSubject(asset.Name).Equals(subject, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (legacyStoryboards.Count > 0)
                {
                    await assetWriter.DeleteAsync(
                        context.ProjectId,
                        legacyStoryboards.Select(asset => asset.Id).ToArray(),
                        cancellationToken);
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
                    message = $"Agent 已创建 {persistedShots.Count} 个独立镜头资源：{subject}；预计总时长 {totalDurationSeconds:0.##} 秒",
                    data = new
                    {
                        assets = persistedShots.Select(AssetResponse.FromAsset),
                        totalDurationSeconds,
                        targetMinimumSeconds,
                        targetMaximumSeconds,
                        replaceExistingShots,
                        removedShotCount = removedShots.Count,
                        scriptResourceId = scriptAsset.ResourceId
                    }
                }, cancellationToken);
                return JsonSerializer.Serialize(
                    new
                    {
                        assets = persistedShots.Select(AssetResponse.FromAsset),
                        shotCount = persistedShots.Count,
                        totalDurationSeconds,
                        targetMinimumSeconds,
                        targetMaximumSeconds,
                        replaceExistingShots,
                        removedShotCount = removedShots.Count,
                        scriptResourceId = scriptAsset.ResourceId
                    },
                    context.JsonOptions);
            }
            catch (ArgumentException exception)
            {
                await context.WriteEventAsync(new
                {
                    type = "process",
                    stage = "tool.failed",
                    message = $"创建独立镜头资源失败：{exception.GetType().Name}: {exception.Message}"
                }, CancellationToken.None);
                return JsonSerializer.Serialize(
                    new
                    {
                        success = false,
                        errorType = exception.GetType().Name,
                        error = exception.Message,
                        instruction = "根据 error 指出的具体字段修正分镜后再次调用 write_storyboard；不要原样重试。"
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
        description: "按来源剧本和结构化镜号严格校验并持久化分镜。每镜必须填写角色/主体、场景/时空、景别、机位/角度、画面与动作、台词/声音、镜头运动、预计时长和连续性/制作备注。scriptAssetId 可传当前项目中该剧本任一版本的资产 ID，工具始终使用同一逻辑剧本的最新版本校验；同一剧本的同一 S场-镜号只新增版本。整稿传 replaceExistingShots=true，并删除本稿缺失的旧镜头；局部重生成传 false，工具会合并未提交镜头。每镜限 3–60 秒。仅当导演明确指定总时长时才传 targetMinimumSeconds 和 targetMaximumSeconds 并校验区间；未指定时两者都传 null，不限制总时长。工具始终统计并返回实际预计总时长。",
        serializerOptions: context.JsonOptions);

    private async Task<HashSet<int>> ReadScriptSceneNumbersAsync(
        Guid projectId,
        Asset scriptAsset,
        CancellationToken cancellationToken)
    {
        await using var stream = await assetReader.OpenReadAsync(projectId, scriptAsset, cancellationToken)
            ?? throw new FileNotFoundException($"剧本文件不存在：{scriptAsset.FileName}");
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        return ScriptSceneHeadingPattern()
            .Matches(content)
            .Select(match => int.Parse(match.Groups[1].Value))
            .ToHashSet();
    }

    private static (int SceneNumber, int ShotNumber) ParseShotNumbers(string shotId)
    {
        var parts = shotId[1..].Split('-');
        return (int.Parse(parts[0]), int.Parse(parts[1]));
    }

    private static double GetDurationSeconds(StoryboardShot shot)
    {
        var durationIndex = shot.Headers
            .Select((header, index) => new { Header = header.Trim(), Index = index })
            .FirstOrDefault(item => item.Header.Equals("预计时长", StringComparison.OrdinalIgnoreCase))
            ?.Index;
        if (durationIndex is null || durationIndex.Value >= shot.Values.Count)
        {
            throw new ArgumentException($"镜头 {shot.Id} 缺少“预计时长”列。");
        }

        var value = shot.Values[durationIndex.Value].Trim();
        var match = DurationPattern().Match(value);
        if (!match.Success
            || !double.TryParse(
                match.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var duration))
        {
            throw new ArgumentException(
                $"镜头 {shot.Id} 的预计时长“{value}”无效；必须填写单一秒数，例如 6s，不得填写范围或模糊描述。");
        }
        return duration;
    }

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

    [GeneratedRegex("^S\\d{2,}-\\d{2,}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShotIdPattern();

    [GeneratedRegex("·\\s*S\\d{2,}-\\d{2,}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShotNamePattern();

    [GeneratedRegex("^(?:约\\s*)?(\\d+(?:\\.\\d+)?)\\s*(?:s|秒)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DurationPattern();

    [GeneratedRegex("^#{2,4}\\s*(\\d+)\\s*[.、．]", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptSceneHeadingPattern();

    private sealed record StoryboardShot(
        string Id,
        string SceneHeading,
        IReadOnlyList<string> Headers,
        IReadOnlyList<string> Values);
}