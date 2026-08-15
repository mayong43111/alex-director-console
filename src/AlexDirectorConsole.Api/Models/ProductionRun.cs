namespace AlexDirectorConsole.Api.Models;

public sealed class ProductionRun
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public string Status { get; set; } = "planning";

    public string CurrentStage { get; set; } = "planning";

    public string OriginalInstruction { get; set; } = string.Empty;

    public string SpecJson { get; set; } = "{}";

    public string? LastError { get; set; }

    public bool DryRun { get; set; }

    public bool KeepVmRunning { get; set; }

    public bool VmStartedByRun { get; set; }

    public Guid? FinalAssetId { get; set; }

    public string? LeaseOwner { get; set; }

    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }
}