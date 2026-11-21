using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// One small daily reservation counter per tenant. Reserved attempts are intentionally
/// counted even on provider timeout: the request may have incurred provider charges.
/// Not a source for actual billing or token consumption.
 /// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261121080000_AiDailyProviderCallBudget")]
public sealed class AiDailyProviderCallBudget : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("AiProviderDailyUsage", columns: table => new
        {
            TenantId = table.Column<long>(type: "bigint", nullable: false),
            UsageDateUtc = table.Column<DateOnly>(type: "date", nullable: false),
            ReservedCalls = table.Column<int>(type: "int", nullable: false),
            LastReservedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_AiProviderDailyUsage", x => new { x.TenantId, x.UsageDateUtc });
            table.ForeignKey("FK_AiProviderDailyUsage_Tenants", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            table.CheckConstraint("CK_AiProviderDailyUsage_Calls", "[ReservedCalls] >= 0");
        });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM dbo.AiProviderDailyUsage) THROW 51091, 'Retain AI usage history before rolling back the daily budget migration.', 1;");
        migrationBuilder.DropTable("AiProviderDailyUsage");
    }
}
