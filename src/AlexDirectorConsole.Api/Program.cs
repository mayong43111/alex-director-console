using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Data;
using AlexDirectorConsole.Api.Models;
using AlexDirectorConsole.Api.Services;
using AlexDirectorConsole.Api.Storage;
using AlexDirectorConsole.Api.Tools;
using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
builder.Services.AddSingleton<IDirectorToolRegistry, DirectorToolRegistry>();
builder.Services.AddSingleton<IDirectorTool, ListProjectResourcesTool>();
builder.Services.AddSingleton<IDirectorTool, ReadProjectResourcesTool>();
builder.Services.AddSingleton<IDirectorTool, ReadProjectResourceContentsTool>();
builder.Services.AddSingleton<IDirectorTool, WriteDirectorRevisionTool>();
builder.Services.AddSingleton<IDirectorTool, GenerateImageTool>();
builder.Services.AddSingleton<IDirectorTool, EditImageTool>();
builder.Services.AddSingleton<IDirectorTool, InspectVisualReferencesTool>();
builder.Services.AddSingleton<IDirectorTool, MergeReferenceImagesTool>();
builder.Services.AddSingleton<IDirectorTool, GenerateImageFromReferencesTool>();
builder.Services.AddSingleton<IDirectorTool, RunScriptBreakdownTool>();
builder.Services.AddSingleton<IDirectorTool, UpdateCurrentResourceTool>();
builder.Services.AddSingleton<IDirectorTool, WriteStoryboardTool>();
builder.Services.AddSingleton<IDirectorTool, BindShotAssetTool>();
builder.Services.AddSingleton<IDirectorTool, InspectRemoteComfyUiTool>();
builder.Services.AddSingleton<IDirectorTool, ManageRemoteComfyUiTool>();
builder.Services.AddSingleton<IDirectorTool, GenerateComfyUiVideoTool>();
builder.Services.AddSingleton<IDirectorTool, AssembleImageSlideshowTool>();
builder.Services.AddSingleton<IDirectorTool, AssembleVideoClipsTool>();
builder.Services.AddHttpClient("ComfyUiProxy", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<IRemoteComfyUiService, RemoteComfyUiService>();
builder.Services.AddHttpClient<IComfyUiVideoGenerator, ComfyUiVideoGenerator>(client => client.Timeout = TimeSpan.FromHours(2));
builder.Services.AddHttpClient<IAzureFoundryImageGenerator, AzureFoundryImageGenerator>();
builder.Services.AddScoped<IAgentSkillExecutor, AgentSkillExecutor>();
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

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
    await EnsureGlobalRuntimeConfigurationAsync(dbContext);
    var dataProtectionProvider = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
    await EnsureAndLoadGlobalFoundryConfigurationAsync(dbContext, dataProtectionProvider, app.Configuration);
    await BackfillAssetVersionsAsync(dbContext);
    await RepairGeneratedImageResourcesAsync(dbContext);
    var blobStorage = scope.ServiceProvider.GetRequiredService<IBlobStorage>();
    await RemoveStaticShotSourcesAsync(dbContext, blobStorage);
    var skillCatalog = scope.ServiceProvider.GetRequiredService<IProjectSkillCatalog>();
    await EnsureSystemSkillsAsync(dbContext, skillCatalog);
    var skillExecutor = scope.ServiceProvider.GetRequiredService<IAgentSkillExecutor>();
    await skillExecutor.BackfillAnalysisAssetsAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "AlexDirectorConsole.Api",
    timestamp = DateTimeOffset.UtcNow
}))
.WithName("GetHealth")
.WithOpenApi();

app.MapGet("/api/agent/status", (
    IDirectorAgent directorAgent,
    IAzureFoundryImageGenerator imageGenerator) =>
{
    return Results.Ok(new
    {
        framework = "Microsoft Agent Framework",
        frameworkVersion = typeof(AIAgent).Assembly.GetName().Version?.ToString(),
        runtime = directorAgent.Runtime,
        skillsRuntime = directorAgent.SkillsRuntime,
        deployment = directorAgent.Deployment,
        configured = directorAgent.IsConfigured,
        imageDeployment = imageGenerator.Deployment,
        imageQuality = imageGenerator.Quality,
        imageConfigured = imageGenerator.IsConfigured
    });
})
.WithName("GetAgentStatus")
.WithOpenApi();

app.MapGet(
    "/api/projects",
    async (AppDbContext dbContext, CancellationToken cancellationToken) =>
    {
        var projects = await dbContext.Projects
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return Results.Ok(projects
            .OrderByDescending(project => project.UpdatedAtUtc)
            .Select(ProjectResponse.FromProject));
    })
    .WithName("GetProjects")
    .WithOpenApi();

app.MapPut(
    "/api/projects/{projectId:guid}",
    async (
        Guid projectId,
        UpsertProjectRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(request.Name)
            || request.Name.Trim().Length > 200
            || request.Description.Length > 4000
            || string.IsNullOrWhiteSpace(request.FormatPreset)
            || request.FormatPreset.Trim().Length > 40
            || string.IsNullOrWhiteSpace(request.PreviewResolution)
            || request.PreviewResolution.Trim().Length > 40
            || string.IsNullOrWhiteSpace(request.LanguageModel)
            || request.LanguageModel.Trim().Length > 100
            || string.IsNullOrWhiteSpace(request.ImageModel)
            || request.ImageModel.Trim().Length > 100
            || request.VideoModel.Length > 100
            || request.OutputWidth is < 1 or > 16384
            || request.OutputHeight is < 1 or > 16384)
        {
            return Results.BadRequest(new { error = "项目字段为空、过长或超出有效范围。" });
        }

        var now = DateTimeOffset.UtcNow;
        var project = await dbContext.Projects
            .SingleOrDefaultAsync(item => item.Id == projectId, cancellationToken);
        if (project is null)
        {
            project = new Project
            {
                Id = projectId,
                Name = request.Name.Trim(),
                CreatedAtUtc = request.CreatedAt == default ? now : request.CreatedAt,
                UpdatedAtUtc = now
            };
            dbContext.Projects.Add(project);
        }

        project.Name = request.Name.Trim();
        project.Description = request.Description.Trim();
        project.FormatPreset = request.FormatPreset.Trim();
        project.OutputWidth = request.OutputWidth;
        project.OutputHeight = request.OutputHeight;
        project.PreviewResolution = request.PreviewResolution.Trim();
        project.LanguageModel = request.LanguageModel.Trim();
        project.ImageModel = request.ImageModel.Trim();
        project.VideoModel = request.VideoModel.Trim();
        project.UpdatedAtUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ProjectResponse.FromProject(project));
    })
    .WithName("UpsertProject")
    .WithOpenApi();

