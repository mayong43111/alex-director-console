using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.V2.Database.Data.Migrations;

public partial class AddSessionsAndAssistantDirector : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.InsertData(
            table: "AgentDefinitions",
            columns: new[] { "Id", "Name", "SystemPrompt", "CreatedAtUtc", "UpdatedAtUtc" },
            columnTypes: new[] { "TEXT", "TEXT", "TEXT", "TEXT", "TEXT" },
            values: new object[]
            {
                new Guid("9b695559-9d9d-492d-8ee7-f1a76438b20c"),
                "副导演",
                "你是 Alex 导演台唯一的副导演 Agent。必须先加载与当前任务匹配的 Skill，再按 Skill 约束选择工具。只根据工具返回报告执行结果，不得虚构数据或声称执行了未完成的操作。项目查询、创建和更新可使用项目管理工具；项目删除没有工具，用户要求删除时必须提示其在项目中心手动操作。",
                new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero)
            });

        migrationBuilder.CreateTable(
            name: "Sessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                AgentId = table.Column<Guid>(type: "TEXT", nullable: false),
                ScopeKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                Runtime = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Sessions", item => item.Id);
                table.ForeignKey(
                    name: "FK_Sessions_AgentDefinitions_AgentId",
                    column: item => item.AgentId,
                    principalTable: "AgentDefinitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Sessions_Projects_ProjectId",
                    column: item => item.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "SessionMessages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Content = table.Column<string>(type: "TEXT", maxLength: 100000, nullable: false),
                Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SessionMessages", item => item.Id);
                table.CheckConstraint("CK_SessionMessages_Sequence", "Sequence > 0");
                table.ForeignKey(
                    name: "FK_SessionMessages_Sessions_SessionId",
                    column: item => item.SessionId,
                    principalTable: "Sessions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SessionMessages_SessionId_Sequence",
            table: "SessionMessages",
            columns: new[] { "SessionId", "Sequence" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Sessions_AgentId_ScopeKey",
            table: "Sessions",
            columns: new[] { "AgentId", "ScopeKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Sessions_ProjectId",
            table: "Sessions",
            column: "ProjectId");

        migrationBuilder.Sql(
            """
            INSERT INTO Sessions (Id, AgentId, ScopeKey, ProjectId, Title, Runtime, CreatedAtUtc, UpdatedAtUtc)
            SELECT c.Id,
                   '9B695559-9D9D-492D-8EE7-F1A76438B20C',
                   'project:' || lower(c.ProjectId) || ':assistant-director',
                   c.ProjectId,
                   '项目：' || p.Name,
                   'MAF HarnessAgent',
                   c.CreatedAtUtc,
                   c.UpdatedAtUtc
            FROM CopilotConversations AS c
            INNER JOIN Projects AS p ON p.Id = c.ProjectId;

            INSERT INTO SessionMessages (Id, SessionId, Sequence, Role, Content, Model, CreatedAtUtc)
            SELECT Id, ConversationId, Sequence, Role, Content, Model, CreatedAtUtc
            FROM CopilotMessages;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SessionMessages");
        migrationBuilder.DropTable(name: "Sessions");
        migrationBuilder.DeleteData(
            table: "AgentDefinitions",
            keyColumn: "Id",
            keyColumnType: "TEXT",
            keyValue: new Guid("9b695559-9d9d-492d-8ee7-f1a76438b20c"));
    }
}
