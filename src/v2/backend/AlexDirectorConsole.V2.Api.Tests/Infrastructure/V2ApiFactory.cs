using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Api.Features.Copilot;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
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
            services.RemoveAll<IProjectCopilotAgent>();
            services.RemoveAll<IProjectCoverGenerator>();
            services.RemoveAll<IProjectSettingsAssistant>();
            services.AddDbContext<V2DbContext>(options =>
                options.UseSqlite($"Data Source={databasePath};Pooling=False"));
            services.AddSingleton<IFoundryConnectionTester, SuccessfulFoundryConnectionTester>();
            services.AddScoped<IProjectCopilotAgent, TestProjectCopilotAgent>();
            services.AddSingleton<IProjectCoverGenerator, TestProjectCoverGenerator>();
            services.AddSingleton<IProjectSettingsAssistant, TestProjectSettingsAssistant>();
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

    private sealed class TestProjectCopilotAgent : IProjectCopilotAgent
    {
        public Task<CopilotAgentReply> ReplyAsync(
            Guid projectId,
            string projectName,
            string page,
            string episode,
            IReadOnlyList<CopilotHistoryMessage> history,
            string message,
            CancellationToken cancellationToken) => Task.FromResult(
                new CopilotAgentReply(
                    $"收到：{message}（历史 {history.Count} 条）",
                    "gpt-5.4",
                    "MAF HarnessAgent"));
    }

    private sealed class TestProjectCoverGenerator : IProjectCoverGenerator
    {
        private static readonly byte[] PngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

        public Task<GeneratedProjectCover> GenerateAsync(
            string prompt,
            string size,
            CancellationToken cancellationToken) => Task.FromResult(
                new GeneratedProjectCover(
                    PngBytes,
                    "image/png",
                    ".png",
                    "gpt-image-2",
                    "medium",
                    prompt));
    }

    private sealed class TestProjectSettingsAssistant : IProjectSettingsAssistant
    {
        public Task<ProjectSettingsAssistView> WriteAsync(
            ProjectSettingsAssistRequest request,
            CancellationToken cancellationToken) => Task.FromResult(
                new ProjectSettingsAssistView(
                    request.Field ?? string.Empty,
                    $"AI 优化：{request.CurrentValue}",
                    "gpt-5.4",
                    "MAF HarnessAgent"));
    }
}