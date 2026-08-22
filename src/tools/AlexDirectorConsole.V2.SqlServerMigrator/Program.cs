using AlexDirectorConsole.V2.Database.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;

var sourcePath = GetRequiredArgument(args, "--source");
var targetConnectionString = GetOptionalArgument(args, "--target")
    ?? Environment.GetEnvironmentVariable("SQL_TARGET")
    ?? throw new ArgumentException("Missing required argument: --target or SQL_TARGET.");

if (!File.Exists(sourcePath))
{
    throw new FileNotFoundException("SQLite source database was not found.", sourcePath);
}

var sourceConnectionString = new SqliteConnectionStringBuilder
{
    DataSource = Path.GetFullPath(sourcePath),
    Mode = SqliteOpenMode.ReadOnly
}.ToString();

var options = new DbContextOptionsBuilder<V2DbContext>()
    .UseSqlServer(targetConnectionString)
    .Options;

await using (var dbContext = new V2DbContext(options))
{
    await dbContext.Database.EnsureCreatedAsync();
}

await using var sourceConnection = new SqliteConnection(sourceConnectionString);
await using var targetConnection = new SqlConnection(targetConnectionString);
await sourceConnection.OpenAsync();
await targetConnection.OpenAsync();

var sourceTables = await GetSourceTablesAsync(sourceConnection);
var targetTables = await GetTargetTablesAsync(targetConnection);
var missingTables = sourceTables.Where(table => !targetTables.Contains(table)).ToArray();
if (missingTables.Length > 0)
{
    throw new InvalidOperationException($"Target schema is missing tables: {string.Join(", ", missingTables)}");
}

foreach (var table in sourceTables)
{
    if (await GetTargetRowCountAsync(targetConnection, table) != 0)
    {
        throw new InvalidOperationException($"Target table '{table}' is not empty.");
    }
}

await using var transaction = (SqlTransaction)await targetConnection.BeginTransactionAsync();
try
{
    foreach (var table in sourceTables)
    {
        await SetConstraintsAsync(targetConnection, transaction, table, enabled: false);
    }

    foreach (var table in sourceTables)
    {
        var columns = await GetSourceColumnsAsync(sourceConnection, table);
        var targetColumnTypes = await GetTargetColumnTypesAsync(targetConnection, transaction, table);
        await using var selectCommand = sourceConnection.CreateCommand();
        selectCommand.CommandText = $"SELECT * FROM {QuoteSqliteIdentifier(table)};";
        await using var reader = await selectCommand.ExecuteReaderAsync();

        using var data = new DataTable();
        foreach (var column in columns)
        {
            if (!targetColumnTypes.TryGetValue(column, out var sqlType))
            {
                throw new InvalidOperationException($"Target table '{table}' is missing column '{column}'.");
            }

            data.Columns.Add(column, GetClrType(sqlType));
        }

        while (await reader.ReadAsync())
        {
            var row = data.NewRow();
            for (var index = 0; index < columns.Count; index++)
            {
                var value = reader.GetValue(index);
                row[index] = value is DBNull
                    ? DBNull.Value
                    : ConvertValue(value, targetColumnTypes[columns[index]]);
            }

            data.Rows.Add(row);
        }

        using var bulkCopy = new SqlBulkCopy(
            targetConnection,
            SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.TableLock,
            transaction)
        {
            DestinationTableName = $"[dbo].{QuoteSqlServerIdentifier(table)}",
            BatchSize = 1_000,
            BulkCopyTimeout = 600,
            EnableStreaming = true
        };
        foreach (var column in columns)
        {
            bulkCopy.ColumnMappings.Add(column, column);
        }

        await bulkCopy.WriteToServerAsync(data);
        Console.WriteLine($"Copied {table}: {data.Rows.Count} rows");
    }

    foreach (var table in sourceTables)
    {
        await SetConstraintsAsync(targetConnection, transaction, table, enabled: true);
    }

    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}

long totalRows = 0;
foreach (var table in sourceTables)
{
    var sourceRows = await GetSourceRowCountAsync(sourceConnection, table);
    var targetRows = await GetTargetRowCountAsync(targetConnection, table);
    if (sourceRows != targetRows)
    {
        throw new InvalidOperationException(
            $"Row count mismatch for '{table}': SQLite={sourceRows}, SQL Server={targetRows}.");
    }

    totalRows += targetRows;
}

Console.WriteLine($"Migration completed: {sourceTables.Count} tables, {totalRows} rows.");
return;

static string GetRequiredArgument(string[] arguments, string name)
{
    return GetOptionalArgument(arguments, name)
        ?? throw new ArgumentException($"Missing required argument: {name}");
}

static string? GetOptionalArgument(string[] arguments, string name)
{
    var index = Array.IndexOf(arguments, name);
    return index >= 0 && index + 1 < arguments.Length && !string.IsNullOrWhiteSpace(arguments[index + 1])
        ? arguments[index + 1]
        : null;
}

