using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductionRunItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShotResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShotAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShotName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    InputFingerprint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    OutputAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ErrorDetail = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionRunItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CurrentStage = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    OriginalInstruction = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
                    SpecJson = table.Column<string>(type: "TEXT", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    DryRun = table.Column<bool>(type: "INTEGER", nullable: false),
                    KeepVmRunning = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRunItems_RunId_ShotResourceId_Stage",
                table: "ProductionRunItems",
                columns: new[] { "RunId", "ShotResourceId", "Stage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRunItems_RunId_Status",
                table: "ProductionRunItems",
                columns: new[] { "RunId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_ProjectId_CreatedAtUtc",
                table: "ProductionRuns",
                columns: new[] { "ProjectId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_Status_CreatedAtUtc",
                table: "ProductionRuns",
                columns: new[] { "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductionRunItems");

            migrationBuilder.DropTable(
                name: "ProductionRuns");
        }
    }
}
