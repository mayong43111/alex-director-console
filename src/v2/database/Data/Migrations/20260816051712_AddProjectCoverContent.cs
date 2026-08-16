using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.V2.Database.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectCoverContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "BlobContent",
                table: "Assets",
                type: "BLOB",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlobContent",
                table: "Assets");
        }
    }
}
