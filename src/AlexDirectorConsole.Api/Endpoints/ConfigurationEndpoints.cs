using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Data;
using AlexDirectorConsole.Api.Models;
using AlexDirectorConsole.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.Api.Endpoints;

public static class ConfigurationEndpoints
{
    public static IEndpointRouteBuilder MapConfigurationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/projects/{projectId:guid}/runtime-configuration",
            async (
                Guid projectId,
                AppDbContext dbContext,
                CancellationToken cancellationToken) =>
            {
                if (!await dbContext.Projects.AsNoTracking().AnyAsync(
                    project => project.Id == projectId,
                    cancellationToken))
                {
                    return Results.NotFound();
                }

                var configuration = await dbContext.ProjectRuntimeConfigurations
                    .SingleOrDefaultAsync(item => item.ProjectId == projectId, cancellationToken);
                if (configuration is null)
                {
                    var template = await dbContext.ProjectRuntimeConfigurations
                        .AsNoTracking()
                        .SingleAsync(item => item.ProjectId == Guid.Empty, cancellationToken);
                    configuration = CopyRuntimeConfiguration(template, projectId);
                    var usedPorts = await dbContext.ProjectRuntimeConfigurations
                        .AsNoTracking()
                        .Where(item => item.ProjectId != Guid.Empty)
                        .Select(item => item.LocalProxyPort)
                        .ToListAsync(cancellationToken);
                    configuration.LocalProxyPort = FindAvailablePort(template.LocalProxyPort, usedPorts);
                    dbContext.ProjectRuntimeConfigurations.Add(configuration);
                    try
                    {
                        await dbContext.SaveChangesAsync(cancellationToken);
                    }
                    catch (DbUpdateException)
                    {
                        return Results.Conflict(new { error = "项目配置初始化发生并发端口冲突，请重试。" });
                    }
                }
                return Results.Ok(ProjectRuntimeConfigurationResponse.FromConfiguration(configuration));
            })
            .WithName("GetProjectRuntimeConfiguration")
            .WithOpenApi();

        app.MapPut(
            "/api/projects/{projectId:guid}/runtime-configuration",
            async (
                Guid projectId,
                UpdateProjectRuntimeConfigurationRequest request,
                AppDbContext dbContext,
                CancellationToken cancellationToken) =>
            {
                if (!await dbContext.Projects.AsNoTracking().AnyAsync(
                    project => project.Id == projectId,
                    cancellationToken))
                {
                    return Results.NotFound();
                }

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
                if (await dbContext.ProjectRuntimeConfigurations.AsNoTracking().AnyAsync(
                    item => item.ProjectId != Guid.Empty
                        && item.ProjectId != projectId
                        && item.LocalProxyPort == request.LocalProxyPort,
                    cancellationToken))
                {
                    return Results.Conflict(new { error = "本地代理端口已由其他项目配置，请为当前项目选择独立端口。" });
                }
                var normalizedVmHost = request.VmHost.Trim().ToLowerInvariant();
                string normalizedComfyUiPath;
                try
                {
                    normalizedComfyUiPath = RemoteUnixPath.Normalize(request.ComfyUiPath);
                }
                catch (ArgumentException exception)
                {
                    return Results.BadRequest(new { error = exception.Message });
                }
                var remoteCandidates = await dbContext.ProjectRuntimeConfigurations
                    .AsNoTracking()
                    .Where(item => item.ProjectId != Guid.Empty
                        && item.ProjectId != projectId
                        && item.VmHost.ToLower() == normalizedVmHost
                        && item.VmPort == request.VmPort)
                    .ToListAsync(cancellationToken);
                if (remoteCandidates.Any(item => item.ComfyUiPort == request.ComfyUiPort
                    || RemoteUnixPath.Normalize(item.ComfyUiPath) == normalizedComfyUiPath))
                {
                    return Results.Conflict(new { error = "远端 ComfyUI 端口或目录已由其他项目配置。" });
                }

                var configuration = await dbContext.ProjectRuntimeConfigurations
                    .SingleOrDefaultAsync(item => item.ProjectId == projectId, cancellationToken);
                if (configuration is null)
                {
                    var template = await dbContext.ProjectRuntimeConfigurations
                        .AsNoTracking()
                        .SingleAsync(item => item.ProjectId == Guid.Empty, cancellationToken);
                    configuration = CopyRuntimeConfiguration(template, projectId);
                    dbContext.ProjectRuntimeConfigurations.Add(configuration);
                }
                configuration.VmHost = normalizedVmHost;
                configuration.VmPort = request.VmPort;
                configuration.VmUsername = request.VmUsername.Trim();
                configuration.SshPrivateKeyPath = request.SshPrivateKeyPath.Trim();
                configuration.ComfyUiPath = normalizedComfyUiPath;
                configuration.ComfyUiPythonPath = request.ComfyUiPythonPath.Trim();
                configuration.ComfyUiPort = request.ComfyUiPort;
                configuration.LocalProxyPort = request.LocalProxyPort;
                configuration.WorkflowDirectory = request.WorkflowDirectory.Trim();
                configuration.OutputDirectory = request.OutputDirectory.Trim();
                configuration.UpdatedAtUtc = DateTimeOffset.UtcNow;
                try
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    return Results.Conflict(new { error = "本地代理端口已由其他项目配置，请选择独立端口。" });
                }
                return Results.Ok(ProjectRuntimeConfigurationResponse.FromConfiguration(configuration));
            })
            .WithName("UpdateProjectRuntimeConfiguration")
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
                FoundryEnvironment.Apply(configuration, protector);
                return Results.Ok(GlobalFoundryConfigurationResponse.FromConfiguration(configuration));
            })
            .WithName("UpdateGlobalFoundryConfiguration")
            .WithOpenApi();

        return app;
    }

    private static ProjectRuntimeConfiguration CopyRuntimeConfiguration(
        ProjectRuntimeConfiguration source,
        Guid projectId) => new()
        {
            ProjectId = projectId,
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
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

    private static int FindAvailablePort(int preferredPort, IReadOnlyCollection<int> usedPorts)
    {
        var used = usedPorts.ToHashSet();
        for (var port = preferredPort; port <= 65535; port++)
        {
            if (!used.Contains(port)) return port;
        }
        for (var port = 1; port < preferredPort; port++)
        {
            if (!used.Contains(port)) return port;
        }
        throw new InvalidOperationException("没有可用的本地代理端口。");
    }
}