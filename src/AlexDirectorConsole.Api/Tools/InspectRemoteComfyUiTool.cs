using AlexDirectorConsole.Api.Application.Configuration;
using AlexDirectorConsole.Api.Services;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class InspectRemoteComfyUiTool(
    IRuntimeConfigurationReader configurationReader,
    IRemoteComfyUiService remoteComfyUiService) : IDirectorTool
{
    public string Name => "inspect_remote_comfyui";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<CancellationToken, Task<string>>)(async cancellationToken =>
        {
            var configuration = await configurationReader.GetAsync(context.ProjectId, cancellationToken)
                ?? throw new InvalidOperationException("当前项目尚未配置 VM 与 ComfyUI。请先在项目配置中保存配置。");
            await context.WriteEventAsync(new { type = "process", stage = "comfyui.inspect", message = "正在通过 8188 HTTP 代理检查 ComfyUI" }, cancellationToken);
            return await remoteComfyUiService.InspectAsync(configuration, cancellationToken);
        }),
        name: Name,
        description: "通过已建立的本地 HTTP 代理读取 ComfyUI system_stats、queue、userdata 和 object_info，检查设备、队列、workflow、H3 节点与可选模型。不会建立 SSH 连接。",
        serializerOptions: context.JsonOptions);
}