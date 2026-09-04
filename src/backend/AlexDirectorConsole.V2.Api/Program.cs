using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Agents;
using AlexDirectorConsole.V2.Api.Features.Copilot;
using AlexDirectorConsole.V2.Api.Features.Generation;
using AlexDirectorConsole.V2.Api.Features.Projects;
using AlexDirectorConsole.V2.Api.Features.Projects.Assets;
using AlexDirectorConsole.V2.Api.Features.Projects.CreateProject;
using AlexDirectorConsole.V2.Api.Features.Projects.ManageProject;
using AlexDirectorConsole.V2.Api.Features.Projects.Production;
using AlexDirectorConsole.V2.Api.Features.Projects.Queries;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.Projects.Sources;
using AlexDirectorConsole.V2.Api.Features.Projects.Storyboard;
using AlexDirectorConsole.V2.Api.Features.Projects.Versions;
using AlexDirectorConsole.V2.Api.Features.Projects.Voice;
using AlexDirectorConsole.V2.Api.Features.Projects.DigitalPresenters;
using AlexDirectorConsole.V2.Api.Features.Sessions;
using AlexDirectorConsole.V2.Api.Features.Skills;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.ComfyUi;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.VoicePackages;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.VoiceTraining;
using AlexDirectorConsole.V2.Database.Data;
using Azure.Core;
using Azure.Identity;
using Hangfire;
using Hangfire.InMemory;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using AlexDirectorConsole.V2.Database.Models;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("V2Database")
    ?? $"Data Source={DatabasePaths.GetDefaultDatabasePath()}";
var databaseProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var useSqlServer = databaseProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase);
if (!useSqlServer)
{
    DatabasePaths.EnsureDatabaseDirectory(connectionString);
}

