using Microsoft.Data.SqlClient;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// An explicitly opted-in recovery drill for the disposable GitHub Actions SQL
/// instance. Never points at a customer database or alters the source database.
/// </summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerBackupRestoreDrillIntegrationTests
{
    [Fact]
    public async Task Disposable_sql_backup_restores_schema_finance_and_tenant_evidence()
    {
        if (Environment.GetEnvironmentVariable("EDUOS_SQL_BACKUP_RESTORE_DRILL") != "1") return;
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Recovery drill requires an explicit disposable SQL connection.");
        var builder = new SqlConnectionStringBuilder(connection);
        if (!string.Equals(builder.InitialCatalog, "EduOS_MigrationSmoke", StringComparison.Ordinal) ||
            !string.Equals(builder.DataSource, "127.0.0.1,14333", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Recovery drill is restricted to the CI disposable SQL database.");

        // Unique SQL identifiers and file names are generated locally, not
        // derived from user input or connection-string metadata.
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var restored = "EduOS_RestoreDrill_" + suffix;
        var file = "/var/opt/mssql/data/eduos-recovery-" + suffix + ".bak";
        builder.InitialCatalog = "master";
        await using var sql = new SqlConnection(builder.ConnectionString);
        await sql.OpenAsync();

        await ExecuteAsync(sql, $"BACKUP DATABASE [EduOS_MigrationSmoke] " +
            $"TO DISK = N'{file}' WITH COPY_ONLY, INIT, CHECKSUM;", 300);
        await ExecuteAsync(sql, $"RESTORE VERIFYONLY FROM DISK = N'{file}' WITH CHECKSUM;", 300);

        var moves = new List<string>();
        await using (var command = new SqlCommand($"RESTORE FILELISTONLY FROM DISK = N'{file}';", sql))
        {
            command.CommandTimeout = 120;
            await using var reader = await command.ExecuteReaderAsync();
            var logicalOrdinal = reader.GetOrdinal("LogicalName");
            var typeOrdinal = reader.GetOrdinal("Type");
            var index = 0;
            while (await reader.ReadAsync())
            {
                var type = reader.GetString(typeOrdinal);
                if (type != "D" && type != "L")
                    throw new InvalidOperationException("Unexpected source database file type.");
                var originalName = reader.GetString(logicalOrdinal).Replace("'", "''");
                var target = "/var/opt/mssql/data/" + restored + "_" + index++ +
                    (type == "L" ? ".ldf" : ".mdf");
                moves.Add($"MOVE N'{originalName}' TO N'{target}'");
            }
        }
        Assert.True(moves.Count >= 2, "The disposable database needs its data and log files.");
        await ExecuteAsync(sql, $"RESTORE DATABASE [{restored}] FROM DISK = N'{file}' " +
            "WITH " + string.Join(", ", moves) + ", RECOVERY, CHECKSUM;", 300);
        await ExecuteAsync(sql, $"DBCC CHECKDB ([{restored}]) WITH NO_INFOMSGS, ALL_ERRORMSGS;", 300);

        var sourceTables = new[] { "__EFMigrationsHistory", "Tenants", "StudentInvoices",
            "Journals", "JournalLines", "AuditLogs" };
        foreach (var table in sourceTables)
        {
            var sourceCount = await ScalarAsync(sql,
                $"SELECT COUNT_BIG(*) FROM [EduOS_MigrationSmoke].[dbo].[{table}];");
            var restoredCount = await ScalarAsync(sql,
                $"SELECT COUNT_BIG(*) FROM [{restored}].[dbo].[{table}];");
            Assert.Equal(sourceCount, restoredCount);
        }
        Assert.True(await ScalarAsync(sql,
            $"SELECT COUNT_BIG(*) FROM [{restored}].[dbo].[__EFMigrationsHistory];") > 0);

        foreach (var column in new[] { "DebitAmount", "CreditAmount" })
        {
            var sourceTotal = await MoneyAsync(sql,
                $"SELECT COALESCE(SUM([{column}]), 0) FROM [EduOS_MigrationSmoke].[dbo].[JournalLines];");
            var restoredTotal = await MoneyAsync(sql,
                $"SELECT COALESCE(SUM([{column}]), 0) FROM [{restored}].[dbo].[JournalLines];");
            Assert.Equal(sourceTotal, restoredTotal);
        }

        // Both restored database and backup file live only inside the short-lived
        // SQL Server CI container, which the workflow removes in its final step.
    }

    private static async Task ExecuteAsync(SqlConnection sql, string commandText, int timeout)
    {
        await using var command = new SqlCommand(commandText, sql) { CommandTimeout = timeout };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(SqlConnection sql, string commandText)
    {
        await using var command = new SqlCommand(commandText, sql) { CommandTimeout = 120 };
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<decimal> MoneyAsync(SqlConnection sql, string commandText)
    {
        await using var command = new SqlCommand(commandText, sql) { CommandTimeout = 120 };
        return Convert.ToDecimal(await command.ExecuteScalarAsync());
    }
}