app.MapGet(
    "/api/system/runtime-configuration",
    async (AppDbContext dbContext, CancellationToken cancellationToken) =>
    {
        var configuration = await dbContext.ProjectRuntimeConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ProjectId == Guid.Empty, cancellationToken)
            ?? new ProjectRuntimeConfiguration
            {
                ProjectId = Guid.Empty,
                UpdatedAtUtc = DateTimeOffset.MinValue
            };
        return Results.Ok(ProjectRuntimeConfigurationResponse.FromConfiguration(configuration));
    })
    .WithName("GetGlobalRuntimeConfiguration")
    .WithOpenApi();

app.MapPut(
    "/api/system/runtime-configuration",
    async (
        UpdateProjectRuntimeConfigurationRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken) =>
    {
        var values = new[]
        {
            request.VmHost,
            request.VmUsername,
            request.SshPrivateKeyPath,
            request.ComfyUiPath,
            request.ComfyUiPythonPath,
            request.WorkflowDirectory,
            request.OutputDirectory
        };
        if (values.Any(value => string.IsNullOrWhiteSpace(value))
            || request.VmHost.Trim().Length > 260
            || request.VmUsername.Trim().Length > 100
            || values.Skip(2).Any(value => value.Trim().Length > 500))
        {
            return Results.BadRequest(new { error = "VM、SSH 与 ComfyUI 配置不能为空或超过长度限制。" });
        }
        if (request.VmPort is < 1 or > 65535
            || request.ComfyUiPort is < 1 or > 65535
            || request.LocalProxyPort is < 1 or > 65535)
        {
            return Results.BadRequest(new { error = "端口必须在 1 到 65535 之间。" });
        }

        var configuration = await dbContext.ProjectRuntimeConfigurations
            .SingleOrDefaultAsync(item => item.ProjectId == Guid.Empty, cancellationToken);
        if (configuration is null)
        {
            configuration = new ProjectRuntimeConfiguration { ProjectId = Guid.Empty };
            dbContext.ProjectRuntimeConfigurations.Add(configuration);
        }
        configuration.VmHost = request.VmHost.Trim();
        configuration.VmPort = request.VmPort;
        configuration.VmUsername = request.VmUsername.Trim();
        configuration.SshPrivateKeyPath = request.SshPrivateKeyPath.Trim();
        configuration.ComfyUiPath = request.ComfyUiPath.Trim();
        configuration.ComfyUiPythonPath = request.ComfyUiPythonPath.Trim();
        configuration.ComfyUiPort = request.ComfyUiPort;
        configuration.LocalProxyPort = request.LocalProxyPort;
        configuration.WorkflowDirectory = request.WorkflowDirectory.Trim();
        configuration.OutputDirectory = request.OutputDirectory.Trim();
        configuration.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ProjectRuntimeConfigurationResponse.FromConfiguration(configuration));
    })
    .WithName("UpdateGlobalRuntimeConfiguration")
    .WithOpenApi();

app.MapGet(
    "/api/system/foundry-configuration",
    async (AppDbContext dbContext, CancellationToken cancellationToken) =>
    {
        var configuration = await dbContext.GlobalFoundryConfigurations.AsNoTracking()
            .SingleAsync(item => item.Id == 1, cancellationToken);
        return Results.Ok(GlobalFoundryConfigurationResponse.FromConfiguration(configuration));
    })
    .WithName("GetGlobalFoundryConfiguration")
    .WithOpenApi();

app.MapPut(
    "/api/system/foundry-configuration",
    async (
        UpdateGlobalFoundryConfigurationRequest request,
        AppDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider,
        CancellationToken cancellationToken) =>
    {
        var openAiEndpoint = request.OpenAiEndpoint.Trim();
        var imageEndpoint = request.ImageEndpoint.Trim();
        if ((!string.IsNullOrWhiteSpace(openAiEndpoint) && !Uri.TryCreate(openAiEndpoint, UriKind.Absolute, out _))
            || (!string.IsNullOrWhiteSpace(imageEndpoint) && !Uri.TryCreate(imageEndpoint, UriKind.Absolute, out _)))
            return Results.BadRequest(new { error = "Foundry Endpoint 必须为空或有效的绝对 URL。" });
        if (request.OpenAiDeployment.Trim().Length is 0 or > 100
            || request.ImageDeployment.Trim().Length is 0 or > 100
            || request.ImageApiVersion.Trim().Length is 0 or > 100
            || request.ImageQuality is not ("low" or "medium" or "high"))
            return Results.BadRequest(new { error = "部署名称、API 版本或图片质量无效。" });

        var configuration = await dbContext.GlobalFoundryConfigurations
            .SingleAsync(item => item.Id == 1, cancellationToken);
        var protector = dataProtectionProvider.CreateProtector("FoundryApiKeys.v1");
        configuration.OpenAiEndpoint = openAiEndpoint;
        configuration.OpenAiDeployment = request.OpenAiDeployment.Trim();
        configuration.ImageEndpoint = imageEndpoint;
        configuration.ImageDeployment = request.ImageDeployment.Trim();
        configuration.ImageApiVersion = request.ImageApiVersion.Trim();
        configuration.ImageQuality = request.ImageQuality;
        if (request.ClearOpenAiApiKey) configuration.ProtectedOpenAiApiKey = string.Empty;
        else if (!string.IsNullOrWhiteSpace(request.OpenAiApiKey)) configuration.ProtectedOpenAiApiKey = protector.Protect(request.OpenAiApiKey.Trim());
        if (request.ClearImageApiKey) configuration.ProtectedImageApiKey = string.Empty;
        else if (!string.IsNullOrWhiteSpace(request.ImageApiKey)) configuration.ProtectedImageApiKey = protector.Protect(request.ImageApiKey.Trim());
        configuration.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        ApplyFoundryEnvironment(configuration, protector);
        return Results.Ok(GlobalFoundryConfigurationResponse.FromConfiguration(configuration));
    })
    .WithName("UpdateGlobalFoundryConfiguration")
    .WithOpenApi();

