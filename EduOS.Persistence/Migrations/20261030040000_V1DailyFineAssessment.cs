using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>Non-destructive historical snapshots and one-assessment-per-overdue-invoice protection.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261030040000_V1DailyFineAssessment")]
public partial class V1DailyFineAssessment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "SourceInvoiceId", table: "StudentFines", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<DateOnly>(name: "SourceInvoiceDueDate", table: "StudentFines", type: "date", nullable: true);
        migrationBuilder.AddColumn<int>(name: "ChargedOverdueDays", table: "StudentFines", type: "int", nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "DailyRateSnapshot", table: "StudentFines", type: "decimal(18,2)", nullable: true);
        migrationBuilder.AddCheckConstraint(name: "CK_StudentFine_DailySource", table: "StudentFines",
            sql: "([SourceInvoiceId] IS NULL AND [SourceInvoiceDueDate] IS NULL AND [ChargedOverdueDays] IS NULL AND [DailyRateSnapshot] IS NULL) OR " +
                "([SourceInvoiceId] IS NOT NULL AND [SourceInvoiceDueDate] IS NOT NULL AND [ChargedOverdueDays] BETWEEN 1 AND 3650 AND [DailyRateSnapshot] > 0)");
        migrationBuilder.CreateIndex(name: "IX_StudentFines_TenantId_SourceInvoiceId_FineRuleId", table: "StudentFines",
            columns: new[] { "TenantId", "SourceInvoiceId", "FineRuleId" }, unique: true,
            filter: "[SourceInvoiceId] IS NOT NULL");
        migrationBuilder.AddForeignKey(name: "FK_StudentFines_StudentInvoices_TenantId_SourceInvoiceId",
            table: "StudentFines", columns: new[] { "TenantId", "SourceInvoiceId" },
            principalTable: "StudentInvoices", principalColumns: new[] { "TenantId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_StudentFines_StudentInvoices_TenantId_SourceInvoiceId", table: "StudentFines");
        migrationBuilder.DropIndex(name: "IX_StudentFines_TenantId_SourceInvoiceId_FineRuleId", table: "StudentFines");
        migrationBuilder.DropCheckConstraint(name: "CK_StudentFine_DailySource", table: "StudentFines");
        migrationBuilder.DropColumn(name: "DailyRateSnapshot", table: "StudentFines");
        migrationBuilder.DropColumn(name: "ChargedOverdueDays", table: "StudentFines");
        migrationBuilder.DropColumn(name: "SourceInvoiceDueDate", table: "StudentFines");
        migrationBuilder.DropColumn(name: "SourceInvoiceId", table: "StudentFines");
    }
}
