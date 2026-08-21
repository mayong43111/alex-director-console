using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Agents;
using AlexDirectorConsole.V2.Api.Features.Copilot;
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
using AlexDirectorConsole.V2.Api.Features.Sessions;
using AlexDirectorConsole.V2.Api.Features.Skills;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.ComfyUi;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;
using AlexDirectorConsole.V2.Database.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("V2Database")
    ?? $"Data Source={DatabasePaths.GetDefaultDatabasePath()}";
DatabasePaths.EnsureDatabaseDirectory(connectionString);

builder.Services.AddProblemDetails();
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("AlexDirectorConsole.V2");
if (!builder.Environment.IsEnvironment("Testing"))
{
    var dataProtectionPath = Path.Combine(
        builder.Environment.ContentRootPath,
        "App_Data",
        "DataProtection");
    Directory.CreateDirectory(dataProtectionPath);
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
    if (OperatingSystem.IsWindows())
    {
        dataProtection.ProtectKeysWithDpapi();
    }
}
builder.Services.AddDbContext<V2DbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IFoundryConnectionTester, AzureFoundryConnectionTester>();
builder.Services.AddHttpClient("ComfyUi", client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient("ComfyUiVideo", client => client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddHttpClient("ComfyUiImage", client => client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddHttpClient<ILocalVoiceDesigner, LocalQwenVoiceDesigner>((provider, client) =>
{
    var baseUrl = provider.GetRequiredService<IConfiguration>()["LocalTts:BaseUrl"]
        ?? "http://127.0.0.1:8010";
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromMinutes(30);
});
builder.Services.AddSingleton<IComfyUiConnectionTester, ComfyUiConnectionTester>();
builder.Services.AddSingleton<IComfyUiVideoClient, ComfyUiVideoClient>();
builder.Services.AddSingleton<IComfyUiWorkflowProvider, PackagedComfyUiWorkflowProvider>();
builder.Services.AddSingleton<IComfyUiImageClient, ComfyUiImageClient>();
builder.Services.AddSingleton<IComfyUiImageWorkflowProvider, PackagedComfyUiImageWorkflowProvider>();
builder.Services.AddScoped<IShotVideoService, ShotVideoService>();
builder.Services.AddScoped<IStoryboardMediaPromptService, StoryboardMediaPromptService>();
builder.Services.AddScoped<IStoryboardMediaBatchService, StoryboardMediaBatchService>();
builder.Services.AddHostedService<ShotVideoWorker>();
builder.Services.AddSingleton<ISkillCatalog, SkillCatalog>();
builder.Services.AddScoped<ISkillCatalogSynchronizer, SkillCatalogSynchronizer>();
builder.Services.AddScoped<ISessionAgent, MafSessionAgent>();
builder.Services.AddHttpClient<IProjectCoverGenerator, AzureFoundryProjectCoverGenerator>(client =>
    client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddHttpClient<IShotFrameGenerator, AzureFoundryShotFrameGenerator>(client =>
    client.Timeout = TimeSpan.FromMinutes(10));
builder.Services.AddScoped<IProjectCoverService, ProjectCoverService>();
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
    await dbContext.Database.MigrateAsync();
    var skillSynchronizer = scope.ServiceProvider.GetRequiredService<ISkillCatalogSynchronizer>();
    await skillSynchronizer.SynchronizeAsync();
}

app.UseExceptionHandler();
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
app.MapStoryboards();
app.MapStoryboardMedia();
app.MapShotFrameContent();
app.MapShotVideos();
app.MapProduction();
app.MapFoundryConfiguration();
app.MapComfyUiConfiguration();
app.MapSkills();
app.MapAgents();
app.MapSessions();
app.MapCopilot();
app.Run();

public partial class Program;
