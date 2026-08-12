using AlexDirectorConsole.Api.Application.Conversations;
using AlexDirectorConsole.Api.Application.Assets;
using AlexDirectorConsole.Api.Application.Configuration;
using AlexDirectorConsole.Api.Application.Maintenance;
using AlexDirectorConsole.Api.Data;
using AlexDirectorConsole.Api.Endpoints;
using AlexDirectorConsole.Api.Models;
using AlexDirectorConsole.Api.Services;
using AlexDirectorConsole.Api.Storage;
using AlexDirectorConsole.Api.Tools;
using DotNetEnv;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

var envFilePath = FindFileUpward(".env", Directory.GetCurrentDirectory())
    ?? FindFileUpward(".env", AppContext.BaseDirectory);
if (envFilePath is not null)
{
    Env.Load(envFilePath);
}

var builder = WebApplication.CreateBuilder(args);
var maxUploadBytes = builder.Configuration.GetValue<long>(
    "BlobStorage:MaxUploadBytes",
    104857600);

builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = maxUploadBytes);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDataProtection()
    .SetApplicationName("AlexDirectorConsole")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtection")));
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddSingleton<IBlobStorage, LocalBlobStorage>();
builder.Services.AddSingleton<IDirectorAgent, AzureFoundryDirectorAgent>();
builder.Services.AddSingleton<IProjectSkillCatalog, ProjectSkillCatalog>();
builder.Services.AddScoped<IDirectorToolRegistry, DirectorToolRegistry>();
builder.Services.AddScoped<IDirectorTool, ListProjectResourcesTool>();
builder.Services.AddScoped<IDirectorTool, ListShotFirstFrameStatusTool>();
builder.Services.AddScoped<IDirectorTool, ReadProjectResourcesTool>();
builder.Services.AddScoped<IDirectorTool, ReadProjectResourceContentsTool>();
builder.Services.AddScoped<IDirectorTool, DeleteProjectResourceTool>();
builder.Services.AddScoped<IDirectorTool, WriteDirectorRevisionTool>();
builder.Services.AddScoped<IDirectorTool, GenerateImageTool>();
builder.Services.AddScoped<IDirectorTool, EditImageTool>();
builder.Services.AddScoped<IDirectorTool, InspectVisualReferencesTool>();
builder.Services.AddScoped<IDirectorTool, MergeReferenceImagesTool>();
builder.Services.AddScoped<IDirectorTool, GenerateImageFromReferencesTool>();
builder.Services.AddScoped<IDirectorTool, RunScriptBreakdownTool>();
builder.Services.AddScoped<IDirectorTool, UpdateCurrentResourceTool>();
builder.Services.AddScoped<IDirectorTool, WriteScriptTool>();
builder.Services.AddScoped<IDirectorTool, WriteStoryboardTool>();
builder.Services.AddScoped<IDirectorTool, BindShotAssetTool>();
builder.Services.AddScoped<IDirectorTool, InspectRemoteComfyUiTool>();
builder.Services.AddScoped<IDirectorTool, ManageRemoteComfyUiTool>();
builder.Services.AddScoped<IDirectorTool, GenerateComfyUiVideoTool>();
builder.Services.AddScoped<IDirectorTool, AssembleImageSlideshowTool>();
builder.Services.AddScoped<IDirectorTool, AssembleVideoClipsTool>();
builder.Services.AddHttpClient("ComfyUiProxy", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<IRemoteComfyUiService, RemoteComfyUiService>();
builder.Services.AddHttpClient<IComfyUiVideoGenerator, ComfyUiVideoGenerator>(client => client.Timeout = TimeSpan.FromHours(2));
builder.Services.AddHttpClient<IAzureFoundryImageGenerator, AzureFoundryImageGenerator>();
builder.Services.AddScoped<IAgentSkillExecutor, AgentSkillExecutor>();
builder.Services.AddScoped<IAssetReader, AssetReader>();
builder.Services.AddScoped<IAssetWriter, AssetWriter>();
builder.Services.AddScoped<IShotAssetBinder, ShotAssetBinder>();
builder.Services.AddScoped<IRuntimeConfigurationReader, RuntimeConfigurationReader>();
builder.Services.AddScoped<IApplicationMaintenanceRunner, ApplicationMaintenanceRunner>();
builder.Services.AddScoped<IDirectorSessionService, DirectorSessionService>();
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = maxUploadBytes);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();
var runMaintenance = args.Contains("--run-maintenance", StringComparer.OrdinalIgnoreCase);

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
    await EnsureGlobalRuntimeConfigurationAsync(dbContext);
    var dataProtectionProvider = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
    await EnsureAndLoadGlobalFoundryConfigurationAsync(dbContext, dataProtectionProvider, app.Configuration);
    var skillCatalog = scope.ServiceProvider.GetRequiredService<IProjectSkillCatalog>();
    await EnsureSystemSkillsAsync(dbContext, skillCatalog);
    if (runMaintenance)
    {
        var maintenanceRunner = scope.ServiceProvider.GetRequiredService<IApplicationMaintenanceRunner>();
        await maintenanceRunner.RunPendingAsync();
    }
}

