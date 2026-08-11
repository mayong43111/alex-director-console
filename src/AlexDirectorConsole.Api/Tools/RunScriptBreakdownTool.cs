using AlexDirectorConsole.Api.Models;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class RunScriptBreakdownTool : IDirectorTool
{
    public string Name => "run_script_breakdown";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<CancellationToken, Task<string>>)(async cancellationToken =>
        {
            if (context.CurrentAsset?.Type != "script")
            {
                throw new InvalidOperationException("run_script_breakdown 需要界面当前资源为剧本文本。");
            }
            context.Execution = await context.SkillExecutor.ExecuteScriptBreakdownAsync(
                context.ProjectId,
                context.CurrentAsset.Id,
                context.Content,
                context.RequestedModel,
                async progress => await context.WriteEventAsync(new
                {
                    type = "process",
                    stage = progress.Type,
                    message = progress.Message,
                    data = progress.Data
                }, cancellationToken),
                cancellationToken);
            return BuildSummary(context.Execution.GeneratedAssets, context.Execution.Run.Id);
        }),
        name: Name,
        description: "分析界面当前选中的剧本，并由 Script Agent 建立分析、人物、场景和关键道具资源。调用时当前资源必须是剧本文本。",
        serializerOptions: context.JsonOptions);

    private static string BuildSummary(IReadOnlyList<Asset> assets, Guid runId)
    {
        var analysisCount = assets.Count(asset => asset.Type == "analysis");
        var characterCount = assets.Count(asset => asset.Type == "character");
        var sceneCount = assets.Count(asset => asset.Type == "scene");
        var propCount = assets.Count(asset => asset.Type == "prop");
        return $"""
            已使用「剧本拆解」技能完成分析，并由 Agent 调用工具写入制作资源。

            已生成分析稿 {analysisCount} 份、人物设定稿 {characterCount} 份、场景设定稿 {sceneCount} 份、道具设定稿 {propCount} 份。
            可在左侧“分析 / 人物 / 场景 / 道具”分类中逐份审阅。

            技能运行 ID：{runId}
            """;
    }
}
