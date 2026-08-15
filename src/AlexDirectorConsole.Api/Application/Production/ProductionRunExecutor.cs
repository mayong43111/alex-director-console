using AlexDirectorConsole.Api.Data;
using AlexDirectorConsole.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.Api.Application.Production;

public interface IProductionRunExecutor
{
    Task<bool> ExecuteNextAsync(string workerId, CancellationToken cancellationToken);
}

public sealed class ProductionRunExecutor(
    AppDbContext dbContext,
    IProductionSkillRunner skillRunner,
    IAzureVmLifecycleService vmLifecycle,
    ILogger<ProductionRunExecutor> logger) : IProductionRunExecutor
{
    public async Task<bool> ExecuteNextAsync(string workerId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = await dbContext.ProductionRuns
            .AsNoTracking()
            .Where(run => !run.DryRun && (run.Status == "queued" || run.Status == "running"))
            .Select(run => new
            {
                run.Id,
                run.Status,
                run.LeaseOwner,
                run.LeaseExpiresAtUtc,
                run.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
        var candidate = candidates
            .Where(run => run.Status == "queued" || run.LeaseExpiresAtUtc < now)
            .OrderBy(run => run.CreatedAtUtc)
            .FirstOrDefault();
        if (candidate is null)
        {
            return false;
        }
        var acquired = await dbContext.ProductionRuns
            .Where(run => run.Id == candidate.Id
                && !run.DryRun
                && run.Status == candidate.Status
                && run.LeaseOwner == candidate.LeaseOwner
                && run.LeaseExpiresAtUtc == candidate.LeaseExpiresAtUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, "running")
                .SetProperty(run => run.LeaseOwner, workerId)
                .SetProperty(run => run.LeaseExpiresAtUtc, now.AddHours(3))
                .SetProperty(run => run.StartedAtUtc, run => run.StartedAtUtc ?? now),
                cancellationToken);
        if (acquired != 1)
        {
            return true;
        }

        dbContext.ChangeTracker.Clear();
        var run = await dbContext.ProductionRuns.SingleAsync(item => item.Id == candidate.Id, cancellationToken);
        try
        {
            await ExecuteStageAsync(run, "frames", cancellationToken);
            await ExecuteStageAsync(run, "narration", cancellationToken);

            await ReconcileExistingStageAsync(run, "videos", cancellationToken);
            var hasPendingVideos = await dbContext.ProductionRunItems.AnyAsync(
                item => item.RunId == run.Id && item.Stage == "videos" && item.Status != "succeeded",
                cancellationToken);
            if (hasPendingVideos)
            {
                run.CurrentStage = "waiting-vm";
                await dbContext.SaveChangesAsync(cancellationToken);
                run.VmStartedByRun = await vmLifecycle.EnsureStartedAsync(cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                await ExecuteStageAsync(run, "videos", cancellationToken);
            }
            run.CurrentStage = "assembly";
            await dbContext.SaveChangesAsync(cancellationToken);
            run.FinalAssetId = await skillRunner.AssembleAsync(run, cancellationToken);
            run.Status = "completed";
            run.CurrentStage = "completed";
            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            run.LeaseOwner = null;
            run.LeaseExpiresAtUtc = null;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Production run {RunId} failed", run.Id);
            run.Status = "failed";
            run.LastError = exception.Message;
            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            run.LeaseOwner = null;
            run.LeaseExpiresAtUtc = null;
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            if (run.VmStartedByRun && !run.KeepVmRunning)
            {
                try
                {
                    await vmLifecycle.DeallocateAsync(CancellationToken.None);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Failed to deallocate VM for production run {RunId}", run.Id);
                }
            }
        }
        return true;
    }

    private async Task ReconcileExistingStageAsync(
        ProductionRun run,
        string stage,
        CancellationToken cancellationToken)
    {
        var items = await dbContext.ProductionRunItems
            .Where(item => item.RunId == run.Id && item.Stage == stage && item.Status != "succeeded")
            .ToListAsync(cancellationToken);
        foreach (var item in items)
        {
            var outputAssetId = await skillRunner.FindExistingOutputAsync(item, cancellationToken);
            if (outputAssetId is null)
            {
                continue;
            }
            item.OutputAssetId = outputAssetId;
            item.Status = "succeeded";
            item.CompletedAtUtc = DateTimeOffset.UtcNow;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ExecuteStageAsync(
        ProductionRun run,
        string stage,
        CancellationToken cancellationToken)
    {
        run.CurrentStage = stage;
        await dbContext.SaveChangesAsync(cancellationToken);
        var items = await dbContext.ProductionRunItems
            .Where(item => item.RunId == run.Id && item.Stage == stage && item.Status != "succeeded")
            .OrderBy(item => item.ShotName)
            .ToListAsync(cancellationToken);
        foreach (var item in items)
        {
            item.Status = "running";
            item.Attempt++;
            item.StartedAtUtc = DateTimeOffset.UtcNow;
            item.ErrorCode = null;
            item.ErrorDetail = null;
            run.LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(3);
            await dbContext.SaveChangesAsync(cancellationToken);
            try
            {
                item.OutputAssetId = await skillRunner.ExecuteShotStageAsync(run, item, cancellationToken);
                item.Status = "succeeded";
                item.CompletedAtUtc = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                item.Status = "failed";
                item.ErrorCode = exception.GetType().Name;
                item.ErrorDetail = exception.Message;
                item.CompletedAtUtc = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(CancellationToken.None);
                throw;
            }
        }
    }
}