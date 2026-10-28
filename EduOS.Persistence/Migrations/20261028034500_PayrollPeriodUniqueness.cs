using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>
/// Reject concurrent duplicate active institution payroll periods. Historical cancelled
/// runs remain intact and do not prevent a controlled recalculation.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261028034500_PayrollPeriodUniqueness")]
public sealed class PayrollPeriodUniqueness : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM [PayrollRuns]
                WHERE [State] <> 5 AND [IsDeleted] = 0
                GROUP BY [TenantId], [Year], [Month]
                HAVING COUNT_BIG(*) > 1
            )
                THROW 51081, 'Existing duplicate active payroll periods require tenant-scoped reconciliation before this migration.', 1;
            """);
        migrationBuilder.CreateIndex(
            name: "IX_PayrollRuns_TenantId_Year_Month",
            table: "PayrollRuns",
            columns: new[] { "TenantId", "Year", "Month" },
            unique: true,
            filter: "[State] <> 5 AND [IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PayrollRuns_TenantId_Year_Month", table: "PayrollRuns");
    }
}
