using AlexDirectorConsole.Api.Application.Production;

namespace AlexDirectorConsole.Api.Contracts;

public sealed record CreateProductionRunRequest(
    string Instruction,
    bool DryRun = true,
    bool KeepVmRunning = false,
    string? ShotNameContains = null);

public sealed record ProductionRunResponse(
    Guid Id,
    Guid ProjectId,
    string Status,
    string CurrentStage,
    bool DryRun,
    bool KeepVmRunning,
    int ShotCount,
    IReadOnlyDictionary<string, int> StageCounts,
    IReadOnlyDictionary<string, int> StatusCounts,
    DateTimeOffset CreatedAt,
    string? LastError,
    Guid? FinalAssetId)
{
    public static ProductionRunResponse FromSnapshot(ProductionRunSnapshot snapshot) => new(
        snapshot.Run.Id,
        snapshot.Run.ProjectId,
        snapshot.Run.Status,
        snapshot.Run.CurrentStage,
        snapshot.Run.DryRun,
        snapshot.Run.KeepVmRunning,
        snapshot.ShotCount,
        snapshot.StageCounts,
        snapshot.StatusCounts,
        snapshot.Run.CreatedAtUtc,
        snapshot.Run.LastError,
        snapshot.Run.FinalAssetId);
}