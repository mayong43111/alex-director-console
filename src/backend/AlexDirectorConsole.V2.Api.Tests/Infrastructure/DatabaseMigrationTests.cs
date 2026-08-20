using System.Text;
using System.Text.Json;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AlexDirectorConsole.V2.Api.Tests.Infrastructure;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public async Task RemoveProtagonistSpecies_cleans_existing_creative_settings()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"alex-director-v2-migration-tests-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<V2DbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;

        try
        {
            await using var dbContext = new V2DbContext(options);
            var migrator = dbContext.GetService<IMigrator>();
            await migrator.MigrateAsync("20260820050000_SeedProjectSettingsTextAgents");

            const string legacyJson =
                "{\"visualStyle\":\"水墨\",\"protagonistSpecies\":\"拟人动物\",\"characterDesign\":\"角色约束\"}";
            var projectId = Guid.NewGuid();
            dbContext.Projects.Add(new Project
            {
                Id = projectId,
                Name = "迁移测试项目",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();

            var asset = new Asset
            {
                ProjectId = projectId,
                ResourceId = Guid.NewGuid(),
                Version = 1,
                Number = 1,
                Type = "creative-settings",
                Name = "旧版项目设定",
                SchemaVersion = 1,
                DocumentJson = legacyJson,
                SizeBytes = Encoding.UTF8.GetByteCount(legacyJson),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            dbContext.Assets.Add(asset);
            await dbContext.SaveChangesAsync();

            await migrator.MigrateAsync();
            dbContext.ChangeTracker.Clear();

            var migrated = await dbContext.Assets.SingleAsync(item => item.Id == asset.Id);
            using var document = JsonDocument.Parse(migrated.DocumentJson!);
            Assert.False(document.RootElement.TryGetProperty("protagonistSpecies", out _));
            Assert.Equal("水墨", document.RootElement.GetProperty("visualStyle").GetString());
            Assert.Equal("角色约束", document.RootElement.GetProperty("characterDesign").GetString());
            Assert.Equal(2, migrated.SchemaVersion);
            Assert.Equal(Encoding.UTF8.GetByteCount(migrated.DocumentJson!), migrated.SizeBytes);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}