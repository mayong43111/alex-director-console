using AlexDirectorConsole.Api.Models;
using AlexDirectorConsole.Api.Services;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class RunScriptBreakdownTool(IAgentSkillExecutor skillExecutor) : IDirectorTool
{
    public string Name => "run_script_breakdown";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, CancellationToken, Task<string>>)(async (scriptAssetId, cancellationToken) =>
        {
            if (!Guid.TryParse(scriptAssetId, out var parsedScriptAssetId))
            {
                throw new ArgumentException("scriptAssetId 必须是有效 UUID。", nameof(scriptAssetId));
            }
            context.Execution = await skillExecutor.ExecuteScriptBreakdownAsync(
                context.ProjectId,
                parsedScriptAssetId,
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
        description: "分析当前项目中指定的剧本文本，并由 Script Agent 建立分析、人物、场景和关键道具资源。scriptAssetId 可使用界面当前剧本 ID；未选择剧本时，先通过 list_project_resources 定位并用 read_project_resource_contents 读取目标剧本，再传入其 ID。目标必须属于当前项目且类型为 script。",
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