if (runMaintenance)
{
    return;
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.MapHealthEndpoints();
app.MapProjectEndpoints();
app.MapConfigurationEndpoints();
app.MapSkillEndpoints();
app.MapAssetEndpoints();
app.MapConversationEndpoints();
app.MapStreamingConversationEndpoints();

app.Run();

static async Task EnsureSystemSkillsAsync(
    AppDbContext dbContext,
    IProjectSkillCatalog skillCatalog)
{
    var definitions = skillCatalog.List();

    var now = DateTime.UtcNow;
    var changed = false;
    foreach (var definition in definitions)
    {
        var skill = await dbContext.SkillDefinitions.FindAsync(definition.Name);
        if (skill is null)
        {
            dbContext.SkillDefinitions.Add(new SkillDefinition
            {
                Id = definition.Name,
                Name = definition.Title,
                Description = definition.Description,
                Version = definition.Version,
                IsEnabled = true,
                IsSystem = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            changed = true;
            continue;
        }

        if (skill.Name != definition.Title
            || skill.Description != definition.Description
            || skill.Version != definition.Version)
        {
            skill.Name = definition.Title;
            skill.Description = definition.Description;
            skill.Version = definition.Version;
            skill.UpdatedAtUtc = now;
            changed = true;
        }
    }

    if (changed)
    {
        await dbContext.SaveChangesAsync();
    }
}

static async Task EnsureGlobalRuntimeConfigurationAsync(AppDbContext dbContext)
{
    var configurations = await dbContext.ProjectRuntimeConfigurations.ToListAsync();
    var global = configurations.FirstOrDefault(configuration => configuration.ProjectId == Guid.Empty);
    if (global is null)
    {
        global = new ProjectRuntimeConfiguration
        {
            ProjectId = Guid.Empty,
            VmHost = Environment.GetEnvironmentVariable("VM_HOST") ?? string.Empty,
            VmPort = GetEnvironmentPort("VM_PORT", 22),
            VmUsername = Environment.GetEnvironmentVariable("VM_USERNAME") ?? "azureuser",
            SshPrivateKeyPath = Environment.GetEnvironmentVariable("SSH_PRIVATE_KEY_PATH")
                ?? "%USERPROFILE%\\.ssh\\id_rsa",
            ComfyUiPath = Environment.GetEnvironmentVariable("COMFYUI_PATH")
                ?? "/home/azureuser/ComfyUI",
            ComfyUiPythonPath = Environment.GetEnvironmentVariable("COMFYUI_PYTHON_PATH")
                ?? "/home/azureuser/envs/comfy311/bin/python",
            ComfyUiPort = GetEnvironmentPort("COMFYUI_PORT", 8188),
            LocalProxyPort = GetEnvironmentPort("COMFYUI_LOCAL_PROXY_PORT", 8188),
            WorkflowDirectory = Environment.GetEnvironmentVariable("COMFYUI_WORKFLOW_DIRECTORY")
                ?? "/home/azureuser/ComfyUI/user/default/workflows",
            OutputDirectory = Environment.GetEnvironmentVariable("COMFYUI_OUTPUT_DIRECTORY")
                ?? "/home/azureuser/ComfyUI/output",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.ProjectRuntimeConfigurations.Add(global);
    }
    await dbContext.SaveChangesAsync();
}

static int GetEnvironmentPort(string variableName, int defaultValue) =>
    int.TryParse(Environment.GetEnvironmentVariable(variableName), out var value) && value is >= 1 and <= 65535
        ? value
        : defaultValue;

static async Task EnsureAndLoadGlobalFoundryConfigurationAsync(
    AppDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    IConfiguration applicationConfiguration)
{
    var configuration = await dbContext.GlobalFoundryConfigurations.SingleOrDefaultAsync(item => item.Id == 1);
    var protector = dataProtectionProvider.CreateProtector("FoundryApiKeys.v1");
    if (configuration is null)
    {
        var openAiApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
            ?? applicationConfiguration["AzureOpenAI:ApiKey"];
        var imageApiKey = Environment.GetEnvironmentVariable("AZURE_IMAGE_API_KEY")
            ?? applicationConfiguration["AzureImage:ApiKey"];
        configuration = new GlobalFoundryConfiguration
        {
            OpenAiEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
                ?? applicationConfiguration["AzureOpenAI:Endpoint"] ?? string.Empty,
            OpenAiDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT")
                ?? applicationConfiguration["AzureOpenAI:Deployment"] ?? "gpt-5.4",
            ProtectedOpenAiApiKey = string.IsNullOrWhiteSpace(openAiApiKey) ? string.Empty : protector.Protect(openAiApiKey),
            ImageEndpoint = Environment.GetEnvironmentVariable("AZURE_IMAGE_ENDPOINT")
                ?? applicationConfiguration["AzureImage:Endpoint"] ?? string.Empty,
            ImageDeployment = Environment.GetEnvironmentVariable("AZURE_IMAGE_DEPLOYMENT")
                ?? applicationConfiguration["AzureImage:Deployment"] ?? "gpt-image-2",
            ImageApiVersion = Environment.GetEnvironmentVariable("AZURE_IMAGE_API_VERSION")
                ?? applicationConfiguration["AzureImage:ApiVersion"] ?? "2025-04-01-preview",
            ImageQuality = Environment.GetEnvironmentVariable("AZURE_IMAGE_QUALITY")
                ?? applicationConfiguration["AzureImage:Quality"] ?? "medium",
            ProtectedImageApiKey = string.IsNullOrWhiteSpace(imageApiKey) ? string.Empty : protector.Protect(imageApiKey),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.GlobalFoundryConfigurations.Add(configuration);
        await dbContext.SaveChangesAsync();
    }
    FoundryEnvironment.Apply(configuration, protector);
}

static string? FindFileUpward(string fileName, string startPath)
{
    var directory = new DirectoryInfo(startPath);
    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, fileName);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        directory = directory.Parent;
    }

    return null;
}
