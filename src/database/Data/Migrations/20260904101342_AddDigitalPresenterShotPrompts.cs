using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.V2.Database.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDigitalPresenterShotPrompts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImagePrompt",
                table: "DigitalPresenterShots",
                type: "TEXT",
                maxLength: 12000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VideoPrompt",
                table: "DigitalPresenterShots",
                type: "TEXT",
                maxLength: 12000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImagePrompt",
                table: "DigitalPresenterShots");

            migrationBuilder.DropColumn(
                name: "VideoPrompt",
                table: "DigitalPresenterShots");
        }
    }
}
