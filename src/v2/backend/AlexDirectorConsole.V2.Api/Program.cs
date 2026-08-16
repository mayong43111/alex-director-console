using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Projects;
using AlexDirectorConsole.V2.Api.Features.Projects.CreateProject;
using AlexDirectorConsole.V2.Api.Features.Projects.Queries;
using AlexDirectorConsole.V2.Database.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("V2Database")
    ?? $"Data Source={DatabasePaths.GetDefaultDatabasePath()}";
DatabasePaths.EnsureDatabaseDirectory(connectionString);

builder.Services.AddProblemDetails();
builder.Services.AddDbContext<V2DbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ICommandDispatcher, CommandDispatcher>();
builder.Services.AddScoped<IQueryDispatcher, QueryDispatcher>();
builder.Services.AddScoped<ICommandHandler<CreateProjectCommand, CreateProjectResult>, CreateProjectCommandHandler>();
builder.Services.AddScoped<IQueryHandler<ListProjectsQuery, IReadOnlyList<ProjectView>>, ListProjectsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetProjectQuery, ProjectView?>, GetProjectQueryHandler>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<V2DbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseExceptionHandler();
app.MapCreateProject();
app.MapProjectQueries();
app.Run();

public partial class Program;
