using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Api.Features.Copilot;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.Projects.Assets;
using AlexDirectorConsole.V2.Api.Features.Projects.Sources;
using AlexDirectorConsole.V2.Api.Features.Projects.Storyboard;
using AlexDirectorConsole.V2.Api.Features.Projects.Voice;
using AlexDirectorConsole.V2.Api.Features.Skills;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.ComfyUi;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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
            services.RemoveAll<IComfyUiConnectionTester>();
            services.RemoveAll<IComfyUiVideoClient>();
            services.RemoveAll<IComfyUiWorkflowProvider>();
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IProjectCopilotAgent>();
            services.RemoveAll<IProjectCoverGenerator>();
            services.RemoveAll<IShotFrameGenerator>();
            services.RemoveAll<ILocalVoiceDesigner>();
            services.RemoveAll<IProjectSettingsAssistant>();
            services.RemoveAll<IStoryMaterialAnalyzer>();
            services.RemoveAll<IAdaptationScriptWriter>();
            services.RemoveAll<IStoryboardDesigner>();
            services.AddDbContext<V2DbContext>(options =>
                options.UseSqlite($"Data Source={databasePath};Pooling=False"));
            services.AddSingleton<IFoundryConnectionTester, SuccessfulFoundryConnectionTester>();
            services.AddSingleton<IComfyUiConnectionTester, SuccessfulComfyUiConnectionTester>();
            services.AddSingleton<TestComfyUiVideoClient>();
            services.AddSingleton<IComfyUiVideoClient>(provider =>
                provider.GetRequiredService<TestComfyUiVideoClient>());
            services.AddSingleton<IComfyUiWorkflowProvider, TestComfyUiWorkflowProvider>();
            services.AddScoped<IProjectCopilotAgent, TestProjectCopilotAgent>();
            services.AddSingleton<IProjectCoverGenerator, TestProjectCoverGenerator>();
            services.AddSingleton<IShotFrameGenerator, TestShotFrameGenerator>();
            services.AddSingleton<ILocalVoiceDesigner, TestLocalVoiceDesigner>();
            services.AddSingleton<IProjectSettingsAssistant, TestProjectSettingsAssistant>();
            services.AddSingleton<IStoryMaterialAnalyzer, TestStoryMaterialAnalyzer>();
            services.AddSingleton<IAdaptationScriptWriter, TestAdaptationScriptWriter>();
            services.AddSingleton<IStoryboardDesigner, TestStoryboardDesigner>();
        });
    }

    public async Task ResetDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();
        Services.GetRequiredService<TestComfyUiVideoClient>().Reset();
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

    private sealed class SuccessfulComfyUiConnectionTester : IComfyUiConnectionTester
    {
        public Task<ComfyUiCapabilities> TestAsync(
            string baseUrl,
            CancellationToken cancellationToken) => Task.FromResult(
                new ComfyUiCapabilities(
                    true,
                    "ComfyUI test connection succeeded.",
                    ComfyUiConfigurationView.RequiredWorkflowProfile,
                    ["MiniMaxH3ImageToVideo", "LoraLoaderModelOnly"],
                    [],
                    ["minimax-h3-test.safetensors"],
                    []));
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

    private sealed class TestShotFrameGenerator : IShotFrameGenerator
    {
        private static readonly byte[] PngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

        public Task<GeneratedShotFrame> GenerateAsync(
            string prompt,
            string size,
            IReadOnlyList<ShotFrameReference> references,
            CancellationToken cancellationToken) => Task.FromResult(
                new GeneratedShotFrame(
                    PngBytes,
                    "image/png",
                    ".png",
                    "gpt-image-2",
                    "medium",
                    prompt));
    }

    private sealed class TestLocalVoiceDesigner : ILocalVoiceDesigner
    {
        private static readonly byte[] WavBytes =
        [
            0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00,
            0x57, 0x41, 0x56, 0x45, 0x66, 0x6d, 0x74, 0x20,
            0x10, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00,
            0x80, 0xbb, 0x00, 0x00, 0x00, 0x77, 0x01, 0x00,
            0x02, 0x00, 0x10, 0x00, 0x64, 0x61, 0x74, 0x61,
            0x00, 0x00, 0x00, 0x00
        ];

        public Task<GeneratedVoiceReference> GenerateAsync(
            LocalVoiceDesignRequest request,
            CancellationToken cancellationToken) => Task.FromResult(
                new GeneratedVoiceReference(
                    WavBytes,
                    "audio/wav",
                    "qwen3-tts-1.7b-voice-design-test",
                    "cpu",
                    48000,
                    0));
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
                            ["推荐信", "椅子"],
                            "建立目标与行动空间",
                            "对白保持简洁，突出外乡人的直率。",
                            projectSettings.TargetEpisodeSeconds,
                            "快速建立空间后，以稳定节奏推进人物入场。",
                            "开场全景的陌生感对比结尾中景的人物决心。",
                            [
                                new AdaptationShotPlanDraft(
                                    1,
                                    projectSettings.TargetEpisodeSeconds * .4,
                                    "全景",
                                    "平视",
                                    "固定",
                                    "建立巴黎街道与人物位置"),
                                new AdaptationShotPlanDraft(
                                    2,
                                    projectSettings.TargetEpisodeSeconds * .6,
                                    "中景",
                                    "平视",
                                    "缓慢推进",
                                    "推进达达尼昂进入行动空间")
                            ])],
                        ["推荐信不翼而飞"],
                        ["幕后势力首次现身"])).ToArray(),
                    "gpt-5.4-test",
                    "Test Harness",
                    ["三集持续升级达达尼昂的入局压力"],
                    ["最终揭示阴谋指向王后"]));

        public Task<ProductionScriptEpisodeDraft> WriteProductionScriptAsync(
            ProjectSettingsView projectSettings,
            StoryMaterialAnalysisView analysis,
            AdaptationEpisodeDraft outline,
            ProductionScriptEpisodeDraft? previousScript,
            string? correction,
            CancellationToken cancellationToken) => Task.FromResult(
                new ProductionScriptEpisodeDraft(
                    outline.Title,
                    outline.Logline,
                    outline.TargetSeconds,
                    [new ProductionScriptSceneDraft(
                        1,
                        "外景 · 巴黎街道 · 日",
                        "达达尼昂进入巴黎。",
                        "达达尼昂攥紧推荐信，穿过拥挤的街道，抬头寻找特雷维尔府邸。",
                        [new ScreenplayDialogueDraft(
                            "达达尼昂",
                            "坚定地",
                            ["巴黎，我来了。", "特雷维尔先生一定会见我。"] )],
                        ["达达尼昂"],
                        ["推荐信"],
                        "以行动和对白建立目标",
                        outline.TargetSeconds,
                        "快速建立空间后推进人物目标。",
                        "陌生城市的全景对比人物坚定的近景。",
                        [
                            new AdaptationShotPlanDraft(
                                1,
                                outline.TargetSeconds * .4,
                                "全景",
                                "平视",
                                "固定",
                                "建立巴黎街道与人物位置"),
                            new AdaptationShotPlanDraft(
                                2,
                                outline.TargetSeconds * .6,
                                "中景",
                                "平视",
                                "缓慢推进",
                                "呈现人物行动与对白")
                        ])],
                    outline.SmallHooks ?? [],
                    outline.BigHooks ?? []));
    }

    private sealed class TestStoryboardDesigner : IStoryboardDesigner
    {
        public Task<StoryboardDesignResult> DesignAsync(
            ProjectSettingsView settings,
            ProductionScriptPackageView scriptPackage,
            IReadOnlyList<VisualAssetView> assets,
            CancellationToken cancellationToken)
        {
            var scenes = scriptPackage.Episode.Scenes;
            return Task.FromResult(
                new StoryboardDesignResult(
                    scenes.SelectMany((scene, sceneIndex) => new[]
                    {
                        new StoryboardShotDraft(
                            scene.SceneNumber,
                            1,
                            2,
                            "全景",
                            "平视",
                            "固定",
                            "建立场景空间与人物关系",
                            scene.Heading,
                            scene.Action,
                            string.Empty,
                            "环境声",
                            scene.Characters,
                            [],
                            null,
                            "direct-first-frame",
                            "人物保持同一朝向与站位，单一首帧足以约束镜头。",
                            $"{scene.Heading}内，人物保持初始站位并看向行动方向。",
                            string.Empty,
                            "0.0-1.0 秒建立空间；1.0-2.0 秒保持机位，让人物完成单一方向动作后切出。"),
                        new StoryboardShotDraft(
                            scene.SceneNumber,
                            2,
                            3,
                            "中景",
                            "平视",
                            "缓慢推进",
                            "主体位于画面视觉中心",
                            scene.Summary,
                            scene.Action,
                            string.Join(" ", scene.Dialogues.SelectMany(dialogue =>
                                dialogue.Lines.Select(line => $"{dialogue.Character}：{line}"))),
                            "动作声与对白",
                            scene.Characters,
                            [],
                            [
                                .. sceneIndex == 0
                                    ? (scriptPackage.Episode.SmallHooks ?? [])
                                        .Select(item => new StoryboardHookDraft("small", item))
                                    : [],
                                .. sceneIndex == scenes.Count - 1
                                    ? (scriptPackage.Episode.BigHooks ?? [])
                                        .Select(item => new StoryboardHookDraft("big", item))
                                    : []
                                    ],
                                    "first-last-continuous",
                                    "人物从背对镜头转为正面，结束朝向必须由尾帧明确约束。",
                                    "人物背对镜头位于画面中心，双手自然垂下。",
                                    "人物完成转身后正对镜头，视线落向画外对手。",
                                    "0.0-1.0 秒从背面中景开始；1.0-2.5 秒人物向左转身，镜头缓慢推进并保持轴线；2.5-3.0 秒在正面视线落定时切出。")
                    }).ToArray(),
                    "gpt-5.4-test",
                    "Test Harness"));
        }
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

public sealed class TestComfyUiVideoClient : IComfyUiVideoClient
{
    private readonly byte[] mp4 = CreateMp4();

    public ComfyUiVideoSubmission? LastSubmission { get; private set; }

    public void Reset() => LastSubmission = null;

    public Task<string> SubmitAsync(
        ComfyUiVideoSubmission submission,
        CancellationToken cancellationToken)
    {
        LastSubmission = submission;
        return Task.FromResult($"test-prompt-{Guid.NewGuid():N}");
    }

    public Task<ComfyUiJobResult> GetResultAsync(
        string baseUrl,
        string promptId,
        CancellationToken cancellationToken) => Task.FromResult(
            new ComfyUiJobResult(
                true,
                false,
                null,
                new ComfyUiVideoOutput("shot.mp4", string.Empty, "output")));

    public Task<byte[]> DownloadAsync(
        string baseUrl,
        ComfyUiVideoOutput output,
        CancellationToken cancellationToken) => Task.FromResult(mp4);

    private static byte[] CreateMp4()
    {
        var bytes = new byte[2048];
        "ftyp"u8.CopyTo(bytes.AsSpan(4));
        return bytes;
    }
}

public sealed class TestComfyUiWorkflowProvider : IComfyUiWorkflowProvider
{
        public Task<string> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(
                """
                {
                    "1":{"class_type":"LoadImage","inputs":{"image":"{{FIRST_FRAME}}"}},
                    "2":{"class_type":"LoadImage","inputs":{"image":"{{LAST_FRAME}}"}},
                    "7":{"class_type":"MiniMaxH3ImageToVideo","inputs":{"first_frame":["1",0],"last_frame":["2",0],"prompt":"{{PROMPT}}","width":"{{WIDTH}}","height":"{{HEIGHT}}","length":"{{FRAME_COUNT}}"}},
                    "8":{"class_type":"RandomNoise","inputs":{"noise_seed":1}},
                    "16":{"class_type":"SaveVideo","inputs":{"filename_prefix":"{{OUTPUT_PREFIX}}"}},
                    "15":{"class_type":"CreateVideo","inputs":{"fps":"{{FPS}}"}}
                }
                """);
}