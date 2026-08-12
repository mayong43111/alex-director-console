using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "Assets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                WITH ResourceNumbers AS (
                    SELECT
                        "ProjectId",
                        "ResourceId",
                        ROW_NUMBER() OVER (
                            PARTITION BY "ProjectId"
                            ORDER BY MIN("CreatedAtUtc"), MIN("Id")
                        ) AS "Number"
                    FROM "Assets"
                    GROUP BY "ProjectId", "ResourceId"
                )
                UPDATE "Assets"
                SET "Number" = (
                    SELECT ResourceNumbers."Number"
                    FROM ResourceNumbers
                    WHERE ResourceNumbers."ProjectId" = "Assets"."ProjectId"
                      AND ResourceNumbers."ResourceId" = "Assets"."ResourceId"
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_ProjectId_Number_Version",
                table: "Assets",
                columns: new[] { "ProjectId", "Number", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assets_ProjectId_Number_Version",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "Assets");
        }
    }
}
