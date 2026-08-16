using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Api.Features.Copilot;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.Projects.Sources;
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
            services.RemoveAll<IStoryMaterialAnalyzer>();
            services.RemoveAll<IAdaptationScriptWriter>();
            services.AddDbContext<V2DbContext>(options =>
                options.UseSqlite($"Data Source={databasePath};Pooling=False"));
            services.AddSingleton<IFoundryConnectionTester, SuccessfulFoundryConnectionTester>();
            services.AddScoped<IProjectCopilotAgent, TestProjectCopilotAgent>();
            services.AddSingleton<IProjectCoverGenerator, TestProjectCoverGenerator>();
            services.AddSingleton<IProjectSettingsAssistant, TestProjectSettingsAssistant>();
            services.AddSingleton<IStoryMaterialAnalyzer, TestStoryMaterialAnalyzer>();
            services.AddSingleton<IAdaptationScriptWriter, TestAdaptationScriptWriter>();
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

    private sealed class TestStoryMaterialAnalyzer : IStoryMaterialAnalyzer
    {
        public Task<StoryMaterialAnalysisResult> AnalyzeAsync(
            string projectName,
            IReadOnlyList<SourceChapterView> chapters,
            CancellationToken cancellationToken) => Task.FromResult(
                new StoryMaterialAnalysisResult(
                    "达达尼昂赴巴黎并进入火枪手关系网络。",
                    [new StoryCharacterMaterial("达达尼昂", "主角", "成为火枪手", ["勇敢", "冲动"], [1, 2])],
                    [new StoryLocationMaterial("巴黎", "主要行动空间", "权力与冒险交织", [2])],
                    [new StoryPlotBeatMaterial(1, "离乡", "达达尼昂带着父亲的建议出发。", [1], ["达达尼昂"], null)],
                    [new StoryRelationMaterial("达达尼昂", "特雷维尔", "投奔", "父亲的推荐信")],
                    "gpt-5.4-test",
                    "Test Harness"));
    }

    private sealed class TestAdaptationScriptWriter : IAdaptationScriptWriter
    {
        public Task<AdaptationScriptResult> WriteAsync(
            ProjectSettingsView projectSettings,
            StoryMaterialAnalysisView analysis,
            IReadOnlyList<SourceChapterView> chapters,
            int desiredEpisodeCount,
            string? instruction,
            CancellationToken cancellationToken) => Task.FromResult(
                new AdaptationScriptResult(
                    "初到巴黎",
                    "合并前两章并以得到接见作为单集收束。",
                    Enumerable.Range(1, desiredEpisodeCount).Select(number => new AdaptationEpisodeDraft(
                        number,
                        $"年轻人的推荐信 {number}",
                        "达达尼昂带着推荐信闯入巴黎火枪手世界。",
                        projectSettings.TargetEpisodeSeconds,
                        [1, 2],
                        [new AdaptationSceneDraft(
                            1,
                            "外景 · 巴黎街道 · 日",
                            "达达尼昂进入巴黎。",
                            ["达达尼昂"],
                            ["推荐信"],
                            "建立目标与行动空间",
                            "对白保持简洁，突出外乡人的直率。")],
                        ["推荐信不翼而飞"],
                        ["幕后势力首次现身"])).ToArray(),
                    "gpt-5.4-test",
                    "Test Harness",
                    ["三集持续升级达达尼昂的入局压力"],
                    ["最终揭示阴谋指向王后"]));
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