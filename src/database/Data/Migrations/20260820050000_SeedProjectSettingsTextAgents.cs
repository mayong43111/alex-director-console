using AlexDirectorConsole.V2.Database.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.V2.Database.Data.Migrations;

[DbContext(typeof(V2DbContext))]
[Migration("20260820050000_SeedProjectSettingsTextAgents")]
public sealed class SeedProjectSettingsTextAgents : Migration
{
    private static readonly DateTimeOffset SeededAt =
        new(2026, 8, 20, 5, 0, 0, TimeSpan.Zero);

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        InsertAgent(
            migrationBuilder,
            new Guid("f623cbe9-0852-4003-83a5-5990ad4470e5"),
            "项目美术方向助手",
            "你是影视项目美术总监。根据完整项目上下文和当前草稿，撰写明确、统一、可执行的美术方向，覆盖时代地域、媒介质感、线条与材质、动作表现和应避免的视觉倾向。保留已确定事实，不杜撰关键剧情。");
        InsertAgent(
            migrationBuilder,
            new Guid("7c7b6d83-89b7-4939-a1dd-80a67326558b"),
            "角色造型约束助手",
            "你是影视角色设计总监。根据完整项目上下文和当前草稿，撰写可跨镜头复用的角色造型硬约束，明确物种、轮廓、体型、服装、关键识别特征、拟人化程度与禁止项。只写稳定约束，不添加未经支持的角色事实。");
        InsertAgent(
            migrationBuilder,
            new Guid("b725ec2a-ecc7-428c-b5c3-f048f11b17d0"),
            "项目色彩策略助手",
            "你是影视色彩设计师。根据完整项目上下文和当前草稿，撰写项目级色彩策略，明确主色、辅助色、场景或情绪变化规则、明暗关系与应避免的色彩倾向，确保后续资产和镜头可一致执行。");
        InsertAgent(
            migrationBuilder,
            new Guid("c86c50ec-8dd5-4889-85af-075c500256e7"),
            "项目摄影语言助手",
            "你是影视摄影指导。根据完整项目上下文和当前草稿，撰写可执行的项目级摄影语言，明确景别、机位、构图、运镜、轴线、节奏与主体可读性规则，并指出应避免的镜头倾向。");
        InsertAgent(
            migrationBuilder,
            new Guid("f430be3f-ee6f-4e03-9475-82af31f699b5"),
            "项目声音策略助手",
            "你是影视声音指导。根据完整项目上下文和当前草稿，撰写项目级声音策略，明确音乐方向、环境声、动作音效、角色声音、节奏和连续性规则，内容应具体且可用于后续制作。");
        InsertAgent(
            migrationBuilder,
            new Guid("b25d162a-691c-4382-a3f2-e430d04a43c8"),
            "图像生成约束助手",
            "你是影视图像生成提示词总监。根据完整项目上下文和当前草稿，撰写可作为所有图像提示词前缀的项目级约束，包含稳定画风、时代环境、角色一致性、构图光影、画幅与明确禁止项；不要写具体镜头事件。");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        DeleteAgent(migrationBuilder, new Guid("b25d162a-691c-4382-a3f2-e430d04a43c8"));
        DeleteAgent(migrationBuilder, new Guid("f430be3f-ee6f-4e03-9475-82af31f699b5"));
        DeleteAgent(migrationBuilder, new Guid("c86c50ec-8dd5-4889-85af-075c500256e7"));
        DeleteAgent(migrationBuilder, new Guid("b725ec2a-ecc7-428c-b5c3-f048f11b17d0"));
        DeleteAgent(migrationBuilder, new Guid("7c7b6d83-89b7-4939-a1dd-80a67326558b"));
        DeleteAgent(migrationBuilder, new Guid("f623cbe9-0852-4003-83a5-5990ad4470e5"));
    }

    private static void InsertAgent(
        MigrationBuilder migrationBuilder,
        Guid id,
        string name,
        string systemPrompt)
    {
        migrationBuilder.InsertData(
            table: "AgentDefinitions",
            columns: new[] { "Id", "Name", "SystemPrompt", "CreatedAtUtc", "UpdatedAtUtc" },
            columnTypes: new[] { "TEXT", "TEXT", "TEXT", "TEXT", "TEXT" },
            values: new object[] { id, name, systemPrompt, SeededAt, SeededAt });
    }

    private static void DeleteAgent(MigrationBuilder migrationBuilder, Guid id)
    {
        migrationBuilder.DeleteData(
            table: "AgentDefinitions",
            keyColumn: "Id",
            keyColumnType: "TEXT",
            keyValue: id);
    }
}