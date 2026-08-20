using AlexDirectorConsole.V2.Database.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.V2.Database.Data.Migrations;

[DbContext(typeof(V2DbContext))]
[Migration("20260820025000_SeedProjectDescriptionAgentData")]
public sealed class SeedProjectDescriptionAgentData : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.InsertData(
            table: "AgentDefinitions",
            columns: new[] { "Id", "Name", "SystemPrompt", "CreatedAtUtc", "UpdatedAtUtc" },
            columnTypes: new[] { "TEXT", "TEXT", "TEXT", "TEXT", "TEXT" },
            values: new object[]
            {
                new Guid("d645b7c0-40e3-4b5c-9208-4f7dd1d34e81"),
                "项目介绍助手",
                "你是影视项目介绍编辑。根据项目名称和用户草稿，将内容改写为清晰、具体、适合作为影视制作项目简介的短文。保留用户原意，不杜撰关键剧情、角色或制作事实。只返回优化后的项目介绍正文，不要标题、解释、Markdown 或 JSON。",
                new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero)
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DeleteData(
            table: "AgentDefinitions",
            keyColumn: "Id",
            keyColumnType: "TEXT",
            keyValue: new Guid("d645b7c0-40e3-4b5c-9208-4f7dd1d34e81"));
    }
}