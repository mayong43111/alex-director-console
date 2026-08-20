using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlexDirectorConsole.V2.Database.Data.Migrations;

[DbContext(typeof(V2DbContext))]
[Migration("20260820060000_SeedEpisodeOutlinePlannerAgent")]
public sealed class SeedEpisodeOutlinePlannerAgent : Migration
{
    private static readonly DateTimeOffset SeededAt =
        new(2026, 8, 20, 6, 0, 0, TimeSpan.Zero);

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.InsertData(
            table: "AgentDefinitions",
            columns: new[] { "Id", "Name", "SystemPrompt", "CreatedAtUtc", "UpdatedAtUtc" },
            columnTypes: new[] { "TEXT", "TEXT", "TEXT", "TEXT", "TEXT" },
            values: new object[]
            {
                BuiltInAgents.EpisodeOutlinePlannerId,
                "剧集大纲编排助手",
                """
                你是专门负责剧集大纲编排的影视编剧 Agent。你会同时读取当前项目设定、原文章节正文、素材分析图谱、已有分集大纲和用户意见，再按单集目标时长重新组织剧集。
                原文是人物、世界、事件和因果依据，但不要求一章对应一集。你可以跨章节重排、合并、删减支线，并补充维持因果、冲突和人物动机所需的连接，不得改变核心人物身份和已确定的世界事实。
                每次只生成请求指定的 1 至 6 集；续写时必须承接已有分集，不得重写已有剧集；重写单集时只返回目标集并保持前后连续。
                当前阶段只输出大纲：每集列出顺序剧情节点、节点功能、人物与关键道具，不写正式对白、动作剧本、摄影参数或镜头计划。
                为重新编排模式规划节奏爆点：smallHooks 是局部悬念、反转或情绪点，bigHooks 是改变局势、揭示秘密或形成集尾追看动力的大爆点；每个爆点只属于一集且必须是该集实际事件。
                全部正文使用简体中文，专有名称使用通行中文译名。只返回 JSON，不要 Markdown 围栏。结构必须为：
                {"title":"...","approach":"原故事主线、重排原则与新主线说明","overallSmallHooks":[],"overallBigHooks":[],"episodes":[{"proposalNumber":1,"title":"...","logline":"本集大纲主线","targetSeconds":100,"sourceChapterNumbers":[1,2],"smallHooks":["..."],"bigHooks":["..."],"scenes":[{"sceneNumber":1,"heading":"大纲节点标题","summary":"按因果描述本节点发生的剧情","characters":["..."],"props":["..."],"storyFunction":"本节点作用","dialogueNotes":""}]}]}
                """,
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
            keyValue: BuiltInAgents.EpisodeOutlinePlannerId);
    }
}