app.MapGet(
    "/api/skills",
    async (
        AppDbContext dbContext,
        IProjectSkillCatalog skillCatalog,
        CancellationToken cancellationToken) =>
    {
        var definitions = await dbContext.SkillDefinitions
            .AsNoTracking()
            .OrderBy(skill => skill.Name)
            .ToListAsync(cancellationToken);
        var skills = definitions.Select(skill =>
        {
            var catalogSkill = skillCatalog.Get(skill.Id);
            return new SkillDefinitionResponse(
                skill.Id,
                skill.Name,
                skill.Description,
                skill.Version,
                skill.IsEnabled,
                skill.IsSystem,
                catalogSkill?.Title ?? skill.Name,
                catalogSkill?.AllowedTools ?? [],
                catalogSkill?.Content ?? string.Empty);
        });
        return Results.Ok(skills);
    })
    .WithName("GetSkills")
    .WithOpenApi();

app.MapPatch(
    "/api/skills/{skillId}",
    async (
        string skillId,
        UpdateSkillRequest request,
        AppDbContext dbContext,
        IProjectSkillCatalog skillCatalog,
        CancellationToken cancellationToken) =>
    {
        var skill = await dbContext.SkillDefinitions
            .SingleOrDefaultAsync(item => item.Id == skillId, cancellationToken);
        if (skill is null)
        {
            return Results.NotFound();
        }

        skill.IsEnabled = request.IsEnabled;
        skill.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        var catalogSkill = skillCatalog.Get(skill.Id);
        return Results.Ok(new SkillDefinitionResponse(
            skill.Id,
            skill.Name,
            skill.Description,
            skill.Version,
            skill.IsEnabled,
            skill.IsSystem,
            catalogSkill?.Title ?? skill.Name,
            catalogSkill?.AllowedTools ?? [],
            catalogSkill?.Content ?? string.Empty));
    })
    .WithName("UpdateSkill")
    .WithOpenApi();

app.MapGet(
    "/api/projects/{projectId:guid}/skill-runs",
    async (Guid projectId, AppDbContext dbContext, CancellationToken cancellationToken) =>
    {
        var runs = await dbContext.SkillRuns
            .AsNoTracking()
            .Where(run => run.ProjectId == projectId)
            .OrderByDescending(run => run.StartedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);
        return Results.Ok(runs.Select(SkillRunResponse.FromRun));
    })
    .WithName("GetProjectSkillRuns")
    .WithOpenApi();

app.MapGet(
    "/api/projects/{projectId:guid}/assets",
    async (Guid projectId, string? type, AppDbContext dbContext, CancellationToken cancellationToken) =>
    {
        var normalizedType = type?.Trim().ToLowerInvariant();
        var query = dbContext.Assets
            .AsNoTracking()
            .Where(asset => asset.ProjectId == projectId);

        if (!string.IsNullOrWhiteSpace(normalizedType))
        {
            query = query.Where(asset => asset.Type == normalizedType);
        }

        var assets = (await query.ToListAsync(cancellationToken))
            .GroupBy(asset => asset.ResourceId)
            .Select(group => new
            {
                Latest = group
                    .OrderByDescending(asset => asset.Version)
                    .ThenByDescending(asset => asset.CreatedAtUtc)
                    .First(),
                Count = group.Count()
            });
        assets = normalizedType == "shot"
            ? assets.OrderBy(item => item.Latest.Name, StringComparer.Ordinal)
            : assets.OrderByDescending(item => item.Latest.UpdatedAtUtc);
        var responseAssets = assets
            .Select(item => AssetResponse.FromAsset(item.Latest, item.Count))
            .ToList();

        return Results.Ok(responseAssets);
    })
    .WithName("GetProjectAssets")
    .WithOpenApi();

app.MapGet(
    "/api/assets/{assetId:guid}/versions",
    async (Guid assetId, AppDbContext dbContext, CancellationToken cancellationToken) =>
    {
        var selected = await dbContext.Assets
            .AsNoTracking()
            .SingleOrDefaultAsync(asset => asset.Id == assetId, cancellationToken);
        if (selected is null)
        {
            return Results.NotFound();
        }

        var versionAssets = (await dbContext.Assets
                .AsNoTracking()
                .Where(asset => asset.ResourceId == selected.ResourceId)
                .ToListAsync(cancellationToken))
            .OrderByDescending(asset => asset.Version)
            .ToList();
        return Results.Ok(versionAssets.Select(asset =>
            AssetResponse.FromAsset(asset, versionAssets.Count)));
    })
    .WithName("GetAssetVersions")
    .WithOpenApi();

