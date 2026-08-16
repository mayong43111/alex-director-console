using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.V2.Database.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialV2Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentTaskEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    DataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTaskEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentTaskItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionEpisodeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    ObjectType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ObjectResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    InputAssetIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputAssetIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ErrorDetail = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTaskItems", x => x.Id);
                    table.CheckConstraint("CK_AgentTaskItems_Attempt", "Attempt >= 0");
                    table.CheckConstraint("CK_AgentTaskItems_Ordinal", "Ordinal >= 0");
                });

            migrationBuilder.CreateTable(
                name: "AgentTaskOutputs",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TaskItemId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTaskOutputs", x => new { x.TaskId, x.AssetId, x.Role });
                    table.ForeignKey(
                        name: "FK_AgentTaskOutputs_AgentTaskItems_TaskItemId",
                        column: x => x.TaskItemId,
                        principalTable: "AgentTaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AgentTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionEpisodeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ParentTaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Intent = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
                    TaskType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ContextSnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    PlanJson = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CurrentStep = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ProgressCompleted = table.Column<int>(type: "INTEGER", nullable: false),
                    ProgressTotal = table.Column<int>(type: "INTEGER", nullable: true),
                    RequestedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RequestedByMessageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTasks", x => x.Id);
                    table.CheckConstraint("CK_AgentTasks_ProgressCompleted", "ProgressCompleted >= 0");
                    table.CheckConstraint("CK_AgentTasks_ProgressTotal", "ProgressTotal IS NULL OR ProgressTotal >= 0");
                    table.ForeignKey(
                        name: "FK_AgentTasks_AgentTasks_ParentTaskId",
                        column: x => x.ParentTaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetDependencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConsumerAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    IsRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetDependencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionEpisodeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentJson = table.Column<string>(type: "TEXT", nullable: true),
                    BlobKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: true),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    GenerationMetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedByTaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.Id);
                    table.CheckConstraint("CK_Assets_SchemaVersion", "SchemaVersion > 0");
                    table.CheckConstraint("CK_Assets_SizeBytes", "SizeBytes >= 0");
                    table.CheckConstraint("CK_Assets_Version", "Version > 0");
                    table.ForeignKey(
                        name: "FK_Assets_AgentTasks_CreatedByTaskId",
                        column: x => x.CreatedByTaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CurrentCreativeSettingsId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Projects_Assets_CurrentCreativeSettingsId",
                        column: x => x.CurrentCreativeSettingsId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionEpisodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TargetSeconds = table.Column<double>(type: "REAL", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionEpisodes", x => x.Id);
                    table.CheckConstraint("CK_ProductionEpisodes_EpisodeNumber", "EpisodeNumber > 0");
                    table.CheckConstraint("CK_ProductionEpisodes_TargetSeconds", "TargetSeconds IS NULL OR TargetSeconds > 0");
                    table.ForeignKey(
                        name: "FK_ProductionEpisodes_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResourceStates",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResourceType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CurrentAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApprovedAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LifecycleStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    IsStale = table.Column<bool>(type: "INTEGER", nullable: false),
                    StaleReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    StaleSinceUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceStates", x => new { x.ProjectId, x.ResourceId });
                    table.ForeignKey(
                        name: "FK_ResourceStates_Assets_ApprovedAssetId",
                        column: x => x.ApprovedAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResourceStates_Assets_CurrentAssetId",
                        column: x => x.CurrentAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResourceStates_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DirectorDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionEpisodeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecisionType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SubjectType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SubjectResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubjectAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Question = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    OptionsJson = table.Column<string>(type: "TEXT", nullable: false),
                    SelectedOption = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DecisionText = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    RequestedByTaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectorDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DirectorDecisions_AgentTasks_RequestedByTaskId",
                        column: x => x.RequestedByTaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DirectorDecisions_Assets_SubjectAssetId",
                        column: x => x.SubjectAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DirectorDecisions_ProductionEpisodes_ProductionEpisodeId",
                        column: x => x.ProductionEpisodeId,
                        principalTable: "ProductionEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DirectorDecisions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShotAssetLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionEpisodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShotResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShotAssetLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShotAssetLinks_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShotAssetLinks_ProductionEpisodes_ProductionEpisodeId",
                        column: x => x.ProductionEpisodeId,
                        principalTable: "ProductionEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShotAssetLinks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShotBeatClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionEpisodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScriptPackageAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BeatId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShotAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShotResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrdinalInShot = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShotBeatClaims", x => x.Id);
                    table.CheckConstraint("CK_ShotBeatClaims_Ordinal", "OrdinalInShot >= 0");
                    table.ForeignKey(
                        name: "FK_ShotBeatClaims_Assets_ScriptPackageAssetId",
                        column: x => x.ScriptPackageAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShotBeatClaims_Assets_ShotAssetId",
                        column: x => x.ShotAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShotBeatClaims_ProductionEpisodes_ProductionEpisodeId",
                        column: x => x.ProductionEpisodeId,
                        principalTable: "ProductionEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShotBeatClaims_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShotDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionEpisodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShotResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShotAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScriptPackageAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SceneId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SceneNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ShotNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationSeconds = table.Column<double>(type: "REAL", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShotDefinitions", x => x.Id);
                    table.CheckConstraint("CK_ShotDefinitions_DurationSeconds", "DurationSeconds > 0");
                    table.ForeignKey(
                        name: "FK_ShotDefinitions_Assets_ScriptPackageAssetId",
                        column: x => x.ScriptPackageAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShotDefinitions_Assets_ShotAssetId",
                        column: x => x.ShotAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShotDefinitions_ProductionEpisodes_ProductionEpisodeId",
                        column: x => x.ProductionEpisodeId,
                        principalTable: "ProductionEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShotDefinitions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ValidationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionEpisodeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubjectAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ValidatorSet = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ValidatorVersion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    TriggeredByTaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ValidationRuns_AgentTasks_TriggeredByTaskId",
                        column: x => x.TriggeredByTaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValidationRuns_Assets_SubjectAssetId",
                        column: x => x.SubjectAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValidationRuns_ProductionEpisodes_ProductionEpisodeId",
                        column: x => x.ProductionEpisodeId,
                        principalTable: "ProductionEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValidationRuns_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VisualReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImageAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ReviewStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    InheritsFromAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedByDecisionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisualReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisualReferences_Assets_ImageAssetId",
                        column: x => x.ImageAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VisualReferences_Assets_InheritsFromAssetId",
                        column: x => x.InheritsFromAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VisualReferences_DirectorDecisions_ApprovedByDecisionId",
                        column: x => x.ApprovedByDecisionId,
                        principalTable: "DirectorDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VisualReferences_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionEpisodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScriptPackageAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreativeSettingsAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReferenceBoardAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PreflightValidationRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedByTaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CurrentStage = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    SpecJson = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalInstruction = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    FinalAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LeaseOwner = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionRuns_AgentTasks_RequestedByTaskId",
                        column: x => x.RequestedByTaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionRuns_Assets_CreativeSettingsAssetId",
                        column: x => x.CreativeSettingsAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionRuns_Assets_FinalAssetId",
                        column: x => x.FinalAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionRuns_Assets_ReferenceBoardAssetId",
                        column: x => x.ReferenceBoardAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionRuns_Assets_ScriptPackageAssetId",
                        column: x => x.ScriptPackageAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionRuns_ProductionEpisodes_ProductionEpisodeId",
                        column: x => x.ProductionEpisodeId,
                        principalTable: "ProductionEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionRuns_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionRuns_ValidationRuns_PreflightValidationRunId",
                        column: x => x.PreflightValidationRunId,
                        principalTable: "ValidationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ValidationResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ValidationRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GateId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    SubjectType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReferencesJson = table.Column<string>(type: "TEXT", nullable: false),
                    SuggestedAction = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ValidationResults_ValidationRuns_ValidationRunId",
                        column: x => x.ValidationRunId,
                        principalTable: "ValidationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductionRunItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionEpisodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShotResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShotAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShotName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    InputAssetIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    InputFingerprint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    OutputAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CostJson = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ErrorDetail = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionRunItems", x => x.Id);
                    table.CheckConstraint("CK_ProductionRunItems_Attempt", "Attempt >= 0");
                    table.ForeignKey(
                        name: "FK_ProductionRunItems_Assets_OutputAssetId",
                        column: x => x.OutputAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionRunItems_Assets_ShotAssetId",
                        column: x => x.ShotAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionRunItems_ProductionEpisodes_ProductionEpisodeId",
                        column: x => x.ProductionEpisodeId,
                        principalTable: "ProductionEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionRunItems_ProductionRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ProductionRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductionRunItems_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskEvents_TaskId_Sequence",
                table: "AgentTaskEvents",
                columns: new[] { "TaskId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskItems_ProductionEpisodeId",
                table: "AgentTaskItems",
                column: "ProductionEpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskItems_ProjectId",
                table: "AgentTaskItems",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskItems_TaskId_Ordinal",
                table: "AgentTaskItems",
                columns: new[] { "TaskId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskItems_TaskId_Status",
                table: "AgentTaskItems",
                columns: new[] { "TaskId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskOutputs_AssetId",
                table: "AgentTaskOutputs",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskOutputs_TaskItemId",
                table: "AgentTaskOutputs",
                column: "TaskItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_ParentTaskId",
                table: "AgentTasks",
                column: "ParentTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_ProductionEpisodeId",
                table: "AgentTasks",
                column: "ProductionEpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_ProjectId_Status_CreatedAtUtc",
                table: "AgentTasks",
                columns: new[] { "ProjectId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetDependencies_ConsumerAssetId_SourceAssetId_Role",
                table: "AssetDependencies",
                columns: new[] { "ConsumerAssetId", "SourceAssetId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetDependencies_ProjectId_ConsumerAssetId",
                table: "AssetDependencies",
                columns: new[] { "ProjectId", "ConsumerAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetDependencies_ProjectId_SourceAssetId",
                table: "AssetDependencies",
                columns: new[] { "ProjectId", "SourceAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetDependencies_SourceAssetId",
                table: "AssetDependencies",
                column: "SourceAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_BlobKey",
                table: "Assets",
                column: "BlobKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_CreatedByTaskId",
                table: "Assets",
                column: "CreatedByTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_ProductionEpisodeId",
                table: "Assets",
                column: "ProductionEpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_ProjectId_Number_Version",
                table: "Assets",
                columns: new[] { "ProjectId", "Number", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_ProjectId_ResourceId_Version",
                table: "Assets",
                columns: new[] { "ProjectId", "ResourceId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_ProjectId_Type",
                table: "Assets",
                columns: new[] { "ProjectId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_DirectorDecisions_ProductionEpisodeId",
                table: "DirectorDecisions",
                column: "ProductionEpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_DirectorDecisions_ProjectId_Status_RequestedAtUtc",
                table: "DirectorDecisions",
                columns: new[] { "ProjectId", "Status", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DirectorDecisions_RequestedByTaskId",
                table: "DirectorDecisions",
                column: "RequestedByTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_DirectorDecisions_SubjectAssetId",
                table: "DirectorDecisions",
                column: "SubjectAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionEpisodes_ProjectId_EpisodeNumber",
                table: "ProductionEpisodes",
                columns: new[] { "ProjectId", "EpisodeNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionEpisodes_ProjectId_Status",
                table: "ProductionEpisodes",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRunItems_OutputAssetId",
                table: "ProductionRunItems",
                column: "OutputAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRunItems_ProductionEpisodeId",
                table: "ProductionRunItems",
                column: "ProductionEpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRunItems_ProjectId",
                table: "ProductionRunItems",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRunItems_RunId_ShotResourceId_Stage",
                table: "ProductionRunItems",
                columns: new[] { "RunId", "ShotResourceId", "Stage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRunItems_ShotAssetId",
                table: "ProductionRunItems",
                column: "ShotAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_CreativeSettingsAssetId",
                table: "ProductionRuns",
                column: "CreativeSettingsAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_FinalAssetId",
                table: "ProductionRuns",
                column: "FinalAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_PreflightValidationRunId",
                table: "ProductionRuns",
                column: "PreflightValidationRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_ProductionEpisodeId",
                table: "ProductionRuns",
                column: "ProductionEpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_ProjectId_CreatedAtUtc",
                table: "ProductionRuns",
                columns: new[] { "ProjectId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_ProjectId_ProductionEpisodeId_CreatedAtUtc",
                table: "ProductionRuns",
                columns: new[] { "ProjectId", "ProductionEpisodeId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_ReferenceBoardAssetId",
                table: "ProductionRuns",
                column: "ReferenceBoardAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_RequestedByTaskId",
                table: "ProductionRuns",
                column: "RequestedByTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_ScriptPackageAssetId",
                table: "ProductionRuns",
                column: "ScriptPackageAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_Status_CreatedAtUtc",
                table: "ProductionRuns",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_CurrentCreativeSettingsId",
                table: "Projects",
                column: "CurrentCreativeSettingsId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceStates_ApprovedAssetId",
                table: "ResourceStates",
                column: "ApprovedAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceStates_CurrentAssetId",
                table: "ResourceStates",
                column: "CurrentAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceStates_ProjectId_CurrentAssetId",
                table: "ResourceStates",
                columns: new[] { "ProjectId", "CurrentAssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceStates_ProjectId_ResourceType_LifecycleStatus",
                table: "ResourceStates",
                columns: new[] { "ProjectId", "ResourceType", "LifecycleStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_ShotAssetLinks_AssetId",
                table: "ShotAssetLinks",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ShotAssetLinks_ProductionEpisodeId",
                table: "ShotAssetLinks",
                column: "ProductionEpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShotAssetLinks_ProjectId_ShotResourceId",
                table: "ShotAssetLinks",
                columns: new[] { "ProjectId", "ShotResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ShotAssetLinks_ProjectId_ShotResourceId_AssetId_Role_SubjectId",
                table: "ShotAssetLinks",
                columns: new[] { "ProjectId", "ShotResourceId", "AssetId", "Role", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShotBeatClaims_ProductionEpisodeId",
                table: "ShotBeatClaims",
                column: "ProductionEpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShotBeatClaims_ProjectId",
                table: "ShotBeatClaims",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ShotBeatClaims_ScriptPackageAssetId_BeatId",
                table: "ShotBeatClaims",
                columns: new[] { "ScriptPackageAssetId", "BeatId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShotBeatClaims_ShotAssetId_BeatId",
                table: "ShotBeatClaims",
                columns: new[] { "ShotAssetId", "BeatId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShotDefinitions_ProductionEpisodeId",
                table: "ShotDefinitions",
                column: "ProductionEpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShotDefinitions_ProjectId_ScriptPackageAssetId_ProductionEpisodeId_SceneNumber_ShotNumber",
                table: "ShotDefinitions",
                columns: new[] { "ProjectId", "ScriptPackageAssetId", "ProductionEpisodeId", "SceneNumber", "ShotNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShotDefinitions_ProjectId_ShotResourceId",
                table: "ShotDefinitions",
                columns: new[] { "ProjectId", "ShotResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShotDefinitions_ScriptPackageAssetId",
                table: "ShotDefinitions",
                column: "ScriptPackageAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ShotDefinitions_ShotAssetId",
                table: "ShotDefinitions",
                column: "ShotAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ValidationResults_ValidationRunId_GateId_SubjectId",
                table: "ValidationResults",
                columns: new[] { "ValidationRunId", "GateId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ValidationResults_ValidationRunId_Status_Severity",
                table: "ValidationResults",
                columns: new[] { "ValidationRunId", "Status", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationRuns_ProductionEpisodeId",
                table: "ValidationRuns",
                column: "ProductionEpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ValidationRuns_ProjectId_SubjectAssetId_StartedAtUtc",
                table: "ValidationRuns",
                columns: new[] { "ProjectId", "SubjectAssetId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationRuns_SubjectAssetId",
                table: "ValidationRuns",
                column: "SubjectAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ValidationRuns_TriggeredByTaskId",
                table: "ValidationRuns",
                column: "TriggeredByTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_VisualReferences_ApprovedByDecisionId",
                table: "VisualReferences",
                column: "ApprovedByDecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_VisualReferences_ImageAssetId_SubjectResourceId_Purpose",
                table: "VisualReferences",
                columns: new[] { "ImageAssetId", "SubjectResourceId", "Purpose" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VisualReferences_InheritsFromAssetId",
                table: "VisualReferences",
                column: "InheritsFromAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_VisualReferences_ProjectId_SubjectResourceId_ReviewStatus",
                table: "VisualReferences",
                columns: new[] { "ProjectId", "SubjectResourceId", "ReviewStatus" });

            migrationBuilder.AddForeignKey(
                name: "FK_AgentTaskEvents_AgentTasks_TaskId",
                table: "AgentTaskEvents",
                column: "TaskId",
                principalTable: "AgentTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentTaskItems_AgentTasks_TaskId",
                table: "AgentTaskItems",
                column: "TaskId",
                principalTable: "AgentTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentTaskItems_ProductionEpisodes_ProductionEpisodeId",
                table: "AgentTaskItems",
                column: "ProductionEpisodeId",
                principalTable: "ProductionEpisodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentTaskItems_Projects_ProjectId",
                table: "AgentTaskItems",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentTaskOutputs_AgentTasks_TaskId",
                table: "AgentTaskOutputs",
                column: "TaskId",
                principalTable: "AgentTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentTaskOutputs_Assets_AssetId",
                table: "AgentTaskOutputs",
                column: "AssetId",
                principalTable: "Assets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentTasks_ProductionEpisodes_ProductionEpisodeId",
                table: "AgentTasks",
                column: "ProductionEpisodeId",
                principalTable: "ProductionEpisodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentTasks_Projects_ProjectId",
                table: "AgentTasks",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AssetDependencies_Assets_ConsumerAssetId",
                table: "AssetDependencies",
                column: "ConsumerAssetId",
                principalTable: "Assets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AssetDependencies_Assets_SourceAssetId",
                table: "AssetDependencies",
                column: "SourceAssetId",
                principalTable: "Assets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AssetDependencies_Projects_ProjectId",
                table: "AssetDependencies",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_ProductionEpisodes_ProductionEpisodeId",
                table: "Assets",
                column: "ProductionEpisodeId",
                principalTable: "ProductionEpisodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_Projects_ProjectId",
                table: "Assets",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_AgentTasks_CreatedByTaskId",
                table: "Assets");

            migrationBuilder.DropForeignKey(
                name: "FK_Assets_ProductionEpisodes_ProductionEpisodeId",
                table: "Assets");

            migrationBuilder.DropForeignKey(
                name: "FK_Assets_Projects_ProjectId",
                table: "Assets");

            migrationBuilder.DropTable(
                name: "AgentTaskEvents");

            migrationBuilder.DropTable(
                name: "AgentTaskOutputs");

            migrationBuilder.DropTable(
                name: "AssetDependencies");

            migrationBuilder.DropTable(
                name: "ProductionRunItems");

            migrationBuilder.DropTable(
                name: "ResourceStates");

            migrationBuilder.DropTable(
                name: "ShotAssetLinks");

            migrationBuilder.DropTable(
                name: "ShotBeatClaims");

            migrationBuilder.DropTable(
                name: "ShotDefinitions");

            migrationBuilder.DropTable(
                name: "ValidationResults");

            migrationBuilder.DropTable(
                name: "VisualReferences");

            migrationBuilder.DropTable(
                name: "AgentTaskItems");

            migrationBuilder.DropTable(
                name: "ProductionRuns");

            migrationBuilder.DropTable(
                name: "DirectorDecisions");

            migrationBuilder.DropTable(
                name: "ValidationRuns");

            migrationBuilder.DropTable(
                name: "AgentTasks");

            migrationBuilder.DropTable(
                name: "ProductionEpisodes");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "Assets");
        }
    }
}
