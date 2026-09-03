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
    public byte[]? BlobContent { get; set; }
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
    public Guid? ProjectId { get; set; }
    public Guid? AgentId { get; set; }
    public Guid? SessionId { get; set; }
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
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public DateTimeOffset? CancellationRequestedAtUtc { get; set; }
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
    public string RunType { get; set; } = "shot-frames";
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
    public string? ExternalJobId { get; set; }
    public Guid? OutputAssetId { get; set; }
    public string? CostJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorDetail { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed class FoundryConfiguration
{
    public int Id { get; set; } = 1;
    public string LlmProvider { get; set; } = "azure-foundry";
    public string Endpoint { get; set; } = string.Empty;
    public string Deployment { get; set; } = "gpt-5.4";
    public string ProtectedApiKey { get; set; } = string.Empty;
    public string VllmBaseUrl { get; set; } = "http://127.0.0.1:8000/v1";
    public string VllmModel { get; set; } = "Qwen 3.8 27B";
    public string ProtectedVllmApiKey { get; set; } = string.Empty;
    public string ImageProvider { get; set; } = "azure-foundry";
    public string ImageEndpoint { get; set; } = string.Empty;
    public string ImageDeployment { get; set; } = "gpt-image-2";
    public string ImageQuality { get; set; } = "medium";
    public string ProtectedImageApiKey { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class ComfyUiConfiguration
{
    public int Id { get; set; } = 1;
    public string ConnectionMode { get; set; } = "local-http";
    public string BaseUrl { get; set; } = "http://127.0.0.1:8188";
    public string WorkflowProfile { get; set; } = "minimax-h3-fl2va-turbo-4step";
    public string TextToImageWorkflow { get; set; } = "krea-2-text-to-image";
    public string ImageEditWorkflow { get; set; } = "qwen-image-edit-2511";
    public int MaxConcurrentJobs { get; set; } = 1;
    public bool IsEnabled { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class VoicePackage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ResourceId { get; set; } = Guid.NewGuid();
    public int Version { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Engine { get; set; } = "gpt-sovits";
    public string BaseModelVersion { get; set; } = "v2ProPlus";
    public string GptWeightsPath { get; set; } = string.Empty;
    public string SoVitsWeightsPath { get; set; } = string.Empty;
    public string ReferenceAudioFileName { get; set; } = string.Empty;
    public string ReferenceAudioContentType { get; set; } = "audio/wav";
    public byte[] ReferenceAudioContent { get; set; } = [];
    public string ReferenceText { get; set; } = string.Empty;
    public string Language { get; set; } = "zh";
    public string Dialect { get; set; } = "普通话";
    public string SpeakingStyle { get; set; } = string.Empty;
    public double DefaultSpeed { get; set; } = 1;
    public string License { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
    public Guid? VoiceTrainingJobId { get; set; }
    public string UsagePolicy { get; set; } = "licensed";
    public bool CanExport { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public bool IsCurrent { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class VoiceTrainingJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string TrainingMode { get; set; } = "replica";
    public string Engine { get; set; } = "gpt-sovits";
    public string BaseModelVersion { get; set; } = "v2ProPlus";
    public string Language { get; set; } = "zh";
    public string Dialect { get; set; } = "普通话";
    public string SpeakingStyle { get; set; } = string.Empty;
    public double DefaultSpeed { get; set; } = 1;
    public string SourceDescription { get; set; } = string.Empty;
    public string UsagePolicy { get; set; } = "practice-only";
    public bool CanExport { get; set; }
    public bool RightsConfirmed { get; set; }
    public string Status { get; set; } = "draft";
    public int ProgressPercent { get; set; }
    public string? ExternalJobId { get; set; }
    public string? GptWeightsPath { get; set; }
    public string? SoVitsWeightsPath { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class VoiceTrainingSample
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VoiceTrainingJobId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "audio/wav";
    public byte[] AudioContent { get; set; } = [];
    public string Transcript { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class SkillDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool IsSystem { get; set; } = true;
    public string SourcePath { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class AgentDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class AgentSkill
{
    public Guid AgentId { get; set; }
    public string SkillId { get; set; } = string.Empty;
}

public sealed class Session
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AgentId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid? ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Runtime { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class SessionMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public long Sequence { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Model { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class CopilotConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class CopilotMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public long Sequence { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Model { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}