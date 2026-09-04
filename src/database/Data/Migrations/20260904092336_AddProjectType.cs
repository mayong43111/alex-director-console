using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.V2.Database.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DigitalPresenterEpisodes_Projects_ProjectId",
                table: "DigitalPresenterEpisodes");

            migrationBuilder.DropForeignKey(
                name: "FK_DigitalPresenters_Projects_ProjectId",
                table: "DigitalPresenters");

            migrationBuilder.DropForeignKey(
                name: "FK_DigitalPresenterShots_Projects_ProjectId",
                table: "DigitalPresenterShots");

            migrationBuilder.DropIndex(
                name: "IX_DigitalPresenterShots_ProjectId",
                table: "DigitalPresenterShots");

            migrationBuilder.DropIndex(
                name: "IX_DigitalPresenterEpisodes_ProjectId",
                table: "DigitalPresenterEpisodes");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Projects",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProjectId",
                table: "DigitalPresenterShots",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProjectId",
                table: "DigitalPresenters",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProjectId",
                table: "DigitalPresenterEpisodes",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Type",
                table: "Projects");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProjectId",
                table: "DigitalPresenterShots",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ProjectId",
                table: "DigitalPresenters",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ProjectId",
                table: "DigitalPresenterEpisodes",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DigitalPresenterShots_ProjectId",
                table: "DigitalPresenterShots",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalPresenterEpisodes_ProjectId",
                table: "DigitalPresenterEpisodes",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_DigitalPresenterEpisodes_Projects_ProjectId",
                table: "DigitalPresenterEpisodes",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DigitalPresenters_Projects_ProjectId",
                table: "DigitalPresenters",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DigitalPresenterShots_Projects_ProjectId",
                table: "DigitalPresenterShots",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
