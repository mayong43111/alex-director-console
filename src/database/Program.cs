using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Initialization;
using Microsoft.EntityFrameworkCore;

var command = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal)) ?? "init";
var connectionArgumentIndex = Array.IndexOf(args, "--connection");
var connectionString = connectionArgumentIndex >= 0 && connectionArgumentIndex + 1 < args.Length
	? args[connectionArgumentIndex + 1]
	: Environment.GetEnvironmentVariable("ALEX_V2_DB_CONNECTION")
		?? $"Data Source={DatabasePaths.GetDefaultDatabasePath()}";

DatabasePaths.EnsureDatabaseDirectory(connectionString);
var options = new DbContextOptionsBuilder<V2DbContext>()
	.UseSqlite(connectionString)
	.Options;

await using var dbContext = new V2DbContext(options);

switch (command.ToLowerInvariant())
{
	case "init":
		await V2SchemaInitializer.InitializeAsync(dbContext);
		break;
	case "migrate":
		await V2SchemaInitializer.InitializeAsync(dbContext);
		break;
	case "status":
		if (!await dbContext.Database.CanConnectAsync())
		{
			Console.Error.WriteLine("V2 database is not reachable.");
			return 1;
		}

		var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
		Console.WriteLine($"Pending migrations: {pendingMigrations.Count()}");
		Console.WriteLine($"Projects: {await dbContext.Projects.CountAsync()}");
		Console.WriteLine($"Production episodes: {await dbContext.ProductionEpisodes.CountAsync()}");
		Console.WriteLine($"Assets: {await dbContext.Assets.CountAsync()}");
		Console.WriteLine($"Resource states: {await dbContext.ResourceStates.CountAsync()}");
		Console.WriteLine($"Asset dependencies: {await dbContext.AssetDependencies.CountAsync()}");
		var connection = dbContext.Database.GetDbConnection();
		await connection.OpenAsync();
		await using (var tableCountCommand = connection.CreateCommand())
		{
			tableCountCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
			Console.WriteLine($"Schema tables: {await tableCountCommand.ExecuteScalarAsync()}");
		}

		var businessTableNames = new List<string>();
		await using (var tableNamesCommand = connection.CreateCommand())
		{
			tableNamesCommand.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory';";
			await using var reader = await tableNamesCommand.ExecuteReaderAsync();
			while (await reader.ReadAsync())
			{
				businessTableNames.Add(reader.GetString(0));
			}
		}

		long businessRows = 0;
		foreach (var tableName in businessTableNames)
		{
			await using var rowCountCommand = connection.CreateCommand();
			rowCountCommand.CommandText = $"SELECT COUNT(*) FROM \"{tableName.Replace("\"", "\"\"")}\";";
			businessRows += Convert.ToInt64(await rowCountCommand.ExecuteScalarAsync());
		}

		Console.WriteLine($"Business tables: {businessTableNames.Count}");
		Console.WriteLine($"Business rows: {businessRows}");

		await using (var foreignKeyCommand = connection.CreateCommand())
		{
			foreignKeyCommand.CommandText = "SELECT COUNT(*) FROM pragma_foreign_key_check;";
			Console.WriteLine($"Foreign key violations: {await foreignKeyCommand.ExecuteScalarAsync()}");
		}
		break;
	case "cancel-active":
		var cancelledAt = DateTimeOffset.UtcNow;
		var activeTasks = await dbContext.AgentTasks
			.Where(item => item.Status == "queued" || item.Status == "running")
			.ToListAsync();
		foreach (var task in activeTasks)
		{
			task.Status = "cancelled";
			task.CurrentStep = "已由管理员停止";
			task.CancellationRequestedAtUtc = cancelledAt;
			task.CompletedAtUtc = cancelledAt;
			task.UpdatedAtUtc = cancelledAt;
		}
		var activeRuns = await dbContext.ProductionRuns
			.Where(item => item.RunType == "shot-video" && (item.Status == "queued" || item.Status == "running"))
			.ToListAsync();
		foreach (var run in activeRuns)
		{
			run.Status = "cancelled";
			run.CurrentStage = "cancelled";
			run.LastError = "已由管理员停止";
			run.CompletedAtUtc = cancelledAt;
			run.UpdatedAtUtc = cancelledAt;
			run.LeaseOwner = null;
			run.LeaseExpiresAtUtc = null;
		}
		var activeItems = await dbContext.ProductionRunItems
			.Where(item => activeRuns.Select(run => run.Id).Contains(item.RunId)
				&& (item.Status == "queued" || item.Status == "running"))
			.ToListAsync();
		foreach (var item in activeItems)
		{
			item.Status = "cancelled";
			item.ErrorCode = "Cancelled";
			item.ErrorDetail = "已由管理员停止";
			item.CompletedAtUtc = cancelledAt;
		}
		await dbContext.SaveChangesAsync();
		Console.WriteLine($"Cancelled tasks: {activeTasks.Count}");
		Console.WriteLine($"Cancelled shot-video runs: {activeRuns.Count}");
		break;
	default:
		Console.Error.WriteLine("Usage: dotnet run -- [init|migrate|status|cancel-active] [--connection <connection-string>]");
		return 2;
}

Console.WriteLine($"V2 database '{command}' completed.");
Console.WriteLine(dbContext.Database.GetDbConnection().DataSource);
return 0;
