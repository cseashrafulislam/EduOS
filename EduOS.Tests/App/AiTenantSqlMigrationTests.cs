using Microsoft.Data.SqlClient;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>Opt-in migration evidence against disposable SQL Server only.</summary>
[Collection("SqlServerIntegration")]
public sealed class AiTenantSqlMigrationTests
{
    [Fact]
    public async Task Tenant_AI_schema_is_migrated_and_concurrency_safe()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_AI_SQL_QA_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var info = new SqlConnectionStringBuilder(connection);
        Assert.Equal("127.0.0.1,14333", info.DataSource);
        Assert.Equal("EduOS_AiSqlQa", info.InitialCatalog);
        await using var db = new SqlConnection(connection);
        await db.OpenAsync();
        const string sql = @"
SELECT
  CASE WHEN OBJECT_ID(N'dbo.TenantAiProviderSettings', N'U') IS NOT NULL THEN 1 ELSE 0 END +
  CASE WHEN OBJECT_ID(N'dbo.TenantAiAgentControls', N'U') IS NOT NULL THEN 1 ELSE 0 END +
  CASE WHEN OBJECT_ID(N'dbo.AiProviderDailyUsage', N'U') IS NOT NULL THEN 1 ELSE 0 END +
  (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory
   WHERE MigrationId IN (
     N'20261121080000_AiDailyProviderCallBudget',
     N'20261121100000_TenantAiProviderCredentials',
     N'20261121110000_AiObservedTokenTelemetry',
     N'20261121120000_TenantAiAgentControls')) +
  (SELECT COUNT(*) FROM sys.indexes WHERE is_primary_key = 1
   AND object_id IN (OBJECT_ID(N'dbo.TenantAiProviderSettings'),
                     OBJECT_ID(N'dbo.TenantAiAgentControls'),
                     OBJECT_ID(N'dbo.AiProviderDailyUsage'))) +
  (SELECT COUNT(*) FROM sys.columns WHERE name = N'RowVersion' AND system_type_id = 189
   AND object_id IN (OBJECT_ID(N'dbo.TenantAiProviderSettings'),
                     OBJECT_ID(N'dbo.TenantAiAgentControls'))) +
  (SELECT COUNT(*) FROM sys.columns WHERE name IN
    (N'ObservedInputTokens',N'ObservedOutputTokens',N'ObservedResponses')
   AND object_id=OBJECT_ID(N'dbo.AiProviderDailyUsage')) +
  (SELECT COUNT(*) FROM sys.index_columns c JOIN sys.indexes i
    ON c.object_id=i.object_id AND c.index_id=i.index_id
   WHERE c.object_id=OBJECT_ID(N'dbo.TenantAiAgentControls')
     AND i.is_primary_key=1 AND c.key_ordinal>0) +
  (SELECT COUNT(*) FROM sys.index_columns c JOIN sys.indexes i
    ON c.object_id=i.object_id AND c.index_id=i.index_id
   WHERE c.object_id=OBJECT_ID(N'dbo.AiProviderDailyUsage')
     AND i.is_primary_key=1 AND c.key_ordinal>0);";
        await using var cmd = new SqlCommand(sql, db);
        Assert.Equal(19, Convert.ToInt32(await cmd.ExecuteScalarAsync()));
    }
}
