using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Additive PO state expansion for independent cancellation approval.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261031004500_ProcurementApprovedCancellationReview")]
public sealed class ProcurementApprovedCancellationReview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_ProcurementPurchaseOrder_State", table: "ProcurementPurchaseOrders");
        migrationBuilder.AddCheckConstraint(
            name: "CK_ProcurementPurchaseOrder_State", table: "ProcurementPurchaseOrders",
            sql: "[StateCode] IN (N'Draft',N'Submitted',N'Approved',N'CancelRequested',N'Rejected',N'Cancelled')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Pending requests must be resolved before restoring the old state constraint.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [ProcurementPurchaseOrders] WHERE [StateCode] = N'CancelRequested')
                THROW 51001, 'Resolve pending PO cancellation requests before rolling back this migration.', 1;
            """);
        migrationBuilder.DropCheckConstraint(
            name: "CK_ProcurementPurchaseOrder_State", table: "ProcurementPurchaseOrders");
        migrationBuilder.AddCheckConstraint(
            name: "CK_ProcurementPurchaseOrder_State", table: "ProcurementPurchaseOrders",
            sql: "[StateCode] IN (N'Draft',N'Submitted',N'Approved',N'Rejected',N'Cancelled')");
    }
}
