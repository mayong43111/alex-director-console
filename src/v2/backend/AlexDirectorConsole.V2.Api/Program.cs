using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Copilot;
using AlexDirectorConsole.V2.Api.Features.Projects;
using AlexDirectorConsole.V2.Api.Features.Projects.Assets;
using AlexDirectorConsole.V2.Api.Features.Projects.CreateProject;
using AlexDirectorConsole.V2.Api.Features.Projects.Production;
using AlexDirectorConsole.V2.Api.Features.Projects.Queries;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.Projects.Sources;
using AlexDirectorConsole.V2.Api.Features.Projects.Storyboard;
using AlexDirectorConsole.V2.Api.Features.Skills;
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
builder.Services.AddSingleton<ISkillCatalog, SkillCatalog>();
builder.Services.AddScoped<ISkillCatalogSynchronizer, SkillCatalogSynchronizer>();
builder.Services.AddScoped<IProjectCopilotAgent, MafProjectCopilotAgent>();
builder.Services.AddHttpClient<IProjectCoverGenerator, AzureFoundryProjectCoverGenerator>(client =>
    client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddHttpClient<IShotFrameGenerator, AzureFoundryShotFrameGenerator>(client =>
    client.Timeout = TimeSpan.FromMinutes(10));
builder.Services.AddScoped<IProjectCoverService, ProjectCoverService>();
builder.Services.AddScoped<IVisualReferenceService, VisualReferenceService>();
builder.Services.AddScoped<IShotFrameService, ShotFrameService>();
builder.Services.AddScoped<IProjectSettingsAssistant, MafProjectSettingsAssistant>();
builder.Services.AddScoped<IProjectSettingsToolService, ProjectSettingsToolService>();
builder.Services.AddScoped<IStoryMaterialAnalyzer, MafStoryMaterialAnalyzer>();
builder.Services.AddScoped<IAdaptationScriptWriter, MafAdaptationScriptWriter>();
builder.Services.AddScoped<IStoryboardDesigner, MafStoryboardDesigner>();
builder.Services.AddScoped<ICommandDispatcher, CommandDispatcher>();
builder.Services.AddScoped<IQueryDispatcher, QueryDispatcher>();
builder.Services.AddScoped<ICommandHandler<CreateProjectCommand, CreateProjectResult>, CreateProjectCommandHandler>();
builder.Services.AddScoped<IQueryHandler<ListProjectsQuery, IReadOnlyList<ProjectView>>, ListProjectsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetProjectQuery, ProjectView?>, GetProjectQueryHandler>();
builder.Services.AddScoped<IQueryHandler<ListProductionEpisodesQuery, IReadOnlyList<ProductionEpisodeView>>, ListProductionEpisodesQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetProjectSettingsQuery, ProjectSettingsView?>, GetProjectSettingsQueryHandler>();
builder.Services.AddScoped<ICommandHandler<SaveProjectSettingsCommand, SaveProjectSettingsResult>, SaveProjectSettingsCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ApproveProjectSettingsCommand, ProjectSettingsView?>, ApproveProjectSettingsCommandHandler>();
builder.Services.AddScoped<IQueryHandler<ListProjectSourcesQuery, IReadOnlyList<ProjectSourceView>>, ListProjectSourcesQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetProjectSourceQuery, ProjectSourceView?>, GetProjectSourceQueryHandler>();
builder.Services.AddScoped<ICommandHandler<CreateProjectSourceCommand, CreateProjectSourceResult>, CreateProjectSourceCommandHandler>();
builder.Services.AddScoped<ICommandHandler<AppendProjectSourceChaptersCommand, CreateProjectSourceResult>, AppendProjectSourceChaptersCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetStoryMaterialAnalysisQuery, StoryMaterialAnalysisView?>, GetStoryMaterialAnalysisQueryHandler>();
builder.Services.AddScoped<ICommandHandler<AnalyzeStoryMaterialCommand, StoryMaterialAnalysisView?>, AnalyzeStoryMaterialCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetAdaptationScriptQuery, AdaptationScriptView?>, GetAdaptationScriptQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetProductionScriptPackageQuery, ProductionScriptPackageView?>, GetProductionScriptPackageQueryHandler>();
builder.Services.AddScoped<ICommandHandler<GenerateAdaptationScriptCommand, AdaptationScriptView?>, GenerateAdaptationScriptCommandHandler>();
builder.Services.AddScoped<ICommandHandler<AppendAdaptationEpisodeCommand, AdaptationScriptView?>, AppendAdaptationEpisodeCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ConfirmAdaptationScriptCommand, AdaptationScriptView?>, ConfirmAdaptationScriptCommandHandler>();
builder.Services.AddScoped<IQueryHandler<ListVisualAssetsQuery, IReadOnlyList<VisualAssetView>>, ListVisualAssetsQueryHandler>();
builder.Services.AddScoped<ICommandHandler<SaveVisualAssetCommand, SaveVisualAssetResult>, SaveVisualAssetCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ImportStoryMaterialAssetsCommand, IReadOnlyList<VisualAssetView>?>, ImportStoryMaterialAssetsCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetStoryboardQuery, StoryboardView?>, GetStoryboardQueryHandler>();
builder.Services.AddScoped<ICommandHandler<GenerateStoryboardCommand, StoryboardView?>, GenerateStoryboardCommandHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateStoryboardShotAssetsCommand, StoryboardView?>, UpdateStoryboardShotAssetsCommandHandler>();
builder.Services.AddScoped<ICommandHandler<StartShotProductionCommand, ShotProductionView?>, StartShotProductionCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetFoundryConfigurationQuery, FoundryConfigurationView>, GetFoundryConfigurationHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateFoundryConfigurationCommand, UpdateFoundryConfigurationResult>, UpdateFoundryConfigurationHandler>();
builder.Services.AddScoped<ICommandHandler<TestFoundryConnectionCommand, TestFoundryConnectionResult>, TestFoundryConnectionHandler>();
builder.Services.AddScoped<IQueryHandler<ListSkillsQuery, IReadOnlyList<SkillView>>, ListSkillsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetSkillQuery, SkillView?>, GetSkillQueryHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateSkillCommand, SkillView?>, UpdateSkillCommandHandler>();
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
app.MapProjectQueries();
app.MapProjectSettings();
app.MapProjectSources();
app.MapStoryMaterialAnalysis();
app.MapAdaptationScripts();
app.MapVisualAssets();
app.MapStoryboards();
app.MapShotFrameContent();
app.MapProduction();
app.MapFoundryConfiguration();
app.MapSkills();
app.MapCopilot();
app.Run();

public partial class Program;
