using AlexDirectorConsole.Api.Models;

namespace AlexDirectorConsole.Api.Application.Production;

public sealed record ProductionRunSnapshot(
    ProductionRun Run,
    int ShotCount,
    IReadOnlyDictionary<string, int> StageCounts,
    IReadOnlyDictionary<string, int> StatusCounts,
    IReadOnlyList<ProductionRunItem> Items);

public interface IProductionRunService
{
    Task<ProductionRunSnapshot> StartAsync(
        Guid projectId,
        string instruction,
        bool dryRun,
        bool keepVmRunning,
        string? shotNameContains,
        CancellationToken cancellationToken);

    Task<ProductionRunSnapshot?> GetAsync(
        Guid projectId,
        Guid runId,
        CancellationToken cancellationToken);

    Task<ProductionRunSnapshot?> ResumeAsync(
        Guid projectId,
        Guid runId,
        CancellationToken cancellationToken);
}