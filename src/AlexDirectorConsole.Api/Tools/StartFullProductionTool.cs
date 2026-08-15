using System.Text.Json;
using AlexDirectorConsole.Api.Application.Production;
using AlexDirectorConsole.Api.Contracts;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class StartFullProductionTool(IProductionRunService productionRuns) : IDirectorTool
{
    public string Name => "start_full_production";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<bool, bool, string, CancellationToken, Task<string>>)(async (
            dryRun,
            keepVmRunning,
            shotNameContains,
            cancellationToken) =>
        {
            var snapshot = await productionRuns.StartAsync(
                context.ProjectId,
                context.Content,
                dryRun,
                keepVmRunning,
                shotNameContains,
                cancellationToken);
            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "full-production.planned",
                message = $"已持久化规划 {snapshot.ShotCount} 个镜头、{snapshot.Items.Count} 个阶段任务"
            }, cancellationToken);
            return JsonSerializer.Serialize(
                ProductionRunResponse.FromSnapshot(snapshot),
                context.JsonOptions);
        }),
        name: Name,
        description: "创建持久化的一句话出片任务。dryRun=true 只规划并返回任务；dryRun=false 会由后台执行器逐镜生成首帧、旁白和 H3 视频，再合成最终 MP4。keepVmRunning 决定真实执行结束后是否保留由本任务启动的 VM。shotNameContains 测试单镜时传镜号，制作全片时传空字符串。",
        serializerOptions: context.JsonOptions);
}