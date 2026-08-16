using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Data.Sqlite;

namespace AlexDirectorConsole.V2.Database.Data;

public sealed class V2DbContextFactory : IDesignTimeDbContextFactory<V2DbContext>
{
    public V2DbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ALEX_V2_DB_CONNECTION")
            ?? $"Data Source={DatabasePaths.GetDefaultDatabasePath()}";
        var options = new DbContextOptionsBuilder<V2DbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new V2DbContext(options);
    }
}

public static class DatabasePaths
{
    public static string GetDefaultDatabasePath()
    {
        var repositoryRoot = FindRepositoryRoot(Environment.CurrentDirectory);
        var appDataPath = repositoryRoot is null
            ? Path.Combine(AppContext.BaseDirectory, "App_Data")
            : Path.Combine(repositoryRoot, "src", "v2", "database", "App_Data");
        Directory.CreateDirectory(appDataPath);
        return Path.Combine(appDataPath, "alex-director-v2.db");
    }

    public static void EnsureDatabaseDirectory(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (builder.Mode == SqliteOpenMode.Memory || string.IsNullOrWhiteSpace(builder.DataSource))
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(builder.DataSource));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string? FindRepositoryRoot(string startPath)
    {
        for (var directory = new DirectoryInfo(startPath); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AlexDirectorConsole.sln")))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}