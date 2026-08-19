using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.V2.Database.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddComfyUiAndVideoProduction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RunType",
                table: "ProductionRuns",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "shot-frames");

            migrationBuilder.AddColumn<string>(
                name: "ExternalJobId",
                table: "ProductionRunItems",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ComfyUiConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConnectionMode = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    WorkflowProfile = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MaxConcurrentJobs = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComfyUiConfigurations", x => x.Id);
                    table.CheckConstraint("CK_ComfyUiConfigurations_MaxConcurrentJobs", "MaxConcurrentJobs > 0");
                    table.CheckConstraint("CK_ComfyUiConfigurations_Singleton", "Id = 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_RunType_Status_CreatedAtUtc",
                table: "ProductionRuns",
                columns: new[] { "RunType", "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComfyUiConfigurations");

            migrationBuilder.DropIndex(
                name: "IX_ProductionRuns_RunType_Status_CreatedAtUtc",
                table: "ProductionRuns");

            migrationBuilder.DropColumn(
                name: "RunType",
                table: "ProductionRuns");

            migrationBuilder.DropColumn(
                name: "ExternalJobId",
                table: "ProductionRunItems");
        }
    }
}