app.MapPost(
    "/api/projects/{projectId:guid}/assets",
    async (
        Guid projectId,
        HttpRequest request,
        AppDbContext dbContext,
        IBlobStorage blobStorage,
        IConfiguration configuration,
        CancellationToken cancellationToken) =>
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "multipart/form-data is required." });
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new { error = "A non-empty file is required." });
        }

        var maxUploadBytes = configuration.GetValue<long>(
            "BlobStorage:MaxUploadBytes",
            104857600);
        if (file.Length > maxUploadBytes)
        {
            return Results.BadRequest(new { error = $"File exceeds the {maxUploadBytes} byte limit." });
        }

        var type = form["type"].ToString().Trim().ToLowerInvariant();
        if (!IsValidAssetType(type))
        {
            return Results.BadRequest(new { error = "Asset type must use 1-50 lowercase letters, numbers, hyphens, or underscores." });
        }

        var fileName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 260)
        {
            return Results.BadRequest(new { error = "File name is invalid or longer than 260 characters." });
        }

        var name = form["name"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Path.GetFileNameWithoutExtension(fileName);
        }

        if (name.Length > 260)
        {
            return Results.BadRequest(new { error = "Asset name cannot exceed 260 characters." });
        }

        var assetId = Guid.NewGuid();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension.Length > 20)
        {
            extension = string.Empty;
        }

        var now = DateTimeOffset.UtcNow;
        var asset = new Asset
        {
            Id = assetId,
            ProjectId = projectId,
            Type = type,
            Name = name,
            BlobKey = $"{projectId:N}/{type}/{assetId:N}{extension}",
            FileName = fileName,
            ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType,
            SizeBytes = file.Length,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await using (var content = file.OpenReadStream())
        {
            await blobStorage.SaveAsync(asset.BlobKey, content, cancellationToken);
        }

        try
        {
            dbContext.Assets.Add(asset);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await blobStorage.DeleteAsync(asset.BlobKey, cancellationToken);
            throw;
        }

        return Results.Created($"/api/assets/{asset.Id}", AssetResponse.FromAsset(asset));
    })
    .WithName("UploadProjectAsset")
    .DisableAntiforgery();

app.MapGet(
    "/api/assets/{assetId:guid}/content",
    async (
        Guid assetId,
        AppDbContext dbContext,
        IBlobStorage blobStorage,
        CancellationToken cancellationToken) =>
    {
        var asset = await dbContext.Assets
            .AsNoTracking()
            .SingleOrDefaultAsync(asset => asset.Id == assetId, cancellationToken);
        if (asset is null)
        {
            return Results.NotFound();
        }

        var content = await blobStorage.OpenReadAsync(asset.BlobKey, cancellationToken);
        return content is null
            ? Results.NotFound()
            : Results.File(
                content,
                asset.ContentType,
                asset.FileName,
                enableRangeProcessing: true);
    })
    .WithName("GetAssetContent")
    .WithOpenApi();

app.MapGet(
    "/api/assets/{shotAssetId:guid}/linked-assets",
    async (
        Guid shotAssetId,
        AppDbContext dbContext,
        CancellationToken cancellationToken) =>
    {
        var shot = await dbContext.Assets
            .AsNoTracking()
            .SingleOrDefaultAsync(asset => asset.Id == shotAssetId && asset.Type == "shot", cancellationToken);
        if (shot is null)
        {
            return Results.NotFound();
        }

        var links = await dbContext.ShotAssetLinks
            .AsNoTracking()
            .Where(link => link.ProjectId == shot.ProjectId && link.ShotResourceId == shot.ResourceId)
            .ToListAsync(cancellationToken);
        links = links
            .OrderBy(link => link.Role)
            .ThenByDescending(link => link.CreatedAtUtc)
            .ToList();
        var assetIds = links.Select(link => link.AssetId).Distinct().ToArray();
        var assets = await dbContext.Assets
            .AsNoTracking()
            .Where(asset => assetIds.Contains(asset.Id))
            .ToDictionaryAsync(asset => asset.Id, cancellationToken);
        var validLinks = links
            .Where(link => assets.ContainsKey(link.AssetId))
            .ToList();
        var exclusiveRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "first-frame",
            "last-frame",
            "video"
        };
        var responseLinks = validLinks
            .Where(link => exclusiveRoles.Contains(link.Role))
            .GroupBy(link => link.Role, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(link => link.CreatedAtUtc)
                .ThenByDescending(link => assets[link.AssetId].Version)
                .First())
            .Concat(validLinks
                .Where(link => !exclusiveRoles.Contains(link.Role))
            .GroupBy(link => new { link.Role, assets[link.AssetId].ResourceId })
            .Select(group => group
                .OrderByDescending(link => assets[link.AssetId].Version)
                .ThenByDescending(link => link.CreatedAtUtc)
                .First()))
            .OrderBy(link => link.Role)
            .ThenByDescending(link => link.CreatedAtUtc);
        var response = responseLinks
            .Select(link => new ShotAssetLinkResponse(
                link.Id,
                link.Role,
                link.CreatedAtUtc,
                AssetResponse.FromAsset(assets[link.AssetId])))
            .ToArray();
        return Results.Ok(response);
    })
    .WithName("GetShotLinkedAssets")
    .WithOpenApi();

