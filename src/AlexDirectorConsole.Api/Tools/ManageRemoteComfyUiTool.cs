using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class ManageRemoteComfyUiTool : IDirectorTool
{
    public string Name => "manage_remote_comfyui";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, CancellationToken, Task<string>>)(async (action, cancellationToken) =>
        {
            var configuration = await context.DbContext.ProjectRuntimeConfigurations
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.ProjectId == Guid.Empty, cancellationToken)
                ?? throw new InvalidOperationException("尚未配置全局 VM 与 ComfyUI。请先在系统配置中保存配置。");
            await context.WriteEventAsync(new { type = "process", stage = "comfyui.manage", message = $"正在执行远程动作：{action}" }, cancellationToken);
            return await context.RemoteComfyUiService.ExecuteActionAsync(configuration, action, cancellationToken);
        }),
        name: Name,
        description: "管理项目 VM 上的 ComfyUI。action 仅可为 start、stop、restart、update、start-tunnel、stop-tunnel。update 只执行 Git fast-forward，不自动安装依赖或模型；执行变更前应先检查状态，并仅在导演明确要求远程变更或视频生成确有需要时调用。",
        serializerOptions: context.JsonOptions);
}