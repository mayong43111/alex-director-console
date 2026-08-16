using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.V2.Database.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddImageGenerationSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageDeployment",
                table: "FoundryConfigurations",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "gpt-image-2");

            migrationBuilder.AddColumn<string>(
                name: "ImageEndpoint",
                table: "FoundryConfigurations",
                type: "TEXT",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProtectedImageApiKey",
                table: "FoundryConfigurations",
                type: "TEXT",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageDeployment",
                table: "FoundryConfigurations");

            migrationBuilder.DropColumn(
                name: "ImageEndpoint",
                table: "FoundryConfigurations");

            migrationBuilder.DropColumn(
                name: "ProtectedImageApiKey",
                table: "FoundryConfigurations");
        }
    }
}
