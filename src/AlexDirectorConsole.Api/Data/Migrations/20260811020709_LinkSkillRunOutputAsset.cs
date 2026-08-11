using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class LinkSkillRunOutputAsset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OutputAssetId",
                table: "SkillRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SkillRuns_OutputAssetId",
                table: "SkillRuns",
                column: "OutputAssetId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SkillRuns_OutputAssetId",
                table: "SkillRuns");

            migrationBuilder.DropColumn(
                name: "OutputAssetId",
                table: "SkillRuns");
        }
    }
}
