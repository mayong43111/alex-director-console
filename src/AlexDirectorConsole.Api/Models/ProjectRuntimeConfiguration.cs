namespace AlexDirectorConsole.Api.Models;

public sealed class ProjectRuntimeConfiguration
{
    public Guid ProjectId { get; set; }

    public string VmHost { get; set; } = string.Empty;

    public int VmPort { get; set; } = 22;

    public string VmUsername { get; set; } = string.Empty;

    public string SshPrivateKeyPath { get; set; } = string.Empty;

    public string ComfyUiPath { get; set; } = "~/ComfyUI";

    public string ComfyUiPythonPath { get; set; } = "python";

    public int ComfyUiPort { get; set; } = 8188;

    public int LocalProxyPort { get; set; } = 8188;

    public string WorkflowDirectory { get; set; } = "~/ComfyUI/user/default/workflows";

    public string OutputDirectory { get; set; } = "~/ComfyUI/output";

    public DateTimeOffset UpdatedAtUtc { get; set; }
}