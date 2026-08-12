using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlexDirectorConsole.Api.Application.Assets;
using AlexDirectorConsole.Api.Data;
using AlexDirectorConsole.Api.Services;
using AlexDirectorConsole.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.Api.Application.Maintenance;

public sealed class ApplicationMaintenanceRunner(
    AppDbContext dbContext,
    IAssetReader assetReader,
    IBlobStorage blobStorage,
    IAgentSkillExecutor skillExecutor,
    IHostEnvironment environment,
    ILogger<ApplicationMaintenanceRunner> logger) : IApplicationMaintenanceRunner
{
    private readonly string statePath = Path.Combine(
        environment.ContentRootPath,
        "App_Data",
        "maintenance-state.json");

    public async Task RunPendingAsync(CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken);
        await RunTaskAsync("asset-resource-versions-v1", BackfillAssetVersionsAsync, state, cancellationToken);
        await RunTaskAsync("generated-image-resources-v1", RepairGeneratedImageResourcesAsync, state, cancellationToken);
        await RunTaskAsync("dynamic-shot-sources-v1", RemoveStaticShotSourcesAsync, state, cancellationToken);
        await RunTaskAsync(
            "analysis-assets-v1",
            async token => _ = await skillExecutor.BackfillAnalysisAssetsAsync(token),
            state,
            cancellationToken);
    }

    private async Task RunTaskAsync(
        string taskId,
        Func<CancellationToken, Task> task,
        MaintenanceState state,
        CancellationToken cancellationToken)
    {
        if (state.CompletedAtUtc.ContainsKey(taskId))
        {
            logger.LogInformation("Maintenance task {TaskId} already completed", taskId);
            return;
        }

        logger.LogInformation("Starting maintenance task {TaskId}", taskId);
        await task(cancellationToken);
        state.CompletedAtUtc[taskId] = DateTimeOffset.UtcNow;
        await SaveStateAsync(state, cancellationToken);
        logger.LogInformation("Completed maintenance task {TaskId}", taskId);
    }

    private async Task BackfillAssetVersionsAsync(CancellationToken cancellationToken)
    {
        var assets = await dbContext.Assets.ToListAsync(cancellationToken);
        if (!assets.Any(asset => asset.Name.Contains("导演修订", StringComparison.Ordinal)))
        {
            return;
        }

        foreach (var group in assets.GroupBy(asset => new
        {
            asset.ProjectId,
            asset.Type,
            Subject = GetResourceSubject(asset.Name)
        }))
        {
            var versions = group
                .OrderBy(asset => asset.CreatedAtUtc)
                .ThenBy(asset => asset.Id)
                .ToList();
            var resourceId = versions[0].Id;
            var canonicalName = versions
                .FirstOrDefault(asset => !asset.Name.Contains("导演修订", StringComparison.Ordinal))
                ?.Name ?? versions[0].Name;
            for (var index = 0; index < versions.Count; index++)
            {
                versions[index].ResourceId = resourceId;
                versions[index].Version = index + 1;
                versions[index].Name = canonicalName;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RepairGeneratedImageResourcesAsync(CancellationToken cancellationToken)
    {
        var generatedImages = (await dbContext.Assets
                .Where(asset => asset.Type == "media"
                    && asset.ContentType.StartsWith("image/")
                    && asset.Name.Contains("AI 图片"))
                .ToListAsync(cancellationToken))
            .Select(asset => new
            {
                Asset = asset,
                ResourceKey = GetGeneratedImageResourceKey(asset.FileName)
            })
            .Where(item => item.ResourceKey is not null)
            .ToList();
        var changed = false;
        foreach (var group in generatedImages.GroupBy(
            item => new { item.Asset.ProjectId, ResourceKey = item.ResourceKey! }))
        {
            var versions = group
                .OrderBy(item => item.Asset.CreatedAtUtc)
                .ThenBy(item => item.Asset.Id)
                .Select(item => item.Asset)
                .ToList();
            var resourceId = versions[0].Id;
            var canonicalName = $"{group.Key.ResourceKey} · AI 图片";
            for (var index = 0; index < versions.Count; index++)
            {
                var asset = versions[index];
                var version = index + 1;
                if (asset.ResourceId == resourceId
                    && asset.Version == version
                    && asset.Name.Equals(canonicalName, StringComparison.Ordinal))
                {
                    continue;
                }
                asset.ResourceId = resourceId;
                asset.Version = version;
                asset.Name = canonicalName;
                changed = true;
            }
        }
        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task RemoveStaticShotSourcesAsync(CancellationToken cancellationToken)
    {
        var shots = await dbContext.Assets
            .Where(asset => asset.Type == "shot" && asset.ContentType.StartsWith("text/"))
            .ToListAsync(cancellationToken);
        var changed = false;
        var replacedBlobKeys = new List<string>();
        foreach (var shot in shots)
        {
            EnsureProjectBlobKey(shot.ProjectId, shot.BlobKey);
            await using var source = await assetReader.OpenReadAsync(shot.ProjectId, shot, cancellationToken);
            if (source is null)
            {
                continue;
            }
            using var reader = new StreamReader(source, detectEncodingFromByteOrderMarks: true);
            var content = await reader.ReadToEndAsync(cancellationToken);
            var headingIndex = content.IndexOf(
                $"{Environment.NewLine}## 来源资源",
                StringComparison.Ordinal);
            if (headingIndex < 0)
            {
                headingIndex = content.IndexOf("\n## 来源资源", StringComparison.Ordinal);
            }
            if (headingIndex < 0)
            {
                continue;
            }

            var revisedContent = content[..headingIndex].TrimEnd() + Environment.NewLine;
            var bytes = Encoding.UTF8.GetBytes(revisedContent);
            var revisedBlobKey = $"{shot.ProjectId:N}/shot/{shot.Id:N}-dynamic.md";
            EnsureProjectBlobKey(shot.ProjectId, revisedBlobKey);
            await blobStorage.DeleteAsync(revisedBlobKey, cancellationToken);
            await using var revisedStream = new MemoryStream(bytes, writable: false);
            await blobStorage.SaveAsync(revisedBlobKey, revisedStream, cancellationToken);
            replacedBlobKeys.Add(shot.BlobKey);
            shot.BlobKey = revisedBlobKey;
            shot.SizeBytes = bytes.LongLength;
            shot.UpdatedAtUtc = DateTimeOffset.UtcNow;
            changed = true;
        }
        if (!changed)
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var blobKey in replacedBlobKeys)
        {
            var projectId = Guid.ParseExact(blobKey[..32], "N");
            EnsureProjectBlobKey(projectId, blobKey);
            await blobStorage.DeleteAsync(blobKey, cancellationToken);
        }
    }

    private static void EnsureProjectBlobKey(Guid projectId, string blobKey)
    {
        if (!blobKey.StartsWith($"{projectId:N}/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("资产 BlobKey 不属于记录中的项目。");
        }
    }

    private async Task<MaintenanceState> LoadStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(statePath))
        {
            return new MaintenanceState();
        }
        await using var stream = File.OpenRead(statePath);
        return await JsonSerializer.DeserializeAsync<MaintenanceState>(stream, cancellationToken: cancellationToken)
            ?? new MaintenanceState();
    }

    private async Task SaveStateAsync(MaintenanceState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        var temporaryPath = statePath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                state,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true },
                cancellationToken);
        }
        File.Move(temporaryPath, statePath, overwrite: true);
    }

    private static string GetResourceSubject(string value) =>
        value.Split('·', StringSplitOptions.TrimEntries)[0];

    private static string? GetGeneratedImageResourceKey(string fileName)
    {
        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var withoutVersion = Regex.Replace(
            withoutExtension,
            "-v\\d+$",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (withoutVersion.Equals(withoutExtension, StringComparison.Ordinal))
        {
            return null;
        }
        var segments = withoutVersion
            .Split('·', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        while (segments.Count > 0
            && segments[^1].Equals("AI 图片", StringComparison.OrdinalIgnoreCase))
        {
            segments.RemoveAt(segments.Count - 1);
        }
        return segments.Count == 0 ? null : string.Join(" · ", segments);
    }

    private sealed class MaintenanceState
    {
        public Dictionary<string, DateTimeOffset> CompletedAtUtc { get; init; } = [];
    }
}