using AlexDirectorConsole.Api.Application.Configuration;
using AlexDirectorConsole.Api.Services;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class ManageRemoteComfyUiTool(
    IRuntimeConfigurationReader configurationReader,
    IRemoteComfyUiService remoteComfyUiService) : IDirectorTool
{
    public string Name => "manage_remote_comfyui";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, CancellationToken, Task<string>>)(async (action, cancellationToken) =>
        {
            var configuration = await configurationReader.GetAsync(context.ProjectId, cancellationToken)
                ?? throw new InvalidOperationException("当前项目尚未配置 VM 与 ComfyUI。请先在项目配置中保存配置。");
            await context.WriteEventAsync(new { type = "process", stage = "comfyui.manage", message = $"正在执行远程动作：{action}" }, cancellationToken);
            return await remoteComfyUiService.ExecuteActionAsync(configuration, action, cancellationToken);
        }),
        name: Name,
        description: "管理项目 VM 上的 ComfyUI。action 仅可为 start、stop、restart、update、start-tunnel、stop-tunnel。update 只执行 Git fast-forward，不自动安装依赖或模型；执行变更前应先检查状态，并仅在导演明确要求远程变更或视频生成确有需要时调用。",
        serializerOptions: context.JsonOptions);
}