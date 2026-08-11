using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalFoundryConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GlobalFoundryConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OpenAiEndpoint = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    OpenAiDeployment = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ProtectedOpenAiApiKey = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    ImageEndpoint = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ImageDeployment = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ImageApiVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ImageQuality = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ProtectedImageApiKey = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalFoundryConfigurations", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GlobalFoundryConfigurations");
        }
    }
}
