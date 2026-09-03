using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.V2.Database.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVoiceTraining : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanExport",
                table: "VoicePackages",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "UsagePolicy",
                table: "VoicePackages",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "licensed");

            migrationBuilder.AddColumn<Guid>(
                name: "VoiceTrainingJobId",
                table: "VoicePackages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VoiceTrainingJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TrainingMode = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Engine = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    BaseModelVersion = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Dialect = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SpeakingStyle = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    DefaultSpeed = table.Column<double>(type: "REAL", nullable: false),
                    SourceDescription = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    UsagePolicy = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CanExport = table.Column<bool>(type: "INTEGER", nullable: false),
                    RightsConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ProgressPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalJobId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    GptWeightsPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SoVitsWeightsPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoiceTrainingJobs", x => x.Id);
                    table.CheckConstraint("CK_VoiceTrainingJobs_DefaultSpeed", "DefaultSpeed >= 0.5 AND DefaultSpeed <= 2.0");
                    table.CheckConstraint("CK_VoiceTrainingJobs_ProgressPercent", "ProgressPercent >= 0 AND ProgressPercent <= 100");
                });

            migrationBuilder.CreateTable(
                name: "VoiceTrainingSamples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoiceTrainingJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AudioContent = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Transcript = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    DurationSeconds = table.Column<double>(type: "REAL", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoiceTrainingSamples", x => x.Id);
                    table.CheckConstraint("CK_VoiceTrainingSamples_DurationSeconds", "DurationSeconds > 0");
                    table.ForeignKey(
                        name: "FK_VoiceTrainingSamples_VoiceTrainingJobs_VoiceTrainingJobId",
                        column: x => x.VoiceTrainingJobId,
                        principalTable: "VoiceTrainingJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoicePackages_VoiceTrainingJobId",
                table: "VoicePackages",
                column: "VoiceTrainingJobId");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceTrainingJobs_Status_UpdatedAtUtc",
                table: "VoiceTrainingJobs",
                columns: new[] { "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VoiceTrainingSamples_VoiceTrainingJobId_SortOrder",
                table: "VoiceTrainingSamples",
                columns: new[] { "VoiceTrainingJobId", "SortOrder" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_VoicePackages_VoiceTrainingJobs_VoiceTrainingJobId",
                table: "VoicePackages",
                column: "VoiceTrainingJobId",
                principalTable: "VoiceTrainingJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VoicePackages_VoiceTrainingJobs_VoiceTrainingJobId",
                table: "VoicePackages");

            migrationBuilder.DropTable(
                name: "VoiceTrainingSamples");

            migrationBuilder.DropTable(
                name: "VoiceTrainingJobs");

            migrationBuilder.DropIndex(
                name: "IX_VoicePackages_VoiceTrainingJobId",
                table: "VoicePackages");

            migrationBuilder.DropColumn(
                name: "CanExport",
                table: "VoicePackages");

            migrationBuilder.DropColumn(
                name: "UsagePolicy",
                table: "VoicePackages");

            migrationBuilder.DropColumn(
                name: "VoiceTrainingJobId",
                table: "VoicePackages");
        }
    }
}
