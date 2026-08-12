using AlexDirectorConsole.Api.Models;

namespace AlexDirectorConsole.Api.Application.Configuration;

public interface IRuntimeConfigurationReader
{
    Task<ProjectRuntimeConfiguration?> GetAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}