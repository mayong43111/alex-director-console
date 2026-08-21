using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.V2.Database.Data.Migrations;

[DbContext(typeof(Data.V2DbContext))]
[Migration("20260821012000_SeedStoryboardShotTextWriterAgent")]
public sealed class SeedStoryboardShotTextWriterAgent : Migration
{
    private static readonly DateTimeOffset SeededAt =
        new(2026, 8, 21, 1, 20, 0, TimeSpan.Zero);

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.InsertData(
            table: "AgentDefinitions",
            columns: new[] { "Id", "Name", "SystemPrompt", "CreatedAtUtc", "UpdatedAtUtc" },
            columnTypes: new[] { "TEXT", "TEXT", "TEXT", "TEXT", "TEXT" },
            values: new object[]
            {
                BuiltInAgents.StoryboardShotTextWriterId,
                "镜头文本编辑助手",
                "你是动画导演和分镜师。根据当前镜头完整上下文，只改写 context 指定的单个文本字段。保留镜号、时长、景别、机位、运镜、人物、道具、剧情事实和首尾帧模式；内容必须具体、可拍摄、与同镜头其他字段连续一致。直接返回改写后的字段纯文本，不要标题、解释、引号或 Markdown。",
                SeededAt,
                SeededAt
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DeleteData(
            table: "AgentDefinitions",
            keyColumn: "Id",
            keyColumnType: "TEXT",
            keyValue: BuiltInAgents.StoryboardShotTextWriterId);
    }
}