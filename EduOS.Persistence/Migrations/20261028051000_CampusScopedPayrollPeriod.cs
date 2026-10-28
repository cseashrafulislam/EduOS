using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>
/// Backward-compatible campus payroll indexes. Preserve global payroll rows and
/// reject ambiguous overlapping global+campus periods before enabling split runs.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261028051000_CampusScopedPayrollPeriod")]
public sealed class CampusScopedPayrollPeriod : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Existing unique index already prevented active duplicates; no data backfill needed.
        migrationBuilder.DropIndex("IX_PayrollRuns_TenantId_Year_Month", "PayrollRuns");
        migrationBuilder.CreateIndex(
            name: "IX_PayrollRuns_TenantId_Year_Month", table: "PayrollRuns",
            columns: new[] { "TenantId", "Year", "Month" }, unique: true,
            filter: "[CampusId] IS NULL AND [State] <> 5 AND [IsDeleted] = 0");
        migrationBuilder.CreateIndex(
            name: "IX_PayrollRuns_TenantId_Year_Month_CampusId", table: "PayrollRuns",
            columns: new[] { "TenantId", "Year", "Month", "CampusId" }, unique: true,
            filter: "[CampusId] IS NOT NULL AND [State] <> 5 AND [IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM [PayrollRuns]
                WHERE [State] <> 5 AND [IsDeleted] = 0
                GROUP BY [TenantId], [Year], [Month]
                HAVING COUNT_BIG(*) > 1
            )
                THROW 51082, 'Cannot roll back campus payroll index while multiple campus runs exist for one period.', 1;
            """);
        migrationBuilder.DropIndex("IX_PayrollRuns_TenantId_Year_Month_CampusId", "PayrollRuns");
        migrationBuilder.DropIndex("IX_PayrollRuns_TenantId_Year_Month", "PayrollRuns");
        migrationBuilder.CreateIndex(
            name: "IX_PayrollRuns_TenantId_Year_Month", table: "PayrollRuns",
            columns: new[] { "TenantId", "Year", "Month" }, unique: true,
            filter: "[State] <> 5 AND [IsDeleted] = 0");
    }
}
