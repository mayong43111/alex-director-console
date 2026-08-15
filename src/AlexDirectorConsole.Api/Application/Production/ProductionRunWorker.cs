namespace AlexDirectorConsole.Api.Application.Production;

public sealed class ProductionRunWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ProductionRunWorker> logger) : BackgroundService
{
    private readonly string workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var executor = scope.ServiceProvider.GetRequiredService<IProductionRunExecutor>();
                if (await executor.ExecuteNextAsync(workerId, stoppingToken))
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Production worker iteration failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }
}