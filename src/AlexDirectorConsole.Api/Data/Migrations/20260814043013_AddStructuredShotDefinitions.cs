using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredShotDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShotDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShotResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScriptResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SceneNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ShotNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationSeconds = table.Column<double>(type: "REAL", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShotDefinitions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShotDefinitions_ProjectId_ScriptResourceId_SceneNumber_ShotNumber",
                table: "ShotDefinitions",
                columns: new[] { "ProjectId", "ScriptResourceId", "SceneNumber", "ShotNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShotDefinitions_ProjectId_ShotResourceId",
                table: "ShotDefinitions",
                columns: new[] { "ProjectId", "ShotResourceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShotDefinitions");
        }
    }
}
