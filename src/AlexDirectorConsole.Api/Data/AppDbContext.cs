using AlexDirectorConsole.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
    public DbSet<SkillDefinition> SkillDefinitions => Set<SkillDefinition>();
    public DbSet<SkillRun> SkillRuns => Set<SkillRun>();
    public DbSet<ShotAssetLink> ShotAssetLinks => Set<ShotAssetLink>();
    public DbSet<ProjectRuntimeConfiguration> ProjectRuntimeConfigurations => Set<ProjectRuntimeConfiguration>();
    public DbSet<GlobalFoundryConfiguration> GlobalFoundryConfigurations => Set<GlobalFoundryConfiguration>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var addedAssets = ChangeTracker.Entries<Asset>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToList();
        foreach (var asset in addedAssets)
        {
            if (asset.ResourceId == Guid.Empty)
            {
                asset.ResourceId = asset.Id;
            }
            if (asset.Version < 1)
            {
                asset.Version = 1;
            }
        }

        foreach (var projectAssets in addedAssets.GroupBy(asset => asset.ProjectId))
        {
            var resourceIds = projectAssets.Select(asset => asset.ResourceId).Distinct().ToArray();
            var existingNumbers = await Assets
                .AsNoTracking()
                .Where(asset => asset.ProjectId == projectAssets.Key
                    && resourceIds.Contains(asset.ResourceId))
                .GroupBy(asset => asset.ResourceId)
                .ToDictionaryAsync(
                    group => group.Key,
                    group => group.Select(asset => asset.Number).First(),
                    cancellationToken);
            var nextNumber = await Assets
                .Where(asset => asset.ProjectId == projectAssets.Key)
                .Select(asset => (int?)asset.Number)
                .MaxAsync(cancellationToken) ?? 0;

            foreach (var resourceAssets in projectAssets.GroupBy(asset => asset.ResourceId))
            {
                if (!existingNumbers.TryGetValue(resourceAssets.Key, out var number))
                {
                    number = ++nextNumber;
                }
                foreach (var asset in resourceAssets)
                {
                    asset.Number = number;
                }
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("Projects");
            entity.HasKey(project => project.Id);
            entity.Property(project => project.Name)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(project => project.Description).HasMaxLength(4000);
            entity.Property(project => project.FormatPreset).HasMaxLength(40).IsRequired();
            entity.Property(project => project.PreviewResolution).HasMaxLength(40).IsRequired();
            entity.Property(project => project.LanguageModel).HasMaxLength(100).IsRequired();
            entity.Property(project => project.ImageModel).HasMaxLength(100).IsRequired();
            entity.Property(project => project.VideoModel).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<Asset>(entity =>
        {
            entity.ToTable("Assets");
            entity.HasKey(asset => asset.Id);
            entity.HasIndex(asset => new { asset.ProjectId, asset.Type });
            entity.HasIndex(asset => new { asset.ProjectId, asset.Number, asset.Version }).IsUnique();
            entity.HasIndex(asset => new { asset.ProjectId, asset.ResourceId, asset.Version }).IsUnique();
            entity.HasIndex(asset => asset.BlobKey).IsUnique();
            entity.Property(asset => asset.Type).HasMaxLength(50).IsRequired();
            entity.Property(asset => asset.Name).HasMaxLength(260).IsRequired();
            entity.Property(asset => asset.BlobKey).HasMaxLength(500).IsRequired();
            entity.Property(asset => asset.FileName).HasMaxLength(260).IsRequired();
            entity.Property(asset => asset.ContentType).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<ConversationMessage>(entity =>
        {
            entity.ToTable("ConversationMessages");
            entity.HasKey(message => message.Id);
            entity.HasIndex(message => new { message.ProjectId, message.CreatedAtUtc });
            entity.Property(message => message.Role).HasMaxLength(20).IsRequired();
            entity.Property(message => message.Content).IsRequired();
            entity.Property(message => message.Model).HasMaxLength(100).IsRequired();
            entity.Property(message => message.GeneratedAssetIdsJson);
        });

        modelBuilder.Entity<SkillDefinition>(entity =>
        {
            entity.ToTable("SkillDefinitions");
            entity.HasKey(skill => skill.Id);
            entity.Property(skill => skill.Id).HasMaxLength(100);
            entity.Property(skill => skill.Name).HasMaxLength(120).IsRequired();
            entity.Property(skill => skill.Description).HasMaxLength(500).IsRequired();
            entity.Property(skill => skill.Version).HasMaxLength(40).IsRequired();
        });

        modelBuilder.Entity<SkillRun>(entity =>
        {
            entity.ToTable("SkillRuns");
            entity.HasKey(run => run.Id);
            entity.HasIndex(run => new { run.ProjectId, run.StartedAtUtc });
            entity.HasIndex(run => run.OutputAssetId).IsUnique();
            entity.Property(run => run.SkillId).HasMaxLength(100).IsRequired();
            entity.Property(run => run.Status).HasMaxLength(30).IsRequired();
            entity.Property(run => run.DirectorInstruction).HasMaxLength(20000).IsRequired();
            entity.Property(run => run.Model).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<ShotAssetLink>(entity =>
        {
            entity.ToTable("ShotAssetLinks");
            entity.HasKey(link => link.Id);
            entity.HasIndex(link => new { link.ProjectId, link.ShotResourceId });
            entity.HasIndex(link => new { link.ProjectId, link.ShotResourceId, link.AssetId, link.Role }).IsUnique();
            entity.Property(link => link.Role).HasMaxLength(40).IsRequired();
        });

        modelBuilder.Entity<ProjectRuntimeConfiguration>(entity =>
        {
            entity.ToTable("ProjectRuntimeConfigurations");
            entity.HasKey(configuration => configuration.ProjectId);
            entity.HasIndex(configuration => configuration.LocalProxyPort)
                .IsUnique()
                .HasFilter("\"ProjectId\" <> '00000000-0000-0000-0000-000000000000'");
            entity.Property(configuration => configuration.ProjectId).ValueGeneratedNever();
            entity.Property(configuration => configuration.VmHost).HasMaxLength(260).UseCollation("NOCASE").IsRequired();
            entity.Property(configuration => configuration.VmUsername).HasMaxLength(100).IsRequired();
            entity.Property(configuration => configuration.SshPrivateKeyPath).HasMaxLength(500).IsRequired();
            entity.Property(configuration => configuration.ComfyUiPath).HasMaxLength(500).IsRequired();
            entity.Property(configuration => configuration.ComfyUiPythonPath).HasMaxLength(500).IsRequired();
            entity.Property(configuration => configuration.WorkflowDirectory).HasMaxLength(500).IsRequired();
            entity.Property(configuration => configuration.OutputDirectory).HasMaxLength(500).IsRequired();
        });

        modelBuilder.Entity<GlobalFoundryConfiguration>(entity =>
        {
            entity.ToTable("GlobalFoundryConfigurations");
            entity.HasKey(configuration => configuration.Id);
            entity.Property(configuration => configuration.OpenAiEndpoint).HasMaxLength(500).IsRequired();
            entity.Property(configuration => configuration.OpenAiDeployment).HasMaxLength(100).IsRequired();
            entity.Property(configuration => configuration.ProtectedOpenAiApiKey).HasMaxLength(4000).IsRequired();
            entity.Property(configuration => configuration.ImageEndpoint).HasMaxLength(500).IsRequired();
            entity.Property(configuration => configuration.ImageDeployment).HasMaxLength(100).IsRequired();
            entity.Property(configuration => configuration.ImageApiVersion).HasMaxLength(100).IsRequired();
            entity.Property(configuration => configuration.ImageQuality).HasMaxLength(20).IsRequired();
            entity.Property(configuration => configuration.ProtectedImageApiKey).HasMaxLength(4000).IsRequired();
            entity.Property(configuration => configuration.SpeechEndpoint).HasMaxLength(500).IsRequired();
            entity.Property(configuration => configuration.SpeechDeployment).HasMaxLength(100).IsRequired();
            entity.Property(configuration => configuration.SpeechApiVersion).HasMaxLength(100).IsRequired();
            entity.Property(configuration => configuration.ProtectedSpeechApiKey).HasMaxLength(4000).IsRequired();
        });
    }
}