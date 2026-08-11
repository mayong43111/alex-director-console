using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class InspectRemoteComfyUiTool : IDirectorTool
{
    public string Name => "inspect_remote_comfyui";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<CancellationToken, Task<string>>)(async cancellationToken =>
        {
            var configuration = await context.DbContext.ProjectRuntimeConfigurations
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.ProjectId == Guid.Empty, cancellationToken)
                ?? throw new InvalidOperationException("尚未配置全局 VM 与 ComfyUI。请先在系统配置中保存配置。");
            await context.WriteEventAsync(new { type = "process", stage = "comfyui.inspect", message = "正在通过 8188 HTTP 代理检查 ComfyUI" }, cancellationToken);
            return await context.RemoteComfyUiService.InspectAsync(configuration, cancellationToken);
        }),
        name: Name,
        description: "通过已建立的本地 HTTP 代理读取 ComfyUI system_stats、queue、userdata 和 object_info，检查设备、队列、workflow、H3 节点与可选模型。不会建立 SSH 连接。",
        serializerOptions: context.JsonOptions);
}