using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// Best-effort observed provider token counters. These are never billing authority;
/// provider timeouts, non-2xx replies and missing usage data can still incur charges.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261121110000_AiObservedTokenTelemetry")]
public sealed class AiObservedTokenTelemetry : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>("ObservedInputTokens", "AiProviderDailyUsage",
            type: "bigint", nullable: false, defaultValue: 0L);
        migrationBuilder.AddColumn<long>("ObservedOutputTokens", "AiProviderDailyUsage",
            type: "bigint", nullable: false, defaultValue: 0L);
        migrationBuilder.AddColumn<long>("ObservedResponses", "AiProviderDailyUsage",
            type: "bigint", nullable: false, defaultValue: 0L);
        migrationBuilder.Sql(
            "ALTER TABLE dbo.AiProviderDailyUsage ADD CONSTRAINT CK_AiProviderDailyUsage_ObservedTokens CHECK " +
            "([ObservedInputTokens]>=0 AND [ObservedOutputTokens]>=0 AND [ObservedResponses]>=0);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM dbo.AiProviderDailyUsage WHERE " +
            "ObservedInputTokens>0 OR ObservedOutputTokens>0 OR ObservedResponses>0) " +
            "THROW 51094, 'Retain observed AI token history before rollback.', 1;");
        migrationBuilder.Sql(
            "ALTER TABLE dbo.AiProviderDailyUsage DROP CONSTRAINT CK_AiProviderDailyUsage_ObservedTokens;");
        migrationBuilder.DropColumn("ObservedInputTokens", "AiProviderDailyUsage");
        migrationBuilder.DropColumn("ObservedOutputTokens", "AiProviderDailyUsage");
        migrationBuilder.DropColumn("ObservedResponses", "AiProviderDailyUsage");
    }
}