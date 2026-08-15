using System.Text.Json;
using AlexDirectorConsole.Api.Data;
using AlexDirectorConsole.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.Api.Application.Production;

public sealed class ProductionRunService(AppDbContext dbContext) : IProductionRunService
{
    private static readonly string[] ShotStages = ["frames", "videos", "narration"];

    public async Task<ProductionRunSnapshot> StartAsync(
        Guid projectId,
        string instruction,
        bool dryRun,
        bool keepVmRunning,
        string? shotNameContains,
        CancellationToken cancellationToken)
    {
        var normalizedInstruction = instruction.Trim();
        if (string.IsNullOrWhiteSpace(normalizedInstruction))
        {
            throw new ArgumentException("导演令不能为空。", nameof(instruction));
        }
        if (!await dbContext.Projects.AsNoTracking().AnyAsync(
            project => project.Id == projectId,
            cancellationToken))
        {
            throw new KeyNotFoundException("项目不存在。");
        }

        var shotVersions = await dbContext.Assets
            .AsNoTracking()
            .Where(asset => asset.ProjectId == projectId && asset.Type == "shot")
            .ToListAsync(cancellationToken);
        var normalizedShotFilter = shotNameContains?.Trim();
        var shots = shotVersions
            .GroupBy(asset => asset.ResourceId)
            .Select(group => group
                .OrderByDescending(asset => asset.Version)
                .ThenByDescending(asset => asset.CreatedAtUtc)
                .First())
            .OrderBy(asset => asset.Name, StringComparer.Ordinal)
            .Where(asset => string.IsNullOrWhiteSpace(normalizedShotFilter)
                || asset.Name.Contains(normalizedShotFilter, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (shots.Length == 0)
        {
            throw new InvalidOperationException("项目没有可制作的结构化 shot。");
        }

        var duplicateShotNames = shots
            .GroupBy(shot => shot.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateShotNames.Length > 0)
        {
            throw new InvalidOperationException($"存在重复镜头名称：{string.Join("、", duplicateShotNames)}。");
        }

        var now = DateTimeOffset.UtcNow;
        var run = new ProductionRun
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Status = dryRun ? "ready" : "queued",
            CurrentStage = "frames",
            OriginalInstruction = normalizedInstruction,
            SpecJson = JsonSerializer.Serialize(new
            {
                referencePolicy = "use-existing-and-continue-from-text-when-missing",
                requireNarration = true,
                videoProvider = "minimax-h3",
                assemblyProvider = "local-ffmpeg"
                ,shotNameContains = normalizedShotFilter
            }),
            DryRun = dryRun,
            KeepVmRunning = keepVmRunning,
            CreatedAtUtc = now
        };
        var items = shots
            .SelectMany(shot => ShotStages.Select(stage => new ProductionRunItem
            {
                Id = Guid.NewGuid(),
                RunId = run.Id,
                ProjectId = projectId,
                ShotResourceId = shot.ResourceId,
                ShotAssetId = shot.Id,
                ShotName = shot.Name,
                Stage = stage,
                Status = "pending",
                CreatedAtUtc = now
            }))
            .ToArray();

        dbContext.ProductionRuns.Add(run);
        dbContext.ProductionRunItems.AddRange(items);
        await dbContext.SaveChangesAsync(cancellationToken);
        return BuildSnapshot(run, items);
    }

    public async Task<ProductionRunSnapshot?> GetAsync(
        Guid projectId,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.ProductionRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == runId && item.ProjectId == projectId,
                cancellationToken);
        if (run is null)
        {
            return null;
        }
        var items = await dbContext.ProductionRunItems
            .AsNoTracking()
            .Where(item => item.RunId == runId && item.ProjectId == projectId)
            .OrderBy(item => item.ShotName)
            .ThenBy(item => item.Stage)
            .ToListAsync(cancellationToken);
        return BuildSnapshot(run, items);
    }

    public async Task<ProductionRunSnapshot?> ResumeAsync(
        Guid projectId,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await dbContext.ProductionRuns.SingleOrDefaultAsync(
            item => item.Id == runId && item.ProjectId == projectId,
            cancellationToken);
        if (run is null)
        {
            return null;
        }
        if (run.DryRun || run.Status != "failed")
        {
            throw new InvalidOperationException("只有失败的真实生产任务可以恢复。");
        }

        var items = await dbContext.ProductionRunItems
            .Where(item => item.RunId == runId && item.ProjectId == projectId)
            .OrderBy(item => item.ShotName)
            .ThenBy(item => item.Stage)
            .ToListAsync(cancellationToken);
        foreach (var item in items.Where(item => item.Status == "failed"))
        {
            item.Status = "pending";
            item.ErrorCode = null;
            item.ErrorDetail = null;
            item.StartedAtUtc = null;
            item.CompletedAtUtc = null;
        }
        run.Status = "queued";
        run.CurrentStage = items.Any(item => item.Stage == "frames" && item.Status != "succeeded")
            ? "frames"
            : items.Any(item => item.Stage == "narration" && item.Status != "succeeded")
                ? "narration"
                : items.Any(item => item.Stage == "videos" && item.Status != "succeeded")
                    ? "videos"
                    : "assembly";
        run.LastError = null;
        run.CompletedAtUtc = null;
        run.FinalAssetId = null;
        run.LeaseOwner = null;
        run.LeaseExpiresAtUtc = null;
        run.VmStartedByRun = false;
        await dbContext.SaveChangesAsync(cancellationToken);
        return BuildSnapshot(run, items);
    }

    private static ProductionRunSnapshot BuildSnapshot(
        ProductionRun run,
        IReadOnlyList<ProductionRunItem> items) => new(
        run,
        items.Select(item => item.ShotResourceId).Distinct().Count(),
        items.GroupBy(item => item.Stage).ToDictionary(group => group.Key, group => group.Count()),
        items.GroupBy(item => item.Status).ToDictionary(group => group.Key, group => group.Count()),
        items);
}