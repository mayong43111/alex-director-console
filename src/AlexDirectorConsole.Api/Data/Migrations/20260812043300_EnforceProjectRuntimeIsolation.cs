using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceProjectRuntimeIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShotAssetLinks_ShotResourceId_AssetId_Role",
                table: "ShotAssetLinks");

            migrationBuilder.DropIndex(
                name: "IX_Assets_ResourceId_Version",
                table: "Assets");

            migrationBuilder.AlterColumn<string>(
                name: "VmHost",
                table: "ProjectRuntimeConfigurations",
                type: "TEXT",
                maxLength: 260,
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 260);

            migrationBuilder.CreateIndex(
                name: "IX_ShotAssetLinks_ProjectId_ShotResourceId_AssetId_Role",
                table: "ShotAssetLinks",
                columns: new[] { "ProjectId", "ShotResourceId", "AssetId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRuntimeConfigurations_LocalProxyPort",
                table: "ProjectRuntimeConfigurations",
                column: "LocalProxyPort",
                unique: true,
                filter: "\"ProjectId\" <> '00000000-0000-0000-0000-000000000000'");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_ProjectId_ResourceId_Version",
                table: "Assets",
                columns: new[] { "ProjectId", "ResourceId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShotAssetLinks_ProjectId_ShotResourceId_AssetId_Role",
                table: "ShotAssetLinks");

            migrationBuilder.DropIndex(
                name: "IX_ProjectRuntimeConfigurations_LocalProxyPort",
                table: "ProjectRuntimeConfigurations");

            migrationBuilder.DropIndex(
                name: "IX_Assets_ProjectId_ResourceId_Version",
                table: "Assets");

            migrationBuilder.AlterColumn<string>(
                name: "VmHost",
                table: "ProjectRuntimeConfigurations",
                type: "TEXT",
                maxLength: 260,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 260,
                oldCollation: "NOCASE");

            migrationBuilder.CreateIndex(
                name: "IX_ShotAssetLinks_ShotResourceId_AssetId_Role",
                table: "ShotAssetLinks",
                columns: new[] { "ShotResourceId", "AssetId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_ResourceId_Version",
                table: "Assets",
                columns: new[] { "ResourceId", "Version" },
                unique: true);
        }
    }
}
