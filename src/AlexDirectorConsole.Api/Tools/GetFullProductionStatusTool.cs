using System.Text.Json;
using AlexDirectorConsole.Api.Application.Production;
using AlexDirectorConsole.Api.Contracts;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class GetFullProductionStatusTool(IProductionRunService productionRuns) : IDirectorTool
{
    public string Name => "get_full_production_status";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, CancellationToken, Task<string>>)(async (runId, cancellationToken) =>
        {
            if (!Guid.TryParse(runId, out var parsedRunId))
            {
                throw new ArgumentException("runId 必须是有效 UUID。", nameof(runId));
            }
            var snapshot = await productionRuns.GetAsync(
                context.ProjectId,
                parsedRunId,
                cancellationToken) ?? throw new KeyNotFoundException("未找到当前项目中的生产任务。");
            return JsonSerializer.Serialize(
                ProductionRunResponse.FromSnapshot(snapshot),
                context.JsonOptions);
        }),
        name: Name,
        description: "查询当前项目中持久化的一句话出片任务状态。runId 必须来自 start_full_production 的真实返回。",
        serializerOptions: context.JsonOptions);
}