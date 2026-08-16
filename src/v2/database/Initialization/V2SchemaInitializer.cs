using AlexDirectorConsole.V2.Database.Data;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Database.Initialization;

public static class V2SchemaInitializer
{
    public static async Task InitializeAsync(
        V2DbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;", cancellationToken);
    }
}