app.MapGet(
    "/api/projects/{projectId:guid}/messages",
    async (Guid projectId, AppDbContext dbContext, CancellationToken cancellationToken) =>
    {
        var messages = await dbContext.ConversationMessages
            .AsNoTracking()
            .Where(message => message.ProjectId == projectId)
            .OrderBy(message => message.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var generatedAssetIds = messages
            .SelectMany(ConversationMessageResponse.GetGeneratedAssetIds)
            .Distinct()
            .ToArray();
        var generatedAssetsById = generatedAssetIds.Length == 0
            ? new Dictionary<Guid, Asset>()
            : await dbContext.Assets
                .AsNoTracking()
                .Where(asset => generatedAssetIds.Contains(asset.Id))
                .ToDictionaryAsync(asset => asset.Id, cancellationToken);
        var legacyGeneratedImages = await dbContext.Assets
            .AsNoTracking()
            .Where(asset =>
                asset.ProjectId == projectId
                && asset.ContentType.StartsWith("image/"))
            .ToListAsync(cancellationToken);

        var responseMessages = messages.Select(message =>
        {
            var explicitAssets = ConversationMessageResponse.GetGeneratedAssetIds(message)
                .Where(generatedAssetsById.ContainsKey)
                .Select(assetId => AssetResponse.FromAsset(generatedAssetsById[assetId]))
                .ToArray();
            var generatedAssets = explicitAssets.Length > 0 || message.Role != "assistant"
                ? explicitAssets
                : legacyGeneratedImages
                    .Where(asset => message.Content.Contains(
                        asset.Name,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(AssetResponse.FromAsset)
                    .ToArray();

            return ConversationMessageResponse.FromMessage(message, generatedAssets);
        }).ToArray();

        return Results.Ok(responseMessages);
    })
    .WithName("GetProjectMessages")
    .WithOpenApi();

app.MapDelete(
    "/api/projects/{projectId:guid}/messages/{messageId:guid}/following",
    async (
        Guid projectId,
        Guid messageId,
        AppDbContext dbContext,
        CancellationToken cancellationToken) =>
    {
        var targetMessage = await dbContext.ConversationMessages
            .AsNoTracking()
            .SingleOrDefaultAsync(message =>
                message.ProjectId == projectId && message.Id == messageId,
                cancellationToken);
        if (targetMessage is null)
        {
            return Results.NotFound();
        }

        if (targetMessage.Role != "user")
        {
            return Results.BadRequest(new { error = "Only user messages can be retried." });
        }

        var messagesToDelete = await dbContext.ConversationMessages
            .Where(message =>
                message.ProjectId == projectId
                && (message.Id == messageId || message.CreatedAtUtc > targetMessage.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        dbContext.ConversationMessages.RemoveRange(messagesToDelete);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    })
    .WithName("DeleteProjectMessagesFrom")
    .WithOpenApi();

app.MapPost(
    "/api/projects/{projectId:guid}/messages",
    async (
        Guid projectId,
        SendMessageRequest request,
        AppDbContext dbContext,
        IDirectorAgent directorAgent,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        var content = request.Message?.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            return Results.BadRequest(new { error = "Message is required." });
        }

        if (content.Length > 20000)
        {
            return Results.BadRequest(new { error = "Message cannot exceed 20,000 characters." });
        }

        if (!string.IsNullOrWhiteSpace(request.Model) && request.Model.Trim().Length > 100)
        {
            return Results.BadRequest(new { error = "Model deployment name cannot exceed 100 characters." });
        }

        if (!directorAgent.IsConfigured)
        {
            return Results.Problem(
                title: "Azure AI Foundry is not configured",
                detail: "Set AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, and AZURE_OPENAI_DEPLOYMENT in .env.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var history = await dbContext.ConversationMessages
            .AsNoTracking()
            .Where(message => message.ProjectId == projectId)
            .OrderByDescending(message => message.CreatedAtUtc)
            .Take(40)
            .ToListAsync(cancellationToken);
        history.Reverse();

        DirectorAgentReply reply;
        try
        {
            reply = await directorAgent.ReplyAsync(
                history,
                content,
                request.Model,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Azure AI Foundry conversation failed for project {ProjectId}", projectId);
            return Results.Problem(
                title: "Azure AI Foundry request failed",
                detail: "The execution assistant could not produce a response.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        var now = DateTime.UtcNow;
        var userMessage = new ConversationMessage
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Role = "user",
            Content = content,
            Model = reply.Deployment,
            CreatedAtUtc = now
        };
        var assistantMessage = new ConversationMessage
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Role = "assistant",
            Content = reply.Text,
            Model = reply.Deployment,
            CreatedAtUtc = now.AddTicks(1)
        };

        dbContext.ConversationMessages.AddRange(userMessage, assistantMessage);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new SendMessageResponse(
            ConversationMessageResponse.FromMessage(userMessage),
            ConversationMessageResponse.FromMessage(assistantMessage)));
    })
    .WithName("SendProjectMessage")
    .WithOpenApi();

app.MapPost(
    "/api/projects/{projectId:guid}/messages/stream",
    async (
        Guid projectId,
        SendMessageRequest request,
        HttpResponse response,
        AppDbContext dbContext,
        IDirectorAgent directorAgent,
        IAzureFoundryImageGenerator imageGenerator,
        IAgentSkillExecutor skillExecutor,
        IRemoteComfyUiService remoteComfyUiService,
        IComfyUiVideoGenerator comfyUiVideoGenerator,
        IProjectSkillCatalog skillCatalog,
        IDirectorToolRegistry toolRegistry,
        IBlobStorage blobStorage,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        var content = request.Message?.Trim();
        if (string.IsNullOrWhiteSpace(content) || content.Length > 20000)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "Message is required and cannot exceed 20,000 characters." }, cancellationToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.ImageModel) && request.ImageModel.Trim().Length > 100)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "Image model deployment name cannot exceed 100 characters." }, cancellationToken);
            return;
        }

        if (!directorAgent.IsConfigured)
        {
            response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await response.WriteAsJsonAsync(new { error = "Azure AI Foundry is not configured." }, cancellationToken);
            return;
        }

        Asset? currentAsset = null;
        string? currentAssetContent = null;
        if (request.AssetId is not null)
        {
            currentAsset = await dbContext.Assets.SingleOrDefaultAsync(
                asset => asset.Id == request.AssetId && asset.ProjectId == projectId,
                cancellationToken);
            if (currentAsset is null)
            {
                response.StatusCode = StatusCodes.Status400BadRequest;
                await response.WriteAsJsonAsync(new { error = "The current asset does not belong to this project." }, cancellationToken);
                return;
            }

            if (IsTextAsset(currentAsset))
            {
                await using var assetStream = await blobStorage.OpenReadAsync(
                    currentAsset.BlobKey,
                    cancellationToken);
                if (assetStream is not null)
                {
                    using var assetReader = new StreamReader(assetStream, detectEncodingFromByteOrderMarks: true);
                    currentAssetContent = await assetReader.ReadToEndAsync(cancellationToken);
                }
            }
        }

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "application/x-ndjson; charset=utf-8";
        response.Headers.CacheControl = "no-cache, no-transform";
        response.Headers.Append("X-Accel-Buffering", "no");

        var history = await dbContext.ConversationMessages
            .AsNoTracking()
            .Where(message => message.ProjectId == projectId)
            .OrderByDescending(message => message.CreatedAtUtc)
            .Take(40)
            .ToListAsync(cancellationToken);
        history.Reverse();
        var recentGeneratedImages = await GetRecentGeneratedImagesAsync(
            dbContext,
            projectId,
            history,
            cancellationToken);
        var enabledSkills = await dbContext.SkillDefinitions
            .AsNoTracking()
            .Where(skill => skill.IsEnabled)
            .OrderBy(skill => skill.Name)
            .ToListAsync(cancellationToken);
        var enabledSkillIds = enabledSkills
            .Select(skill => skill.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var availableSkills = skillCatalog.List()
            .Where(skill => enabledSkillIds.Contains(skill.Name))
            .ToArray();

        await WriteStreamEventAsync(response, new
        {
            type = "message.accepted",
            message = "已接收导演令"
        }, cancellationToken);

        try
        {
            var deployment = string.IsNullOrWhiteSpace(request.Model)
                ? directorAgent.Deployment
                : request.Model.Trim();
            var replyBuilder = new StringBuilder();
            using var toolContext = new DirectorToolContext
            {
                ProjectId = projectId,
                Content = content,
                RequestedModel = request.Model,
                ImageSize = request.ImageSize is "1536x1024" or "1024x1536"
                    ? request.ImageSize
                    : "1024x1024",
                ImageDeployment = string.IsNullOrWhiteSpace(request.ImageModel)
                    ? imageGenerator.Deployment
                    : request.ImageModel.Trim(),
                CurrentAsset = currentAsset,
                CurrentAssetContent = currentAssetContent,
                DbContext = dbContext,
                Response = response,
                BlobStorage = blobStorage,
                ImageGenerator = imageGenerator,
                SkillExecutor = skillExecutor
                ,RemoteComfyUiService = remoteComfyUiService,
                ComfyUiVideoGenerator = comfyUiVideoGenerator
            };
            var tools = toolRegistry.CreateTools(toolContext).ToList();

            var selectedResourceContext = currentAsset is null
                ? "界面当前资源：未选择。可根据对话历史和最近生成图片自行确定操作对象，不要仅因界面未选择资源而要求导演重复指定。"
                : $"""
                    界面当前资源（由界面选择，不需要导演重复说明）：
                    - ID：{currentAsset.Id}
                    - 名称：{currentAsset.Name}
                    - 类型：{currentAsset.Type}
                    - 文件：{currentAsset.FileName}

                    当前资源完整正文：
                    {currentAssetContent ?? "[非文本资源，正文不可读取]"}
                    """;
            var recentImageContext = recentGeneratedImages.Count == 0
                ? "最近对话没有生成图片。"
                : $"""
                    最近对话生成的图片（从新到旧，续作或修改时由 Agent 自行判断引用哪一张）：
                    {string.Join(Environment.NewLine, recentGeneratedImages.Select(asset => $"- ID：{asset.Id}；名称：{asset.Name}；版本：v{asset.Version}；文件：{asset.FileName}"))}
                    """;
            var projectFormatContext = $"""
                当前项目成片画面规格：
                - 项目名称：{request.ProjectName ?? "未设置"}
                - 项目描述：{request.ProjectDescription ?? "未设置"}
                - 画幅比例：{request.ProjectAspectRatio ?? "未设置"}
                - 成片分辨率：{request.ProjectResolution ?? "未设置"}
                - 快速拉片分辨率：{request.PreviewResolution ?? "未设置"}
                - Image 模型部署：{toolContext.ImageDeployment}
                - 视频模型部署：{(string.IsNullOrWhiteSpace(request.VideoModel) ? "未配置" : request.VideoModel.Trim())}
                - 成片类图片的模型原生生成尺寸：{toolContext.ImageSize}

                项目画幅只用于 shot 首帧、关键帧、分镜图和其他成片画面。人物三视图、人物设定图、场景设定图、道具设定图及其他视觉参考素材不继承项目画幅，固定使用 1:1（1024x1024）。调用图片生成或编辑工具时必须按此用途选择 imagePurpose。成片类图片的模型原生尺寸与交付分辨率不同时，按项目画幅构图，并以成片分辨率作为后期交付目标。
                """;
            var agentContext = $"{projectFormatContext}\n\n{recentImageContext}\n\n{selectedResourceContext}";
            await WriteStreamEventAsync(response, new
            {
                type = "process",
                stage = "context.current-resource",
                message = currentAsset is null
                    ? "当前未选择资源"
                    : $"已载入当前资源：{currentAsset.Name}"
            }, cancellationToken);
            await WriteStreamEventAsync(response, new
            {
                type = "process",
                stage = "agent.started",
                message = $"正在调用 {deployment}，由 Agent 自主选择技能与工具"
            }, cancellationToken);
            await foreach (var delta in directorAgent.StreamReplyWithToolsAsync(
                history,
                content,
                agentContext,
                request.Model,
                tools,
                availableSkills
                    .Select(skill => Path.GetDirectoryName(skill.FilePath)!)
                    .ToArray(),
                cancellationToken))
            {
                replyBuilder.Append(delta);
                await WriteStreamEventAsync(response, new
                {
                    type = "assistant.delta",
                    delta
                }, cancellationToken);
            }
            if (toolContext.ImagePrompts.Count > 0)
            {
                var promptAppendix = new StringBuilder();
                foreach (var (operation, resourceName, prompt) in toolContext.ImagePrompts)
                {
                    promptAppendix
                        .AppendLine()
                        .AppendLine()
                        .AppendLine($"### {operation}完整提示词：{resourceName}")
                        .AppendLine()
                        .AppendLine("```text")
                        .AppendLine(prompt)
                        .Append("```");
                }
                var promptOutput = promptAppendix.ToString();
                replyBuilder.Append(promptOutput);
                await WriteStreamEventAsync(response, new
                {
                    type = "assistant.delta",
                    delta = promptOutput
                }, cancellationToken);
            }
            if (toolContext.VideoPrompts.Count > 0)
            {
                var promptAppendix = new StringBuilder();
                foreach (var record in toolContext.VideoPrompts)
                {
                    promptAppendix
                        .AppendLine()
                        .AppendLine()
                        .AppendLine($"### 生成视频完整提示词：{record.ResourceName}")
                        .AppendLine()
                        .AppendLine($"Workflow：`{record.Workflow}` · {record.Width}×{record.Height} · {record.FrameCount} 帧 · {record.Fps} FPS")
                        .AppendLine()
                        .AppendLine("```text")
                        .AppendLine(record.Prompt)
                        .Append("```");
                }
                var promptOutput = promptAppendix.ToString();
                replyBuilder.Append(promptOutput);
                await WriteStreamEventAsync(response, new { type = "assistant.delta", delta = promptOutput }, cancellationToken);
            }
            var execution = toolContext.Execution;
            if (execution is not null)
            {
                deployment = execution.Run.Model;
            }

            var revisedAssets = toolContext.RevisedAssets;
            var generatedAssets = execution is not null
                ? execution.GeneratedAssets
                    .Concat(revisedAssets)
                    .DistinctBy(asset => asset.Id)
                    .ToArray()
                : revisedAssets.ToArray();

            var updatedAsset = toolContext.UpdatedAsset;

            var now = DateTime.UtcNow;
            var userMessage = new ConversationMessage
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Role = "user",
                Content = content,
                Model = deployment,
                CreatedAtUtc = now
            };
            var assistantMessage = new ConversationMessage
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Role = "assistant",
                Content = replyBuilder.ToString(),
                Model = deployment,
                GeneratedAssetIdsJson = generatedAssets.Length == 0
                    ? null
                    : JsonSerializer.Serialize(generatedAssets.Select(asset => asset.Id)),
                CreatedAtUtc = now.AddTicks(1)
            };
            dbContext.ConversationMessages.AddRange(userMessage, assistantMessage);
            await dbContext.SaveChangesAsync(cancellationToken);

            await WriteStreamEventAsync(response, new
            {
                type = "completed",
                userMessage = ConversationMessageResponse.FromMessage(userMessage),
                assistantMessage = ConversationMessageResponse.FromMessage(
                    assistantMessage,
                    generatedAssets.Select(AssetResponse.FromAsset).ToArray()),
                skillRun = execution is null ? null : SkillRunResponse.FromRun(execution.Run),
                outputAsset = execution?.OutputAsset is null
                    ? null
                    : AssetResponse.FromAsset(execution.OutputAsset),
                generatedAssets = generatedAssets.Select(AssetResponse.FromAsset),
                updatedAsset = updatedAsset is null ? null : AssetResponse.FromAsset(updatedAsset)
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Streaming Agent request failed for project {ProjectId}", projectId);
            await WriteStreamEventAsync(response, new
            {
                type = "error",
                message = "执行副导演未能完成本次请求。",
                detail = exception.Message
            }, CancellationToken.None);
        }
    })
    .WithName("StreamProjectMessage");

app.Run();

static bool IsValidAssetType(string type) =>
    type.Length is > 0 and <= 50
    && type.All(character =>
        character is >= 'a' and <= 'z'
        or >= '0' and <= '9'
        or '-'
        or '_');

static bool IsTextAsset(Asset asset) =>
    asset.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
    || asset.ContentType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
    || Path.GetExtension(asset.FileName).Equals(".md", StringComparison.OrdinalIgnoreCase)
    || Path.GetExtension(asset.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase)
    || Path.GetExtension(asset.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase);

static async Task<IReadOnlyList<Asset>> GetRecentGeneratedImagesAsync(
    AppDbContext dbContext,
    Guid projectId,
    IReadOnlyList<ConversationMessage> history,
    CancellationToken cancellationToken)
{
    var assetIds = history
        .Reverse()
        .Where(message => message.Role == "assistant")
        .SelectMany(ConversationMessageResponse.GetGeneratedAssetIds)
        .Distinct()
        .Take(10)
        .ToArray();
    if (assetIds.Length == 0)
    {
        return [];
    }

    var assetsById = await dbContext.Assets
        .AsNoTracking()
        .Where(asset =>
            asset.ProjectId == projectId
            && assetIds.Contains(asset.Id)
            && asset.ContentType.StartsWith("image/"))
        .ToDictionaryAsync(asset => asset.Id, cancellationToken);
    return assetIds
        .Where(assetsById.ContainsKey)
        .Select(assetId => assetsById[assetId])
        .ToArray();
}

static string GetResourceSubject(string value) =>
    value.Split('·', StringSplitOptions.TrimEntries)[0];

static async Task BackfillAssetVersionsAsync(AppDbContext dbContext)
{
    var assets = await dbContext.Assets.ToListAsync();
    if (!assets.Any(asset => asset.Name.Contains("导演修订", StringComparison.Ordinal)))
    {
        return;
    }

    foreach (var group in assets.GroupBy(asset => new
             {
                 asset.ProjectId,
                 asset.Type,
                 Subject = GetResourceSubject(asset.Name)
             }))
    {
        var versions = group
            .OrderBy(asset => asset.CreatedAtUtc)
            .ThenBy(asset => asset.Id)
            .ToList();
        var resourceId = versions[0].Id;
        var canonicalName = versions
            .FirstOrDefault(asset => !asset.Name.Contains("导演修订", StringComparison.Ordinal))
            ?.Name ?? versions[0].Name;
        for (var index = 0; index < versions.Count; index++)
        {
            versions[index].ResourceId = resourceId;
            versions[index].Version = index + 1;
            versions[index].Name = canonicalName;
        }
    }

    await dbContext.SaveChangesAsync();
}

static async Task RepairGeneratedImageResourcesAsync(AppDbContext dbContext)
{
    var generatedImages = (await dbContext.Assets
            .Where(asset => asset.Type == "media"
                && asset.ContentType.StartsWith("image/")
                && asset.Name.Contains("AI 图片"))
            .ToListAsync())
        .Select(asset => new
        {
            Asset = asset,
            ResourceKey = GetGeneratedImageResourceKey(asset.FileName)
        })
        .Where(item => item.ResourceKey is not null)
        .ToList();
    var changed = false;
    foreach (var group in generatedImages.GroupBy(
        item => new { item.Asset.ProjectId, ResourceKey = item.ResourceKey! }))
    {
        var versions = group
            .OrderBy(item => item.Asset.CreatedAtUtc)
            .ThenBy(item => item.Asset.Id)
            .Select(item => item.Asset)
            .ToList();
        var resourceId = versions[0].Id;
        var canonicalName = $"{group.Key.ResourceKey} · AI 图片";
        for (var index = 0; index < versions.Count; index++)
        {
            var asset = versions[index];
            var version = index + 1;
            if (asset.ResourceId == resourceId
                && asset.Version == version
                && asset.Name.Equals(canonicalName, StringComparison.Ordinal))
            {
                continue;
            }
            asset.ResourceId = resourceId;
            asset.Version = version;
            asset.Name = canonicalName;
            changed = true;
        }
    }
    if (changed)
    {
        await dbContext.SaveChangesAsync();
    }
}

static string? GetGeneratedImageResourceKey(string fileName)
{
    var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
    var withoutVersion = Regex.Replace(
        withoutExtension,
        "-v\\d+$",
        string.Empty,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    if (withoutVersion.Equals(withoutExtension, StringComparison.Ordinal))
    {
        return null;
    }
    var segments = withoutVersion
        .Split('·', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();
    while (segments.Count > 0
        && segments[^1].Equals("AI 图片", StringComparison.OrdinalIgnoreCase))
    {
        segments.RemoveAt(segments.Count - 1);
    }
    return segments.Count == 0 ? null : string.Join(" · ", segments);
}

static async Task RemoveStaticShotSourcesAsync(
    AppDbContext dbContext,
    IBlobStorage blobStorage)
{
    var shots = await dbContext.Assets
        .Where(asset => asset.Type == "shot"
            && asset.ContentType.StartsWith("text/"))
        .ToListAsync();
    var changed = false;
    var replacedBlobKeys = new List<string>();
    foreach (var shot in shots)
    {
        await using var source = await blobStorage.OpenReadAsync(shot.BlobKey, CancellationToken.None);
        if (source is null)
        {
            continue;
        }
        using var reader = new StreamReader(source, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync();
        var headingIndex = content.IndexOf(
            $"{Environment.NewLine}## 来源资源",
            StringComparison.Ordinal);
        if (headingIndex < 0)
        {
            headingIndex = content.IndexOf("\n## 来源资源", StringComparison.Ordinal);
        }
        if (headingIndex < 0)
        {
            continue;
        }
        var revisedContent = content[..headingIndex].TrimEnd() + Environment.NewLine;
        var bytes = Encoding.UTF8.GetBytes(revisedContent);
        var revisedBlobKey = $"{shot.ProjectId:N}/shot/{shot.Id:N}-dynamic.md";
        await blobStorage.DeleteAsync(revisedBlobKey, CancellationToken.None);
        await using var revisedStream = new MemoryStream(bytes, writable: false);
        await blobStorage.SaveAsync(revisedBlobKey, revisedStream, CancellationToken.None);
        replacedBlobKeys.Add(shot.BlobKey);
        shot.BlobKey = revisedBlobKey;
        shot.SizeBytes = bytes.LongLength;
        shot.UpdatedAtUtc = DateTimeOffset.UtcNow;
        changed = true;
    }
    if (changed)
    {
        await dbContext.SaveChangesAsync();
        foreach (var blobKey in replacedBlobKeys)
        {
            await blobStorage.DeleteAsync(blobKey, CancellationToken.None);
        }
    }
}

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
    var configurations = (await dbContext.ProjectRuntimeConfigurations.ToListAsync())
        .OrderByDescending(configuration => configuration.UpdatedAtUtc)
        .ToList();
    var global = configurations.FirstOrDefault(configuration => configuration.ProjectId == Guid.Empty);
    var source = global ?? configurations.FirstOrDefault();
    if (source is not null && global is null)
    {
        global = new ProjectRuntimeConfiguration
        {
            ProjectId = Guid.Empty,
            VmHost = source.VmHost,
            VmPort = source.VmPort,
            VmUsername = source.VmUsername,
            SshPrivateKeyPath = source.SshPrivateKeyPath,
            ComfyUiPath = source.ComfyUiPath,
            ComfyUiPythonPath = source.ComfyUiPythonPath,
            ComfyUiPort = source.ComfyUiPort,
            LocalProxyPort = source.LocalProxyPort,
            WorkflowDirectory = source.WorkflowDirectory,
            OutputDirectory = source.OutputDirectory,
            UpdatedAtUtc = source.UpdatedAtUtc
        };
        dbContext.ProjectRuntimeConfigurations.Add(global);
    }
    dbContext.ProjectRuntimeConfigurations.RemoveRange(
        configurations.Where(configuration => configuration.ProjectId != Guid.Empty));
    await dbContext.SaveChangesAsync();
}

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
    ApplyFoundryEnvironment(configuration, protector);
}

static void ApplyFoundryEnvironment(GlobalFoundryConfiguration configuration, IDataProtector protector)
{
    Environment.SetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", NullIfEmpty(configuration.OpenAiEndpoint));
    Environment.SetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT", configuration.OpenAiDeployment);
    Environment.SetEnvironmentVariable("AZURE_OPENAI_API_KEY", UnprotectOrNull(configuration.ProtectedOpenAiApiKey, protector));
    Environment.SetEnvironmentVariable("AZURE_IMAGE_ENDPOINT", NullIfEmpty(configuration.ImageEndpoint));
    Environment.SetEnvironmentVariable("AZURE_IMAGE_DEPLOYMENT", configuration.ImageDeployment);
    Environment.SetEnvironmentVariable("AZURE_IMAGE_API_VERSION", configuration.ImageApiVersion);
    Environment.SetEnvironmentVariable("AZURE_IMAGE_QUALITY", configuration.ImageQuality);
    Environment.SetEnvironmentVariable("AZURE_IMAGE_API_KEY", UnprotectOrNull(configuration.ProtectedImageApiKey, protector));
}

static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

static string? UnprotectOrNull(string protectedValue, IDataProtector protector) =>
    string.IsNullOrWhiteSpace(protectedValue) ? null : protector.Unprotect(protectedValue);

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

static async ValueTask WriteStreamEventAsync(
    HttpResponse response,
    object value,
    CancellationToken cancellationToken)
{
    var json = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    await response.WriteAsync(json + "\n", cancellationToken);
    await response.Body.FlushAsync(cancellationToken);
}
