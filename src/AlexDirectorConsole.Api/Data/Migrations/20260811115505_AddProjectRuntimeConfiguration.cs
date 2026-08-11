using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectRuntimeConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectRuntimeConfigurations",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VmHost = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    VmPort = table.Column<int>(type: "INTEGER", nullable: false),
                    VmUsername = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SshPrivateKeyPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ComfyUiPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ComfyUiPythonPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ComfyUiPort = table.Column<int>(type: "INTEGER", nullable: false),
                    LocalProxyPort = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkflowDirectory = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    OutputDirectory = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectRuntimeConfigurations", x => x.ProjectId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectRuntimeConfigurations");
        }
    }
}
