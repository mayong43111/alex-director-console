using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Api.Features.Skills;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AlexDirectorConsole.V2.Api.Tests.Infrastructure;

public sealed class V2ApiFactory : WebApplicationFactory<Program>
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(),
        $"alex-director-v2-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<V2DbContext>();
            services.RemoveAll<DbContextOptions<V2DbContext>>();
            services.RemoveAll<IFoundryConnectionTester>();
            services.AddDbContext<V2DbContext>(options =>
                options.UseSqlite($"Data Source={databasePath};Pooling=False"));
            services.AddSingleton<IFoundryConnectionTester, SuccessfulFoundryConnectionTester>();
        });
    }

    public async Task ResetDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();
        var skillSynchronizer = scope.ServiceProvider.GetRequiredService<ISkillCatalogSynchronizer>();
        await skillSynchronizer.SynchronizeAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }

    private sealed class SuccessfulFoundryConnectionTester : IFoundryConnectionTester
    {
        public Task TestAsync(
            string endpoint,
            string deployment,
            string apiKey,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}