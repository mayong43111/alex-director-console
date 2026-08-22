using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using AlexDirectorConsole.V2.Api.Features.Agents;
using AlexDirectorConsole.V2.Api.Features.Copilot;
using AlexDirectorConsole.V2.Api.Features.Generation;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.Projects.Assets;
using AlexDirectorConsole.V2.Api.Features.Projects.Sources;
using AlexDirectorConsole.V2.Api.Features.Projects.Storyboard;
using AlexDirectorConsole.V2.Api.Features.Projects.Voice;
using AlexDirectorConsole.V2.Api.Features.Sessions;
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
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

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
            services.RemoveAll<IComfyUiDialogueClient>();
            services.RemoveAll<IComfyUiWorkflowProvider>();
            services.RemoveAll<IHostedService>();
            services.RemoveAll<ISessionAgent>();
            services.RemoveAll<IProjectCoverGenerator>();
            services.RemoveAll<IProjectCoverPromptWriter>();
            services.RemoveAll<IShotFrameGenerator>();
            services.RemoveAll<ILocalVoiceDesigner>();
            services.RemoveAll<IProjectSettingsAssistant>();
            services.RemoveAll<IAgentTextInvoker>();
            services.RemoveAll<IStoryMaterialAnalyzer>();
            services.RemoveAll<IAdaptationScriptWriter>();
            services.RemoveAll<IStoryboardDesigner>();
            services.RemoveAll<IStoryboardShotTextRewriter>();
            services.RemoveAll<IShotVideoPromptAgent>();
            services.AddDbContext<V2DbContext>(options =>
                options.UseSqlite($"Data Source={databasePath};Pooling=False"));
            services.AddSingleton<IFoundryConnectionTester, SuccessfulFoundryConnectionTester>();
            services.AddSingleton<IComfyUiConnectionTester, SuccessfulComfyUiConnectionTester>();
            services.AddSingleton<TestComfyUiVideoClient>();
            services.AddSingleton<IComfyUiVideoClient>(provider =>
                provider.GetRequiredService<TestComfyUiVideoClient>());
            services.AddSingleton<IComfyUiDialogueClient, TestComfyUiDialogueClient>();
            services.AddSingleton<IComfyUiWorkflowProvider, TestComfyUiWorkflowProvider>();
            services.AddScoped<ISessionAgent, TestSessionAgent>();
            services.AddSingleton<IProjectCoverGenerator, TestProjectCoverGenerator>();
            services.AddSingleton<TestProjectCoverPromptWriter>();
            services.AddSingleton<IProjectCoverPromptWriter>(provider =>
                provider.GetRequiredService<TestProjectCoverPromptWriter>());
            services.AddSingleton<TestShotFrameGenerator>();
            services.AddSingleton<IShotFrameGenerator>(provider =>
                provider.GetRequiredService<TestShotFrameGenerator>());
            services.AddSingleton<ILocalVoiceDesigner, TestLocalVoiceDesigner>();
            services.AddSingleton<TestProjectSettingsAssistant>();
            services.AddSingleton<IProjectSettingsAssistant>(provider =>
                provider.GetRequiredService<TestProjectSettingsAssistant>());
            services.AddSingleton<TestAgentTextInvoker>();
            services.AddSingleton<IAgentTextInvoker>(provider =>
                provider.GetRequiredService<TestAgentTextInvoker>());
            services.AddSingleton<IStoryMaterialAnalyzer, TestStoryMaterialAnalyzer>();
            services.AddSingleton<IAdaptationScriptWriter, TestAdaptationScriptWriter>();
            services.AddSingleton<IStoryboardDesigner, TestStoryboardDesigner>();
            services.AddSingleton<IStoryboardShotTextRewriter, TestStoryboardShotTextRewriter>();
            services.AddSingleton<TestShotVideoPromptAgent>();
            services.AddSingleton<IShotVideoPromptAgent>(provider =>
                provider.GetRequiredService<TestShotVideoPromptAgent>());
        });
    }

    public async Task ResetDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();
        Services.GetRequiredService<TestComfyUiVideoClient>().Reset();
        Services.GetRequiredService<TestShotFrameGenerator>().Reset();
        Services.GetRequiredService<TestProjectSettingsAssistant>().Reset();
        Services.GetRequiredService<TestProjectCoverPromptWriter>().Reset();
        Services.GetRequiredService<TestAgentTextInvoker>().Reset();
        Services.GetRequiredService<TestShotVideoPromptAgent>().Reset();
        var skillSynchronizer = scope.ServiceProvider.GetRequiredService<ISkillCatalogSynchronizer>();
        await skillSynchronizer.SynchronizeAsync();
    }

    public async Task<T> CompleteGenerationTaskAsync<T>(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Accepted)
        {
            throw new InvalidOperationException($"Expected 202 but received {(int)response.StatusCode}.");
        }
        using var taskDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var taskId = taskDocument.RootElement.GetProperty("id").GetGuid();
        await Services.GetRequiredService<GenerationTaskJob>().ExecuteAsync(taskId, CancellationToken.None);

        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
        var task = await dbContext.AgentTasks.AsNoTracking().SingleAsync(item => item.Id == taskId);
        if (task.Status != "completed")
        {
            throw new InvalidOperationException(task.LastError ?? $"Generation task ended as {task.Status}.");
        }
        var resultJson = await dbContext.AgentTaskEvents.AsNoTracking()
            .Where(item => item.TaskId == taskId && item.EventType == "result")
            .OrderByDescending(item => item.Sequence)
            .Select(item => item.DataJson)
            .FirstAsync();
        return JsonSerializer.Deserialize<T>(resultJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Generation task result was empty.");
    }

    public async Task<AgentTask> FailGenerationTaskAsync(HttpResponseMessage response)
    {
        using var taskDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var taskId = taskDocument.RootElement.GetProperty("id").GetGuid();
        try
        {
            await Services.GetRequiredService<GenerationTaskJob>().ExecuteAsync(taskId, CancellationToken.None);
        }
        catch
        {
        }
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<V2DbContext>()
            .AgentTasks.AsNoTracking().SingleAsync(item => item.Id == taskId);
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

    private sealed class TestSessionAgent : ISessionAgent
    {
        public Task<SessionAgentReply> ReplyAsync(
            AgentView agent,
            SessionAgentContext context,
            IReadOnlyList<SessionHistoryMessage> history,
            string message,
            CancellationToken cancellationToken) => Task.FromResult(
                new SessionAgentReply(
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

    public IReadOnlyList<ProjectCoverPromptWriterRequest> ProjectCoverPromptWriterCalls =>
        Services.GetRequiredService<TestProjectCoverPromptWriter>().Calls;

    private sealed class TestProjectCoverPromptWriter : IProjectCoverPromptWriter
    {
        private readonly ConcurrentQueue<ProjectCoverPromptWriterRequest> calls = new();

        public IReadOnlyList<ProjectCoverPromptWriterRequest> Calls => calls.ToArray();

        public Task<ProjectCoverPromptWriterResult> WriteAsync(
            ProjectCoverPromptWriterRequest request,
            CancellationToken cancellationToken)
        {
            calls.Enqueue(request with { ProjectContext = request.ProjectContext.Clone() });
            return Task.FromResult(new ProjectCoverPromptWriterResult(
                $"Agent-authored cinematic cover prompt v{calls.Count}",
                "gpt-5.4",
                "test"));
        }

        public void Reset()
        {
            while (calls.TryDequeue(out _)) { }
        }
    }

    public IReadOnlyList<ShotFrameGenerationCall> ShotFrameCalls =>
        Services.GetRequiredService<TestShotFrameGenerator>().Calls;

    public sealed record ShotFrameGenerationCall(
        string Prompt,
        IReadOnlyList<ShotFrameReference> References);

    private sealed class TestShotFrameGenerator : IShotFrameGenerator
    {
        private static readonly byte[] PngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        private readonly ConcurrentQueue<ShotFrameGenerationCall> calls = new();

        public IReadOnlyList<ShotFrameGenerationCall> Calls => calls.ToArray();

        public void Reset()
        {
            while (calls.TryDequeue(out _)) { }
        }

        public Task<GeneratedShotFrame> GenerateAsync(
            string prompt,
            string size,
            IReadOnlyList<ShotFrameReference> references,
            CancellationToken cancellationToken)
        {
            calls.Enqueue(new ShotFrameGenerationCall(prompt, references.ToArray()));
            return Task.FromResult(new GeneratedShotFrame(
                    PngBytes,
                    "image/png",
                    ".png",
                    "gpt-image-2",
                    "medium",
                    prompt));
        }
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

    private sealed class TestComfyUiDialogueClient : IComfyUiDialogueClient
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

        public Task<GeneratedDialogueAudio> GenerateAsync(
            ComfyUiDialogueRequest request,
            CancellationToken cancellationToken) => Task.FromResult(
                new GeneratedDialogueAudio(WavBytes, 48000, 0));
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
            ProjectSourceView source,
            StoryMaterialAnalysisView analysis,
            int? desiredEpisodeCount,
            string? instruction,
            CancellationToken cancellationToken)
        {
            var episodeCount = desiredEpisodeCount ?? Math.Clamp(analysis.PlotBeats.Count, 1, 6);
            return Task.FromResult(
                new AdaptationScriptResult(
                    "初到巴黎",
                    "合并前两章并以得到接见作为单集收束。",
                    Enumerable.Range(1, episodeCount).Select(number => new AdaptationEpisodeDraft(
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
        }

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

    private sealed class TestStoryboardShotTextRewriter : IStoryboardShotTextRewriter
    {
        public Task<StoryboardShotTextRevision> RewriteAsync(
            StoryboardShotView shot,
            string instruction,
            CancellationToken cancellationToken) => Task.FromResult(new StoryboardShotTextRevision(
                $"{shot.VisualDescription}；已按意见调整：{instruction}",
                shot.Action,
                $"已按意见重新判断：{instruction}",
                $"{shot.FirstFrameDescription}；调整要求：{instruction}",
                shot.ProductionMode == ShotProductionModes.FirstLastContinuous
                    ? $"{shot.LastFrameDescription}；调整要求：{instruction}"
                    : "",
                $"{shot.CutDescription}；调整要求：{instruction}",
                shot.Dialogue,
                shot.Sound,
                "gpt-5.4-test",
                "Test Harness"));
    }

    private sealed class TestShotVideoPromptAgent : IShotVideoPromptAgent
    {
        public ShotVideoPromptAgentInput? LastInput { get; private set; }
        public int CallCount { get; private set; }

        public void Reset()
        {
            LastInput = null;
            CallCount = 0;
        }

        public Task<ShotVideoPromptDraft> GenerateAsync(
            ShotVideoPromptAgentInput input,
            CancellationToken cancellationToken)
        {
            LastInput = input;
            CallCount++;
            return Task.FromResult(new ShotVideoPromptDraft(
                "Hold the fixed framing while the host presents the wallet and simple scene icons appear in sequence.",
                "Use the linked character voice profile, natural Mandarin articulation, and exact lip sync.",
                "Use restrained natural production sound below the voice.",
                "Preserve identities, wardrobe, lighting, spatial relationships, and camera axis."));
        }
    }

    private sealed class TestProjectSettingsAssistant : IProjectSettingsAssistant
    {
        public ProjectSettingsAssistRequest? LastRequest { get; private set; }

        public void Reset() => LastRequest = null;

        public Task<ProjectSettingsAssistView> WriteAsync(
            ProjectSettingsAssistRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new ProjectSettingsAssistView(
                    request.Field ?? string.Empty,
                    $"AI 优化：{request.CurrentValue}",
                    "gpt-5.4",
                    "MAF HarnessAgent"));
        }
    }

    private sealed class TestAgentTextInvoker : IAgentTextInvoker
    {
        public AgentTextInvocation? LastInvocation { get; private set; }

        public void Reset() => LastInvocation = null;

        public Task<AgentTextInvocationResult> InvokeAsync(
            AgentTextInvocation invocation,
            CancellationToken cancellationToken)
        {
            LastInvocation = invocation;
            return Task.FromResult(new AgentTextInvocationResult(
                $"Agent 候选：{invocation.Input}",
                "gpt-5.4",
                "Test Harness"));
        }
    }

    public ProjectSettingsAssistRequest? LastProjectSettingsAssistRequest =>
        Services.GetRequiredService<TestProjectSettingsAssistant>().LastRequest;

    public AgentTextInvocation? LastAgentTextInvocation =>
        Services.GetRequiredService<TestAgentTextInvoker>().LastInvocation;

    public ShotVideoPromptAgentInput? LastShotVideoPromptAgentInput =>
        Services.GetRequiredService<TestShotVideoPromptAgent>().LastInput;

    public int ShotVideoPromptAgentCallCount =>
        Services.GetRequiredService<TestShotVideoPromptAgent>().CallCount;
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