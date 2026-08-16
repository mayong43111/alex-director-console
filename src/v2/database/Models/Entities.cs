namespace AlexDirectorConsole.V2.Database.Models;

public sealed class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CurrentCreativeSettingsId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class ProductionEpisode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public int EpisodeNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public double? TargetSeconds { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class Asset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid? ProductionEpisodeId { get; set; }
    public Guid ResourceId { get; set; }
    public int Version { get; set; }
    public int Number { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public string? DocumentJson { get; set; }
    public string? BlobKey { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long SizeBytes { get; set; }
    public string? GenerationMetadataJson { get; set; }
    public Guid? CreatedByTaskId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class ResourceState
{
    public Guid ProjectId { get; set; }
    public Guid ResourceId { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public Guid CurrentAssetId { get; set; }
    public Guid? ApprovedAssetId { get; set; }
    public string LifecycleStatus { get; set; } = string.Empty;
    public bool IsStale { get; set; }
    public string? StaleReason { get; set; }
    public DateTimeOffset? StaleSinceUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class AssetDependency
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid ConsumerAssetId { get; set; }
    public Guid SourceAssetId { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class VisualReference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid ImageAssetId { get; set; }
    public Guid SubjectResourceId { get; set; }
    public string SubjectType { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string ReviewStatus { get; set; } = string.Empty;
    public Guid? InheritsFromAssetId { get; set; }
    public Guid? ApprovedByDecisionId { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class ShotDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid ProductionEpisodeId { get; set; }
    public Guid ShotResourceId { get; set; }
    public Guid ShotAssetId { get; set; }
    public Guid ScriptPackageAssetId { get; set; }
    public Guid SceneId { get; set; }
    public int SceneNumber { get; set; }
    public int ShotNumber { get; set; }
    public double DurationSeconds { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class ShotBeatClaim
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid ProductionEpisodeId { get; set; }
    public Guid ScriptPackageAssetId { get; set; }
    public Guid BeatId { get; set; }
    public Guid ShotAssetId { get; set; }
    public Guid ShotResourceId { get; set; }
    public int OrdinalInShot { get; set; }
}

public sealed class ShotAssetLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid ProductionEpisodeId { get; set; }
    public Guid ShotResourceId { get; set; }
    public Guid AssetId { get; set; }
    public string Role { get; set; } = string.Empty;
    public Guid? SubjectId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class DirectorDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid? ProductionEpisodeId { get; set; }
    public string DecisionType { get; set; } = string.Empty;
    public string SubjectType { get; set; } = string.Empty;
    public Guid? SubjectResourceId { get; set; }
    public Guid? SubjectAssetId { get; set; }
    public string Question { get; set; } = string.Empty;
    public string OptionsJson { get; set; } = "[]";
    public string? SelectedOption { get; set; }
    public string? DecisionText { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? RequestedByTaskId { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
}

public sealed class ValidationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid? ProductionEpisodeId { get; set; }
    public Guid SubjectAssetId { get; set; }
    public string ValidatorSet { get; set; } = string.Empty;
    public string ValidatorVersion { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? TriggeredByTaskId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed class ValidationResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ValidationRunId { get; set; }
    public string GateId { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? SubjectType { get; set; }
    public Guid? SubjectId { get; set; }
    public string ReferencesJson { get; set; } = "[]";
    public string? SuggestedAction { get; set; }
}

public sealed class AgentTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid? ProductionEpisodeId { get; set; }
    public Guid? ParentTaskId { get; set; }
    public string Intent { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string ContextSnapshotJson { get; set; } = "{}";
    public string PlanJson { get; set; } = "{}";
    public string Status { get; set; } = string.Empty;
    public string? CurrentStep { get; set; }
    public int ProgressCompleted { get; set; }
    public int? ProgressTotal { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public Guid? RequestedByMessageId { get; set; }
    public string? Model { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class AgentTaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? ProductionEpisodeId { get; set; }
    public int Ordinal { get; set; }
    public string ObjectType { get; set; } = string.Empty;
    public Guid? ObjectResourceId { get; set; }
    public string InputAssetIdsJson { get; set; } = "[]";
    public string Action { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Attempt { get; set; }
    public string OutputAssetIdsJson { get; set; } = "[]";
    public string? ErrorCode { get; set; }
    public string? ErrorDetail { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed class AgentTaskEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public long Sequence { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? Stage { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? DataJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class AgentTaskOutput
{
    public Guid TaskId { get; set; }
    public Guid? TaskItemId { get; set; }
    public Guid AssetId { get; set; }
    public string Role { get; set; } = string.Empty;
}

public sealed class ProductionRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid ProductionEpisodeId { get; set; }
    public Guid ScriptPackageAssetId { get; set; }
    public Guid CreativeSettingsAssetId { get; set; }
    public Guid? ReferenceBoardAssetId { get; set; }
    public Guid? PreflightValidationRunId { get; set; }
    public Guid? RequestedByTaskId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string CurrentStage { get; set; } = string.Empty;
    public string SpecJson { get; set; } = "{}";
    public string OriginalInstruction { get; set; } = string.Empty;
    public string? LastError { get; set; }
    public Guid? FinalAssetId { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class ProductionRunItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ProductionEpisodeId { get; set; }
    public Guid ShotResourceId { get; set; }
    public Guid ShotAssetId { get; set; }
    public string ShotName { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Attempt { get; set; }
    public string InputAssetIdsJson { get; set; } = "[]";
    public string? InputFingerprint { get; set; }
    public Guid? OutputAssetId { get; set; }
    public string? CostJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorDetail { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}