builder.Services.AddProblemDetails();
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("AlexDirectorConsole.V2");
if (!builder.Environment.IsEnvironment("Testing"))
{
    var dataProtectionBlobUri = builder.Configuration["DataProtection:BlobUri"];
    if (!string.IsNullOrWhiteSpace(dataProtectionBlobUri))
    {
        var managedIdentityClientId = builder.Configuration["Azure:ManagedIdentityClientId"];
        TokenCredential credential = string.IsNullOrWhiteSpace(managedIdentityClientId)
            ? new DefaultAzureCredential()
            : new ManagedIdentityCredential(managedIdentityClientId);
        dataProtection.PersistKeysToAzureBlobStorage(new Uri(dataProtectionBlobUri), credential);
    }
    else
    {
        var configuredDataProtectionPath = builder.Configuration["DataProtection:KeysPath"];
        var dataProtectionPath = string.IsNullOrWhiteSpace(configuredDataProtectionPath)
            ? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtection")
            : Path.GetFullPath(configuredDataProtectionPath);
        Directory.CreateDirectory(dataProtectionPath);
        dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
        if (OperatingSystem.IsWindows())
        {
            dataProtection.ProtectKeysWithDpapi();
        }
    }
}
builder.Services.AddDbContext<V2DbContext>(options =>
{
    if (useSqlServer)
    {
        options.UseSqlServer(connectionString, sqlServer => sqlServer.EnableRetryOnFailure());
        return;
    }

    options.UseSqlite(connectionString);
});
builder.Services.AddHangfire(configuration =>
{
    configuration.UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings();
    if (useSqlServer)
    {
        configuration.UseSqlServerStorage(connectionString);
        return;
    }

    configuration.UseInMemoryStorage();
});
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHangfireServer();
}
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IFoundryConnectionTester, AzureFoundryConnectionTester>();
builder.Services.AddHttpClient("ComfyUi", client => client.Timeout = TimeSpan.FromMinutes(2))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseProxy = false });
builder.Services.AddHttpClient("ComfyUiVideo", client => client.Timeout = TimeSpan.FromMinutes(5))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseProxy = false });
builder.Services.AddHttpClient("ComfyUiImage", client => client.Timeout = TimeSpan.FromMinutes(5))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseProxy = false });
builder.Services.AddHttpClient<ILocalVoiceDesigner, LocalQwenVoiceDesigner>((provider, client) =>
{
    var baseUrl = provider.GetRequiredService<IConfiguration>()["LocalTts:BaseUrl"]
        ?? "http://127.0.0.1:8010";
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromMinutes(30);
});
builder.Services.AddSingleton<IComfyUiConnectionTester, ComfyUiConnectionTester>();
builder.Services.AddSingleton<IComfyUiVideoClient, ComfyUiVideoClient>();
builder.Services.AddHttpClient<IGptSoVitsDialogueClient, GptSoVitsDialogueClient>((provider, client) =>
{
    var baseUrl = provider.GetRequiredService<IConfiguration>()["GptSoVits:BaseUrl"]
        ?? "http://127.0.0.1:9880";
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromMinutes(30);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseProxy = false });
builder.Services.AddHttpClient("GptSoVitsReferenceUpload", (provider, client) =>
{
    var baseUrl = provider.GetRequiredService<IConfiguration>()["GptSoVits:ReferenceUploadBaseUrl"]
        ?? "http://127.0.0.1:50010";
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromMinutes(5);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseProxy = false });
builder.Services.AddHttpClient<ICosyVoiceDialogueClient, CosyVoiceDialogueClient>((provider, client) =>
{
    var baseUrl = provider.GetRequiredService<IConfiguration>()["CosyVoice:BaseUrl"]
        ?? "http://127.0.0.1:50000";
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromMinutes(30);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseProxy = false });
builder.Services.AddHttpClient<IVoiceTrainingWorkerClient, VoiceTrainingWorkerClient>((provider, client) =>
{
    var baseUrl = provider.GetRequiredService<IConfiguration>()["VoiceTraining:BaseUrl"]
        ?? "http://127.0.0.1:50010";
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromMinutes(30);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseProxy = false });
builder.Services.AddScoped<IVoicePackageDialogueGenerator, VoicePackageDialogueGenerator>();
builder.Services.AddSingleton<IComfyUiWorkflowProvider, PackagedComfyUiWorkflowProvider>();
builder.Services.AddSingleton<IComfyUiImageClient, ComfyUiImageClient>();
builder.Services.AddSingleton<IComfyUiImageWorkflowProvider, PackagedComfyUiImageWorkflowProvider>();
builder.Services.AddScoped<IShotImagePromptAgent, MafShotImagePromptAgent>();
builder.Services.AddScoped<IShotVideoPromptAgent, MafShotVideoPromptAgent>();
builder.Services.AddScoped<IShotVideoService, ShotVideoService>();
builder.Services.AddScoped<IStoryboardMediaPromptService, StoryboardMediaPromptService>();
builder.Services.AddScoped<IStoryboardMediaBatchService, StoryboardMediaBatchService>();
builder.Services.AddScoped<IStoryboardDialogueAudioService, StoryboardDialogueAudioService>();
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<ShotVideoWorker>();
}
builder.Services.AddSingleton<ISkillCatalog, SkillCatalog>();
builder.Services.AddSingleton<IAgentCatalog, AgentCatalog>();
builder.Services.AddScoped<ISkillCatalogSynchronizer, SkillCatalogSynchronizer>();
builder.Services.AddScoped<IDefaultVoicePackageSynchronizer, DefaultVoicePackageSynchronizer>();
builder.Services.AddScoped<ISessionAgent, MafSessionAgent>();
builder.Services.AddSingleton<SessionAgentTaskCancellation>();
builder.Services.AddSingleton<SessionAgentExecutionContext>();
builder.Services.AddTransient<SessionAgentTaskJob>();
builder.Services.AddScoped<IGenerationTaskScheduler, GenerationTaskScheduler>();
builder.Services.AddTransient<GenerationTaskJob>();
builder.Services.AddTransient<GenerationTaskRecoveryJob>();
builder.Services.AddHttpClient<IProjectCoverGenerator, AzureFoundryProjectCoverGenerator>(client =>
    client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddHttpClient<IShotFrameGenerator, AzureFoundryShotFrameGenerator>(client =>
    client.Timeout = TimeSpan.FromMinutes(10));
builder.Services.AddScoped<IProjectCoverService, ProjectCoverService>();
builder.Services.AddScoped<IProjectCoverPromptWriter, MafProjectCoverPromptWriter>();
builder.Services.AddScoped<IVisualReferencePromptWriter, MafVisualReferencePromptWriter>();
builder.Services.AddScoped<IVisualReferenceService, VisualReferenceService>();
builder.Services.AddScoped<IVoiceProfileService, VoiceProfileService>();
builder.Services.AddScoped<IShotFrameService, ShotFrameService>();
builder.Services.AddScoped<IProjectSettingsAssistant, MafProjectSettingsAssistant>();
builder.Services.AddScoped<IAgentTextInvoker, MafAgentTextInvoker>();
builder.Services.AddScoped<IProjectSettingsToolService, ProjectSettingsToolService>();
builder.Services.AddScoped<IStoryProductionToolService, StoryProductionToolService>();
builder.Services.AddScoped<IVisualAssetProductionToolService, VisualAssetProductionToolService>();
builder.Services.AddScoped<IStoryMaterialAnalyzer, MafStoryMaterialAnalyzer>();
builder.Services.AddScoped<IAdaptationScriptWriter, MafAdaptationScriptWriter>();
builder.Services.AddScoped<IStoryboardDesigner, MafStoryboardDesigner>();
builder.Services.AddScoped<IStoryboardShotTextRewriter, MafStoryboardShotTextRewriter>();
builder.Services.AddScoped<ICommandDispatcher, CommandDispatcher>();
builder.Services.AddScoped<IQueryDispatcher, QueryDispatcher>();
builder.Services.AddScoped<ICommandHandler<CreateProjectCommand, CreateProjectResult>, CreateProjectCommandHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateProjectCommand, UpdateProjectResult>, UpdateProjectCommandHandler>();
builder.Services.AddScoped<ICommandHandler<DeleteProjectCommand, DeleteProjectResult>, DeleteProjectCommandHandler>();
builder.Services.AddScoped<IQueryHandler<ListProjectsQuery, IReadOnlyList<ProjectView>>, ListProjectsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetProjectQuery, ProjectView?>, GetProjectQueryHandler>();
builder.Services.AddScoped<IQueryHandler<ListProductionEpisodesQuery, IReadOnlyList<ProductionEpisodeView>>, ListProductionEpisodesQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetProjectSettingsQuery, ProjectSettingsView?>, GetProjectSettingsQueryHandler>();
builder.Services.AddScoped<ICommandHandler<SaveProjectSettingsCommand, SaveProjectSettingsResult>, SaveProjectSettingsCommandHandler>();
builder.Services.AddScoped<IQueryHandler<ListProjectSourcesQuery, IReadOnlyList<ProjectSourceView>>, ListProjectSourcesQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetProjectSourceQuery, ProjectSourceView?>, GetProjectSourceQueryHandler>();
builder.Services.AddScoped<ICommandHandler<CreateProjectSourceCommand, CreateProjectSourceResult>, CreateProjectSourceCommandHandler>();
builder.Services.AddScoped<ICommandHandler<AppendProjectSourceChaptersCommand, CreateProjectSourceResult>, AppendProjectSourceChaptersCommandHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateProjectSourceChapterCommand, CreateProjectSourceResult>, UpdateProjectSourceChapterCommandHandler>();
builder.Services.AddScoped<ICommandHandler<DeleteProjectSourceChapterCommand, CreateProjectSourceResult>, DeleteProjectSourceChapterCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetStoryMaterialAnalysisQuery, StoryMaterialAnalysisView?>, GetStoryMaterialAnalysisQueryHandler>();
builder.Services.AddScoped<ICommandHandler<AnalyzeStoryMaterialCommand, StoryMaterialAnalysisView?>, AnalyzeStoryMaterialCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetAdaptationScriptQuery, AdaptationScriptView?>, GetAdaptationScriptQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetProductionScriptPackageQuery, ProductionScriptPackageView?>, GetProductionScriptPackageQueryHandler>();
builder.Services.AddScoped<ICommandHandler<GenerateAdaptationScriptCommand, AdaptationScriptView?>, GenerateAdaptationScriptCommandHandler>();
builder.Services.AddScoped<ICommandHandler<AppendAdaptationEpisodeCommand, AdaptationScriptView?>, AppendAdaptationEpisodeCommandHandler>();
builder.Services.AddScoped<ICommandHandler<RegenerateAdaptationEpisodeCommand, AdaptationScriptView?>, RegenerateAdaptationEpisodeCommandHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateAdaptationEpisodeCommand, AdaptationScriptView?>, UpdateAdaptationEpisodeCommandHandler>();
builder.Services.AddScoped<ICommandHandler<DeleteAdaptationEpisodeCommand, AdaptationScriptView?>, DeleteAdaptationEpisodeCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ClearAdaptationEpisodesCommand, AdaptationScriptView?>, ClearAdaptationEpisodesCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ConfirmAdaptationScriptCommand, AdaptationScriptView?>, ConfirmAdaptationScriptCommandHandler>();
builder.Services.AddScoped<ICommandHandler<RegenerateProductionScriptCommand, ProductionScriptPackageView?>, RegenerateProductionScriptCommandHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateProductionScriptSceneCommand, ProductionScriptPackageView?>, UpdateProductionScriptSceneCommandHandler>();
builder.Services.AddScoped<IQueryHandler<ListVisualAssetsQuery, IReadOnlyList<VisualAssetView>>, ListVisualAssetsQueryHandler>();
builder.Services.AddScoped<ICommandHandler<SaveVisualAssetCommand, SaveVisualAssetResult>, SaveVisualAssetCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ImportStoryMaterialAssetsCommand, IReadOnlyList<VisualAssetView>?>, ImportStoryMaterialAssetsCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetStoryboardQuery, StoryboardView?>, GetStoryboardQueryHandler>();
builder.Services.AddScoped<ICommandHandler<GenerateStoryboardCommand, StoryboardView?>, GenerateStoryboardCommandHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateStoryboardShotAssetsCommand, StoryboardView?>, UpdateStoryboardShotAssetsCommandHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateStoryboardShotModeCommand, StoryboardView?>, UpdateStoryboardShotModeCommandHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateStoryboardShotTextCommand, StoryboardView?>, UpdateStoryboardShotTextCommandHandler>();
builder.Services.AddScoped<ICommandHandler<RewriteStoryboardShotTextCommand, StoryboardView?>, RewriteStoryboardShotTextCommandHandler>();
builder.Services.AddScoped<ICommandHandler<StartShotProductionCommand, ShotProductionView?>, StartShotProductionCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetFoundryConfigurationQuery, FoundryConfigurationView>, GetFoundryConfigurationHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateFoundryConfigurationCommand, UpdateFoundryConfigurationResult>, UpdateFoundryConfigurationHandler>();
builder.Services.AddScoped<ICommandHandler<TestFoundryConnectionCommand, TestFoundryConnectionResult>, TestFoundryConnectionHandler>();
builder.Services.AddScoped<IQueryHandler<GetComfyUiConfigurationQuery, ComfyUiConfigurationView>, GetComfyUiConfigurationHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateComfyUiConfigurationCommand, UpdateComfyUiConfigurationResult>, UpdateComfyUiConfigurationHandler>();
builder.Services.AddScoped<ICommandHandler<TestComfyUiConnectionCommand, ComfyUiCapabilities>, TestComfyUiConnectionHandler>();
builder.Services.AddScoped<IQueryHandler<ListSkillsQuery, IReadOnlyList<SkillView>>, ListSkillsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetSkillQuery, SkillView?>, GetSkillQueryHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateSkillCommand, SkillView?>, UpdateSkillCommandHandler>();
builder.Services.AddScoped<IQueryHandler<ListAgentsQuery, IReadOnlyList<AgentView>>, ListAgentsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetAgentQuery, AgentView?>, GetAgentQueryHandler>();
builder.Services.AddScoped<ICommandHandler<CreateAgentCommand, SaveAgentResult>, CreateAgentCommandHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateAgentCommand, SaveAgentResult>, UpdateAgentCommandHandler>();
builder.Services.AddScoped<ICommandHandler<DeleteAgentCommand, bool>, DeleteAgentCommandHandler>();
builder.Services.AddScoped<IQueryHandler<ListSessionsQuery, IReadOnlyList<SessionSummaryView>>, ListSessionsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetSessionQuery, SessionView?>, GetSessionQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetScopedSessionQuery, SessionView?>, GetSessionQueryHandler>();
builder.Services.AddScoped<ICommandHandler<SendSessionMessageCommand, SendSessionMessageResult>, SendSessionMessageCommandHandler>();
builder.Services.AddScoped<ICommandHandler<RetrySessionMessageCommand, SendSessionMessageResult>, RetrySessionMessageCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ClearSessionMessagesCommand, bool>, ClearSessionMessagesCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetCopilotConversationQuery, CopilotConversationView?>, GetCopilotConversationQueryHandler>();
builder.Services.AddScoped<ICommandHandler<SendCopilotMessageCommand, SendCopilotMessageResult>, SendCopilotMessageCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ResetCopilotConversationCommand, bool>, ResetCopilotConversationCommandHandler>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
    if (useSqlServer)
    {
        await dbContext.Database.EnsureCreatedAsync();
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            IF OBJECT_ID(N'[AgentTasks]', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH('AgentTasks', 'AgentId') IS NULL
                    ALTER TABLE [AgentTasks] ADD [AgentId] uniqueidentifier NULL;
                IF COL_LENGTH('AgentTasks', 'SessionId') IS NULL
                    ALTER TABLE [AgentTasks] ADD [SessionId] uniqueidentifier NULL;
                IF COL_LENGTH('AgentTasks', 'LeaseOwner') IS NULL
                    ALTER TABLE [AgentTasks] ADD [LeaseOwner] nvarchar(200) NULL;
                IF COL_LENGTH('AgentTasks', 'LeaseExpiresAtUtc') IS NULL
                    ALTER TABLE [AgentTasks] ADD [LeaseExpiresAtUtc] datetimeoffset NULL;
                IF COL_LENGTH('AgentTasks', 'CancellationRequestedAtUtc') IS NULL
                    ALTER TABLE [AgentTasks] ADD [CancellationRequestedAtUtc] datetimeoffset NULL;

                ALTER TABLE [AgentTasks] ALTER COLUMN [ProjectId] uniqueidentifier NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentTasks_AgentId' AND object_id = OBJECT_ID('AgentTasks'))
                    CREATE INDEX [IX_AgentTasks_AgentId] ON [AgentTasks] ([AgentId]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgentTasks_SessionId' AND object_id = OBJECT_ID('AgentTasks'))
                    CREATE INDEX [IX_AgentTasks_SessionId] ON [AgentTasks] ([SessionId]);
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_AgentTasks_AgentDefinitions_AgentId')
                    ALTER TABLE [AgentTasks] ADD CONSTRAINT [FK_AgentTasks_AgentDefinitions_AgentId]
                        FOREIGN KEY ([AgentId]) REFERENCES [AgentDefinitions] ([Id]);
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_AgentTasks_Sessions_SessionId')
                    ALTER TABLE [AgentTasks] ADD CONSTRAINT [FK_AgentTasks_Sessions_SessionId]
                        FOREIGN KEY ([SessionId]) REFERENCES [Sessions] ([Id]) ON DELETE SET NULL;
            END
            """);
    }
    else
    {
        await dbContext.Database.MigrateAsync();
    }
    const string digitalPresenterProjectId = "00000000-0000-0000-0000-000000000001";
    if (!await dbContext.Projects.AnyAsync(item => item.Id == Guid.Parse(digitalPresenterProjectId)))
    {
        var now = app.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        dbContext.Projects.Add(new Project
        {
            Id = Guid.Parse(digitalPresenterProjectId),
            Type = "digital-presenter",
            Name = "数字人工作室",
            Description = "数字人资产与剧集工作区",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await dbContext.SaveChangesAsync();
    }
    var voicePackageSynchronizer = scope.ServiceProvider.GetRequiredService<IDefaultVoicePackageSynchronizer>();
    await voicePackageSynchronizer.SynchronizeAsync();
    var skillSynchronizer = scope.ServiceProvider.GetRequiredService<ISkillCatalogSynchronizer>();
    await skillSynchronizer.SynchronizeAsync();
}

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/v2/health", () => Results.Ok(new { status = "ok" }));
app.MapCreateProject();
app.MapProjectManagement();
app.MapProjectQueries();
app.MapProjectSettings();
app.MapResourceVersions();
app.MapProjectSources();
app.MapStoryMaterialAnalysis();
app.MapAdaptationScripts();
app.MapVisualAssets();
app.MapAudioMaterials();
app.MapDigitalPresenters();
app.MapStoryboards();
app.MapStoryboardMedia();
app.MapStoryboardDialogueAudio();
app.MapShotFrameContent();
app.MapShotVideos();
app.MapProduction();
app.MapFoundryConfiguration();
app.MapComfyUiConfiguration();
app.MapVoicePackages();
app.MapVoiceTraining();
app.MapSkills();
app.MapAgents();
app.MapGenerationTasks();
if (!app.Environment.IsEnvironment("Testing"))
{
    var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
    recurringJobs.AddOrUpdate<GenerationTaskRecoveryJob>(
        "recover-generation-tasks",
        job => job.ExecuteAsync(CancellationToken.None),
        Cron.Minutely);
}
app.MapSessions();
app.MapCopilot();
app.MapFallbackToFile("index.html");
app.Run();

public partial class Program;
