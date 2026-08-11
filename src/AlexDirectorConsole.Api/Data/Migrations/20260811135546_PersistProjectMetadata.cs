using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersistProjectMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Projects",
                type: "TEXT",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FormatPreset",
                table: "Projects",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "16:9");

            migrationBuilder.AddColumn<string>(
                name: "ImageModel",
                table: "Projects",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "gpt-image-2");

            migrationBuilder.AddColumn<string>(
                name: "LanguageModel",
                table: "Projects",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "gpt-5.4");

            migrationBuilder.AddColumn<int>(
                name: "OutputHeight",
                table: "Projects",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1080);

            migrationBuilder.AddColumn<int>(
                name: "OutputWidth",
                table: "Projects",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1920);

            migrationBuilder.AddColumn<string>(
                name: "PreviewResolution",
                table: "Projects",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "960x540");

            migrationBuilder.AddColumn<string>(
                name: "VideoModel",
                table: "Projects",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "FormatPreset",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ImageModel",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "LanguageModel",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "OutputHeight",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "OutputWidth",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "PreviewResolution",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "VideoModel",
                table: "Projects");
        }
    }
}
