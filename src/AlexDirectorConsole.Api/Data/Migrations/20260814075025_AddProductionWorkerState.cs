using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionWorkerState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FinalAssetId",
                table: "ProductionRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAtUtc",
                table: "ProductionRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseOwner",
                table: "ProductionRuns",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VmStartedByRun",
                table: "ProductionRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FinalAssetId",
                table: "ProductionRuns");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAtUtc",
                table: "ProductionRuns");

            migrationBuilder.DropColumn(
                name: "LeaseOwner",
                table: "ProductionRuns");

            migrationBuilder.DropColumn(
                name: "VmStartedByRun",
                table: "ProductionRuns");
        }
    }
}
