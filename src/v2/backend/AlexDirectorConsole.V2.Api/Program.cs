using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Copilot;
using AlexDirectorConsole.V2.Api.Features.Projects;
using AlexDirectorConsole.V2.Api.Features.Projects.CreateProject;
using AlexDirectorConsole.V2.Api.Features.Projects.Queries;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
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
builder.Services.AddScoped<IProjectCoverService, ProjectCoverService>();
builder.Services.AddScoped<IProjectSettingsAssistant, MafProjectSettingsAssistant>();
builder.Services.AddScoped<IProjectSettingsToolService, ProjectSettingsToolService>();
builder.Services.AddScoped<ICommandDispatcher, CommandDispatcher>();
builder.Services.AddScoped<IQueryDispatcher, QueryDispatcher>();
builder.Services.AddScoped<ICommandHandler<CreateProjectCommand, CreateProjectResult>, CreateProjectCommandHandler>();
builder.Services.AddScoped<IQueryHandler<ListProjectsQuery, IReadOnlyList<ProjectView>>, ListProjectsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetProjectQuery, ProjectView?>, GetProjectQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetProjectSettingsQuery, ProjectSettingsView?>, GetProjectSettingsQueryHandler>();
builder.Services.AddScoped<ICommandHandler<SaveProjectSettingsCommand, SaveProjectSettingsResult>, SaveProjectSettingsCommandHandler>();
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
app.MapFoundryConfiguration();
app.MapSkills();
app.MapCopilot();
app.Run();

public partial class Program;
