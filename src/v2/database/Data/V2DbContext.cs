using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Database.Data;

public sealed class V2DbContext(DbContextOptions<V2DbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProductionEpisode> ProductionEpisodes => Set<ProductionEpisode>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<ResourceState> ResourceStates => Set<ResourceState>();
    public DbSet<AssetDependency> AssetDependencies => Set<AssetDependency>();
    public DbSet<VisualReference> VisualReferences => Set<VisualReference>();
    public DbSet<ShotDefinition> ShotDefinitions => Set<ShotDefinition>();
    public DbSet<ShotBeatClaim> ShotBeatClaims => Set<ShotBeatClaim>();
    public DbSet<ShotAssetLink> ShotAssetLinks => Set<ShotAssetLink>();
    public DbSet<DirectorDecision> DirectorDecisions => Set<DirectorDecision>();
    public DbSet<ValidationRun> ValidationRuns => Set<ValidationRun>();
    public DbSet<ValidationResult> ValidationResults => Set<ValidationResult>();
    public DbSet<AgentTask> AgentTasks => Set<AgentTask>();
    public DbSet<AgentTaskItem> AgentTaskItems => Set<AgentTaskItem>();
    public DbSet<AgentTaskEvent> AgentTaskEvents => Set<AgentTaskEvent>();
    public DbSet<AgentTaskOutput> AgentTaskOutputs => Set<AgentTaskOutput>();
    public DbSet<ProductionRun> ProductionRuns => Set<ProductionRun>();
    public DbSet<ProductionRunItem> ProductionRunItems => Set<ProductionRunItem>();
    public DbSet<FoundryConfiguration> FoundryConfigurations => Set<FoundryConfiguration>();
    public DbSet<ComfyUiConfiguration> ComfyUiConfigurations => Set<ComfyUiConfiguration>();
    public DbSet<SkillDefinition> SkillDefinitions => Set<SkillDefinition>();
    public DbSet<CopilotConversation> CopilotConversations => Set<CopilotConversation>();
    public DbSet<CopilotMessage> CopilotMessages => Set<CopilotMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureProjects(modelBuilder);
        ConfigureAssets(modelBuilder);
        ConfigureCreativeWorkflow(modelBuilder);
        ConfigureAgentWorkflow(modelBuilder);
        ConfigureProduction(modelBuilder);
        ConfigureSystem(modelBuilder);
    }

    private static void ConfigureProjects(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("Projects");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(4000);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.CurrentCreativeSettingsId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductionEpisode>(entity =>
        {
            entity.ToTable("ProductionEpisodes", table =>
            {
                table.HasCheckConstraint("CK_ProductionEpisodes_EpisodeNumber", "EpisodeNumber > 0");
                table.HasCheckConstraint("CK_ProductionEpisodes_TargetSeconds", "TargetSeconds IS NULL OR TargetSeconds > 0");
            });
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ProjectId, item.EpisodeNumber }).IsUnique();
            entity.HasIndex(item => new { item.ProjectId, item.Status });
            entity.Property(item => item.Title).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Status).HasMaxLength(30).IsRequired();
            entity.HasOne<Project>().WithMany().HasForeignKey(item => item.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureAssets(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Asset>(entity =>
        {
            entity.ToTable("Assets", table =>
            {
                table.HasCheckConstraint("CK_Assets_Version", "Version > 0");
                table.HasCheckConstraint("CK_Assets_SchemaVersion", "SchemaVersion > 0");
                table.HasCheckConstraint("CK_Assets_SizeBytes", "SizeBytes >= 0");
            });
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ProjectId, item.ResourceId, item.Version }).IsUnique();
            entity.HasIndex(item => new { item.ProjectId, item.Number, item.Version }).IsUnique();
            entity.HasIndex(item => new { item.ProjectId, item.Type });
            entity.HasIndex(item => item.BlobKey).IsUnique();
            entity.Property(item => item.Type).HasMaxLength(50).IsRequired();
            entity.Property(item => item.Name).HasMaxLength(260).IsRequired();
            entity.Property(item => item.BlobKey).HasMaxLength(500);
            entity.Property(item => item.FileName).HasMaxLength(260);
            entity.Property(item => item.ContentType).HasMaxLength(200);
            entity.HasOne<Project>().WithMany().HasForeignKey(item => item.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductionEpisode>().WithMany().HasForeignKey(item => item.ProductionEpisodeId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AgentTask>().WithMany().HasForeignKey(item => item.CreatedByTaskId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ResourceState>(entity =>
        {
            entity.ToTable("ResourceStates");
            entity.HasKey(item => new { item.ProjectId, item.ResourceId });
            entity.HasIndex(item => new { item.ProjectId, item.CurrentAssetId }).IsUnique();
            entity.HasIndex(item => new { item.ProjectId, item.ResourceType, item.LifecycleStatus });
            entity.Property(item => item.ResourceType).HasMaxLength(50).IsRequired();
            entity.Property(item => item.LifecycleStatus).HasMaxLength(30).IsRequired();
            entity.Property(item => item.StaleReason).HasMaxLength(1000);
            entity.HasOne<Project>().WithMany().HasForeignKey(item => item.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.CurrentAssetId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.ApprovedAssetId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AssetDependency>(entity =>
        {
            entity.ToTable("AssetDependencies");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ConsumerAssetId, item.SourceAssetId, item.Role }).IsUnique();
            entity.HasIndex(item => new { item.ProjectId, item.SourceAssetId });
            entity.HasIndex(item => new { item.ProjectId, item.ConsumerAssetId });
            entity.Property(item => item.Role).HasMaxLength(50).IsRequired();
            entity.HasOne<Project>().WithMany().HasForeignKey(item => item.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.ConsumerAssetId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.SourceAssetId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureCreativeWorkflow(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VisualReference>(entity =>
        {
            entity.ToTable("VisualReferences");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ImageAssetId, item.SubjectResourceId, item.Purpose }).IsUnique();
            entity.HasIndex(item => new { item.ProjectId, item.SubjectResourceId, item.ReviewStatus });
            entity.Property(item => item.SubjectType).HasMaxLength(30).IsRequired();
            entity.Property(item => item.Purpose).HasMaxLength(30).IsRequired();
            entity.Property(item => item.Source).HasMaxLength(30).IsRequired();
            entity.Property(item => item.ReviewStatus).HasMaxLength(30).IsRequired();
            entity.HasOne<Project>().WithMany().HasForeignKey(item => item.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.ImageAssetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.InheritsFromAssetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DirectorDecision>().WithMany().HasForeignKey(item => item.ApprovedByDecisionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ShotDefinition>(entity =>
        {
            entity.ToTable("ShotDefinitions", table =>
                table.HasCheckConstraint("CK_ShotDefinitions_DurationSeconds", "DurationSeconds > 0"));
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ProjectId, item.ShotResourceId }).IsUnique();
            entity.HasIndex(item => new
            {
                item.ProjectId,
                item.ScriptPackageAssetId,
                item.ProductionEpisodeId,
                item.SceneNumber,
                item.ShotNumber
            }).IsUnique();
            entity.HasOne<Project>().WithMany().HasForeignKey(item => item.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductionEpisode>().WithMany().HasForeignKey(item => item.ProductionEpisodeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.ShotAssetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.ScriptPackageAssetId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ShotBeatClaim>(entity =>
        {
            entity.ToTable("ShotBeatClaims", table =>
                table.HasCheckConstraint("CK_ShotBeatClaims_Ordinal", "OrdinalInShot >= 0"));
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ScriptPackageAssetId, item.BeatId }).IsUnique();
            entity.HasIndex(item => new { item.ShotAssetId, item.BeatId }).IsUnique();
            entity.HasOne<Project>().WithMany().HasForeignKey(item => item.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductionEpisode>().WithMany().HasForeignKey(item => item.ProductionEpisodeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.ScriptPackageAssetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.ShotAssetId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ShotAssetLink>(entity =>
        {
            entity.ToTable("ShotAssetLinks");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ProjectId, item.ShotResourceId });
            entity.HasIndex(item => new { item.ProjectId, item.ShotResourceId, item.AssetId, item.Role, item.SubjectId }).IsUnique();
            entity.Property(item => item.Role).HasMaxLength(40).IsRequired();
            entity.HasOne<Project>().WithMany().HasForeignKey(item => item.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductionEpisode>().WithMany().HasForeignKey(item => item.ProductionEpisodeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.AssetId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DirectorDecision>(entity =>
        {
            entity.ToTable("DirectorDecisions");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ProjectId, item.Status, item.RequestedAtUtc });
            entity.Property(item => item.DecisionType).HasMaxLength(50).IsRequired();
            entity.Property(item => item.SubjectType).HasMaxLength(50).IsRequired();
            entity.Property(item => item.Question).HasMaxLength(2000).IsRequired();
            entity.Property(item => item.SelectedOption).HasMaxLength(200);
            entity.Property(item => item.DecisionText).HasMaxLength(4000);
            entity.Property(item => item.Status).HasMaxLength(30).IsRequired();
            entity.HasOne<Project>().WithMany().HasForeignKey(item => item.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductionEpisode>().WithMany().HasForeignKey(item => item.ProductionEpisodeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.SubjectAssetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AgentTask>().WithMany().HasForeignKey(item => item.RequestedByTaskId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ValidationRun>(entity =>
        {
            entity.ToTable("ValidationRuns");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ProjectId, item.SubjectAssetId, item.StartedAtUtc });
            entity.Property(item => item.ValidatorSet).HasMaxLength(100).IsRequired();
            entity.Property(item => item.ValidatorVersion).HasMaxLength(40).IsRequired();
            entity.Property(item => item.Status).HasMaxLength(30).IsRequired();
            entity.HasOne<Project>().WithMany().HasForeignKey(item => item.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductionEpisode>().WithMany().HasForeignKey(item => item.ProductionEpisodeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.SubjectAssetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AgentTask>().WithMany().HasForeignKey(item => item.TriggeredByTaskId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ValidationResult>(entity =>
        {
            entity.ToTable("ValidationResults");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ValidationRunId, item.GateId, item.SubjectId }).IsUnique();
            entity.HasIndex(item => new { item.ValidationRunId, item.Status, item.Severity });
            entity.Property(item => item.GateId).HasMaxLength(100).IsRequired();
            entity.Property(item => item.Severity).HasMaxLength(20).IsRequired();
            entity.Property(item => item.Status).HasMaxLength(30).IsRequired();
            entity.Property(item => item.Message).HasMaxLength(2000).IsRequired();
            entity.Property(item => item.SubjectType).HasMaxLength(50);
            entity.Property(item => item.SuggestedAction).HasMaxLength(1000);
            entity.HasOne<ValidationRun>().WithMany().HasForeignKey(item => item.ValidationRunId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureAgentWorkflow(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AgentTask>(entity =>
        {
            entity.ToTable("AgentTasks", table =>
            {
                table.HasCheckConstraint("CK_AgentTasks_ProgressCompleted", "ProgressCompleted >= 0");
                table.HasCheckConstraint("CK_AgentTasks_ProgressTotal", "ProgressTotal IS NULL OR ProgressTotal >= 0");
            });
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ProjectId, item.Status, item.CreatedAtUtc });
            entity.HasIndex(item => item.ParentTaskId);
            entity.Property(item => item.Intent).HasMaxLength(20000).IsRequired();
            entity.Property(item => item.TaskType).HasMaxLength(100).IsRequired();
            entity.Property(item => item.Status).HasMaxLength(30).IsRequired();
            entity.Property(item => item.CurrentStep).HasMaxLength(100);
            entity.Property(item => item.RequestedBy).HasMaxLength(100).IsRequired();
            entity.Property(item => item.Model).HasMaxLength(100);
            entity.Property(item => item.LastError).HasMaxLength(4000);
            entity.HasOne<Project>().WithMany().HasForeignKey(item => item.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductionEpisode>().WithMany().HasForeignKey(item => item.ProductionEpisodeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AgentTask>().WithMany().HasForeignKey(item => item.ParentTaskId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AgentTaskItem>(entity =>
        {
            entity.ToTable("AgentTaskItems", table =>
            {
                table.HasCheckConstraint("CK_AgentTaskItems_Ordinal", "Ordinal >= 0");
                table.HasCheckConstraint("CK_AgentTaskItems_Attempt", "Attempt >= 0");
            });
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.TaskId, item.Ordinal }).IsUnique();
            entity.HasIndex(item => new { item.TaskId, item.Status });
            entity.Property(item => item.ObjectType).HasMaxLength(50).IsRequired();
            entity.Property(item => item.Action).HasMaxLength(100).IsRequired();
            entity.Property(item => item.Status).HasMaxLength(30).IsRequired();
            entity.Property(item => item.ErrorCode).HasMaxLength(100);
            entity.Property(item => item.ErrorDetail).HasMaxLength(4000);
            entity.HasOne<AgentTask>().WithMany().HasForeignKey(item => item.TaskId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Project>().WithMany().HasForeignKey(item => item.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductionEpisode>().WithMany().HasForeignKey(item => item.ProductionEpisodeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AgentTaskEvent>(entity =>
        {
            entity.ToTable("AgentTaskEvents");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.TaskId, item.Sequence }).IsUnique();
            entity.Property(item => item.EventType).HasMaxLength(50).IsRequired();
            entity.Property(item => item.Stage).HasMaxLength(100);
            entity.Property(item => item.Message).HasMaxLength(2000).IsRequired();
            entity.HasOne<AgentTask>().WithMany().HasForeignKey(item => item.TaskId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentTaskOutput>(entity =>
        {
            entity.ToTable("AgentTaskOutputs");
            entity.HasKey(item => new { item.TaskId, item.AssetId, item.Role });
            entity.Property(item => item.Role).HasMaxLength(50);
            entity.HasOne<AgentTask>().WithMany().HasForeignKey(item => item.TaskId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AgentTaskItem>().WithMany().HasForeignKey(item => item.TaskItemId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.AssetId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureProduction(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProductionRun>(entity =>
        {
            entity.ToTable("ProductionRuns");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ProjectId, item.CreatedAtUtc });
            entity.HasIndex(item => new { item.ProjectId, item.ProductionEpisodeId, item.CreatedAtUtc });
            entity.HasIndex(item => new { item.RunType, item.Status, item.CreatedAtUtc });
            entity.HasIndex(item => new { item.Status, item.CreatedAtUtc });
            entity.Property(item => item.RunType).HasMaxLength(30).HasDefaultValue("shot-frames").IsRequired();
            entity.Property(item => item.Status).HasMaxLength(30).IsRequired();
            entity.Property(item => item.CurrentStage).HasMaxLength(30).IsRequired();
            entity.Property(item => item.OriginalInstruction).HasMaxLength(20000).IsRequired();
            entity.Property(item => item.LastError).HasMaxLength(4000);
            entity.Property(item => item.LeaseOwner).HasMaxLength(200);
            entity.HasOne<Project>().WithMany().HasForeignKey(item => item.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductionEpisode>().WithMany().HasForeignKey(item => item.ProductionEpisodeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.ScriptPackageAssetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.CreativeSettingsAssetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.ReferenceBoardAssetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ValidationRun>().WithMany().HasForeignKey(item => item.PreflightValidationRunId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AgentTask>().WithMany().HasForeignKey(item => item.RequestedByTaskId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.FinalAssetId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductionRunItem>(entity =>
        {
            entity.ToTable("ProductionRunItems", table =>
                table.HasCheckConstraint("CK_ProductionRunItems_Attempt", "Attempt >= 0"));
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.RunId, item.ShotResourceId, item.Stage }).IsUnique();
            entity.Property(item => item.ShotName).HasMaxLength(260).IsRequired();
            entity.Property(item => item.Stage).HasMaxLength(30).IsRequired();
            entity.Property(item => item.Status).HasMaxLength(30).IsRequired();
            entity.Property(item => item.InputFingerprint).HasMaxLength(128);
            entity.Property(item => item.ExternalJobId).HasMaxLength(200);
            entity.Property(item => item.ErrorCode).HasMaxLength(100);
            entity.Property(item => item.ErrorDetail).HasMaxLength(4000);
            entity.HasOne<ProductionRun>().WithMany().HasForeignKey(item => item.RunId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Project>().WithMany().HasForeignKey(item => item.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductionEpisode>().WithMany().HasForeignKey(item => item.ProductionEpisodeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.ShotAssetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>().WithMany().HasForeignKey(item => item.OutputAssetId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureSystem(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FoundryConfiguration>(entity =>
        {
            entity.ToTable("FoundryConfigurations", table =>
                table.HasCheckConstraint("CK_FoundryConfigurations_Singleton", "Id = 1"));
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Endpoint).HasMaxLength(1000).IsRequired();
            entity.Property(item => item.Deployment).HasMaxLength(100).IsRequired();
            entity.Property(item => item.ProtectedApiKey).HasMaxLength(4000).IsRequired();
            entity.Property(item => item.ImageEndpoint).HasMaxLength(1000).IsRequired();
            entity.Property(item => item.ImageDeployment).HasMaxLength(100).IsRequired();
            entity.Property(item => item.ImageQuality).HasMaxLength(20).IsRequired();
            entity.Property(item => item.ProtectedImageApiKey).HasMaxLength(4000).IsRequired();
        });

        modelBuilder.Entity<ComfyUiConfiguration>(entity =>
        {
            entity.ToTable("ComfyUiConfigurations", table =>
            {
                table.HasCheckConstraint("CK_ComfyUiConfigurations_Singleton", "Id = 1");
                table.HasCheckConstraint("CK_ComfyUiConfigurations_MaxConcurrentJobs", "MaxConcurrentJobs > 0");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ConnectionMode).HasMaxLength(30).IsRequired();
            entity.Property(item => item.BaseUrl).HasMaxLength(1000).IsRequired();
            entity.Property(item => item.WorkflowProfile).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<SkillDefinition>(entity =>
        {
            entity.ToTable("SkillDefinitions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(100);
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(4000).IsRequired();
            entity.Property(item => item.Version).HasMaxLength(40).IsRequired();
            entity.Property(item => item.SourcePath).HasMaxLength(1000).IsRequired();
        });

        modelBuilder.Entity<CopilotConversation>(entity =>
        {
            entity.ToTable("CopilotConversations");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.ProjectId).IsUnique();
            entity.HasOne<Project>().WithMany().HasForeignKey(item => item.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CopilotMessage>(entity =>
        {
            entity.ToTable("CopilotMessages", table =>
                table.HasCheckConstraint("CK_CopilotMessages_Sequence", "Sequence > 0"));
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ConversationId, item.Sequence }).IsUnique();
            entity.Property(item => item.Role).HasMaxLength(20).IsRequired();
            entity.Property(item => item.Content).HasMaxLength(100000).IsRequired();
            entity.Property(item => item.Model).HasMaxLength(100);
            entity.HasOne<CopilotConversation>().WithMany().HasForeignKey(item => item.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}