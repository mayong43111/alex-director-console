namespace AlexDirectorConsole.Api.Application.Maintenance;

public interface IApplicationMaintenanceRunner
{
    Task RunPendingAsync(CancellationToken cancellationToken = default);
}