using AlexDirectorConsole.Api.Data;
using AlexDirectorConsole.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.Api.Application.Configuration;

public sealed class RuntimeConfigurationReader(AppDbContext dbContext) : IRuntimeConfigurationReader
{
    public async Task<ProjectRuntimeConfiguration?> GetAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        if (!await dbContext.Projects.AsNoTracking().AnyAsync(
            project => project.Id == projectId,
            cancellationToken))
        {
            return null;
        }

        return await dbContext.ProjectRuntimeConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                configuration => configuration.ProjectId == projectId,
                cancellationToken);
    }
}