static async Task<List<string>> GetSourceTablesAsync(SqliteConnection connection)
{
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT name
        FROM sqlite_master
        WHERE type = 'table'
          AND name NOT LIKE 'sqlite_%'
          AND name <> '__EFMigrationsHistory'
        ORDER BY name;
        """;

    var tables = new List<string>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        tables.Add(reader.GetString(0));
    }

    return tables;
}

static async Task<HashSet<string>> GetTargetTablesAsync(SqlConnection connection)
{
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT TABLE_NAME
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = 'dbo' AND TABLE_TYPE = 'BASE TABLE';
        """;

    var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        tables.Add(reader.GetString(0));
    }

    return tables;
}

static async Task<List<string>> GetSourceColumnsAsync(SqliteConnection connection, string table)
{
    await using var command = connection.CreateCommand();
    command.CommandText = $"PRAGMA table_info({QuoteSqliteIdentifier(table)});";

    var columns = new List<string>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        columns.Add(reader.GetString(1));
    }

    return columns;
}

static async Task<Dictionary<string, string>> GetTargetColumnTypesAsync(
    SqlConnection connection,
    SqlTransaction transaction,
    string table)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = """
        SELECT COLUMN_NAME, DATA_TYPE
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @table;
        """;
    command.Parameters.AddWithValue("@table", table);

    var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        columns.Add(reader.GetString(0), reader.GetString(1));
    }

    return columns;
}

static Type GetClrType(string sqlType) => sqlType.ToLowerInvariant() switch
{
    "uniqueidentifier" => typeof(Guid),
    "bit" => typeof(bool),
    "tinyint" => typeof(byte),
    "smallint" => typeof(short),
    "int" => typeof(int),
    "bigint" => typeof(long),
    "real" => typeof(float),
    "float" => typeof(double),
    "decimal" or "numeric" or "money" or "smallmoney" => typeof(decimal),
    "date" or "datetime" or "datetime2" or "smalldatetime" => typeof(DateTime),
    "datetimeoffset" => typeof(DateTimeOffset),
    "time" => typeof(TimeSpan),
    "binary" or "varbinary" or "image" or "timestamp" or "rowversion" => typeof(byte[]),
    _ => typeof(string)
};

static object ConvertValue(object value, string sqlType) => sqlType.ToLowerInvariant() switch
{
    "uniqueidentifier" => value is Guid guid ? guid : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!),
    "bit" => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
    "tinyint" => Convert.ToByte(value, CultureInfo.InvariantCulture),
    "smallint" => Convert.ToInt16(value, CultureInfo.InvariantCulture),
    "int" => Convert.ToInt32(value, CultureInfo.InvariantCulture),
    "bigint" => Convert.ToInt64(value, CultureInfo.InvariantCulture),
    "real" => Convert.ToSingle(value, CultureInfo.InvariantCulture),
    "float" => Convert.ToDouble(value, CultureInfo.InvariantCulture),
    "decimal" or "numeric" or "money" or "smallmoney" => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
    "date" or "datetime" or "datetime2" or "smalldatetime" => value is DateTime dateTime
        ? dateTime
        : DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
    "datetimeoffset" => value is DateTimeOffset dateTimeOffset
        ? dateTimeOffset
        : DateTimeOffset.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
    "time" => value is TimeSpan timeSpan
        ? timeSpan
        : TimeSpan.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture),
    "binary" or "varbinary" or "image" or "timestamp" or "rowversion" => (byte[])value,
    _ => Convert.ToString(value, CultureInfo.InvariantCulture)!
};

static async Task<long> GetSourceRowCountAsync(SqliteConnection connection, string table)
{
    await using var command = connection.CreateCommand();
    command.CommandText = $"SELECT COUNT(*) FROM {QuoteSqliteIdentifier(table)};";
    return Convert.ToInt64(await command.ExecuteScalarAsync());
}

static async Task<long> GetTargetRowCountAsync(
    SqlConnection connection,
    string table,
    SqlTransaction? transaction = null)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = $"SELECT COUNT_BIG(*) FROM [dbo].{QuoteSqlServerIdentifier(table)};";
    return Convert.ToInt64(await command.ExecuteScalarAsync());
}

static async Task SetConstraintsAsync(
    SqlConnection connection,
    SqlTransaction transaction,
    string table,
    bool enabled)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = enabled
        ? $"ALTER TABLE [dbo].{QuoteSqlServerIdentifier(table)} WITH CHECK CHECK CONSTRAINT ALL;"
        : $"ALTER TABLE [dbo].{QuoteSqlServerIdentifier(table)} NOCHECK CONSTRAINT ALL;";
    await command.ExecuteNonQueryAsync();
}

static string QuoteSqliteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

static string QuoteSqlServerIdentifier(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";