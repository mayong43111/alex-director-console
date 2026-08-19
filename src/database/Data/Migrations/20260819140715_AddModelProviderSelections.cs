using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.V2.Database.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModelProviderSelections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageProvider",
                table: "FoundryConfigurations",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LlmProvider",
                table: "FoundryConfigurations",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProtectedVllmApiKey",
                table: "FoundryConfigurations",
                type: "TEXT",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VllmBaseUrl",
                table: "FoundryConfigurations",
                type: "TEXT",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VllmModel",
                table: "FoundryConfigurations",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ImageEditWorkflow",
                table: "ComfyUiConfigurations",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TextToImageWorkflow",
                table: "ComfyUiConfigurations",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageProvider",
                table: "FoundryConfigurations");

            migrationBuilder.DropColumn(
                name: "LlmProvider",
                table: "FoundryConfigurations");

            migrationBuilder.DropColumn(
                name: "ProtectedVllmApiKey",
                table: "FoundryConfigurations");

            migrationBuilder.DropColumn(
                name: "VllmBaseUrl",
                table: "FoundryConfigurations");

            migrationBuilder.DropColumn(
                name: "VllmModel",
                table: "FoundryConfigurations");

            migrationBuilder.DropColumn(
                name: "ImageEditWorkflow",
                table: "ComfyUiConfigurations");

            migrationBuilder.DropColumn(
                name: "TextToImageWorkflow",
                table: "ComfyUiConfigurations");
        }
    }
}
