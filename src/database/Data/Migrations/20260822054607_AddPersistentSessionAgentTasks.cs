using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.V2.Database.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersistentSessionAgentTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "ProjectId",
                table: "AgentTasks",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<Guid>(
                name: "AgentId",
                table: "AgentTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancellationRequestedAtUtc",
                table: "AgentTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAtUtc",
                table: "AgentTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseOwner",
                table: "AgentTasks",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                table: "AgentTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_AgentId",
                table: "AgentTasks",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_SessionId",
                table: "AgentTasks",
                column: "SessionId");

            migrationBuilder.AddForeignKey(
                name: "FK_AgentTasks_AgentDefinitions_AgentId",
                table: "AgentTasks",
                column: "AgentId",
                principalTable: "AgentDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentTasks_Sessions_SessionId",
                table: "AgentTasks",
                column: "SessionId",
                principalTable: "Sessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentTasks_AgentDefinitions_AgentId",
                table: "AgentTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_AgentTasks_Sessions_SessionId",
                table: "AgentTasks");

            migrationBuilder.DropIndex(
                name: "IX_AgentTasks_AgentId",
                table: "AgentTasks");

            migrationBuilder.DropIndex(
                name: "IX_AgentTasks_SessionId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "AgentId",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "CancellationRequestedAtUtc",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAtUtc",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "LeaseOwner",
                table: "AgentTasks");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "AgentTasks");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProjectId",
                table: "AgentTasks",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
