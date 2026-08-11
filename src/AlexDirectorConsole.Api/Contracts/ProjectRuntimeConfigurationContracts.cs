using AlexDirectorConsole.Api.Models;

namespace AlexDirectorConsole.Api.Contracts;

public sealed record UpdateProjectRuntimeConfigurationRequest(
    string VmHost,
    int VmPort,
    string VmUsername,
    string SshPrivateKeyPath,
    string ComfyUiPath,
    string ComfyUiPythonPath,
    int ComfyUiPort,
    int LocalProxyPort,
    string WorkflowDirectory,
    string OutputDirectory);

public sealed record ProjectRuntimeConfigurationResponse(
    Guid ProjectId,
    string VmHost,
    int VmPort,
    string VmUsername,
    string SshPrivateKeyPath,
    string ComfyUiPath,
    string ComfyUiPythonPath,
    int ComfyUiPort,
    int LocalProxyPort,
    string WorkflowDirectory,
    string OutputDirectory,
    DateTimeOffset UpdatedAtUtc)
{
    public static ProjectRuntimeConfigurationResponse FromConfiguration(
        ProjectRuntimeConfiguration configuration) => new(
            configuration.ProjectId,
            configuration.VmHost,
            configuration.VmPort,
            configuration.VmUsername,
            configuration.SshPrivateKeyPath,
            configuration.ComfyUiPath,
            configuration.ComfyUiPythonPath,
            configuration.ComfyUiPort,
            configuration.LocalProxyPort,
            configuration.WorkflowDirectory,
            configuration.OutputDirectory,
            configuration.UpdatedAtUtc);
}