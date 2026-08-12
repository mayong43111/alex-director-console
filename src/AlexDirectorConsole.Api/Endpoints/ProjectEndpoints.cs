using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Data;
using AlexDirectorConsole.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.Api.Endpoints;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
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

        return app;
    }
}