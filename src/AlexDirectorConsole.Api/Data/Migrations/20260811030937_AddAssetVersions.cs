using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ResourceId",
                table: "Assets",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Assets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE Assets SET ResourceId = Id, Version = 1;");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_ResourceId_Version",
                table: "Assets",
                columns: new[] { "ResourceId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assets_ResourceId_Version",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "ResourceId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Assets");
        }
    }
}
