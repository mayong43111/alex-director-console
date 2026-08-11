using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShotAssetLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShotAssetLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShotResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShotAssetLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShotAssetLinks_ProjectId_ShotResourceId",
                table: "ShotAssetLinks",
                columns: new[] { "ProjectId", "ShotResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ShotAssetLinks_ShotResourceId_AssetId_Role",
                table: "ShotAssetLinks",
                columns: new[] { "ShotResourceId", "AssetId", "Role" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShotAssetLinks");
        }
    }
}
