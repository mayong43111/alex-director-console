using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFoundrySpeechConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProtectedSpeechApiKey",
                table: "GlobalFoundryConfigurations",
                type: "TEXT",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SpeechApiVersion",
                table: "GlobalFoundryConfigurations",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "2025-03-01-preview");

            migrationBuilder.AddColumn<string>(
                name: "SpeechDeployment",
                table: "GlobalFoundryConfigurations",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "tts");

            migrationBuilder.AddColumn<string>(
                name: "SpeechEndpoint",
                table: "GlobalFoundryConfigurations",
                type: "TEXT",
                maxLength: 500,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProtectedSpeechApiKey",
                table: "GlobalFoundryConfigurations");

            migrationBuilder.DropColumn(
                name: "SpeechApiVersion",
                table: "GlobalFoundryConfigurations");

            migrationBuilder.DropColumn(
                name: "SpeechDeployment",
                table: "GlobalFoundryConfigurations");

            migrationBuilder.DropColumn(
                name: "SpeechEndpoint",
                table: "GlobalFoundryConfigurations");
        }
    }
}
