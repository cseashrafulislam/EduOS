using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// Additive control quantity on reserved draft lines. Posted stock continues to
/// derive exclusively from InventoryMovements in the existing canonical ledger.
/// Rollback removes fulfilment tracking and must not be used after partial issues
/// exist without an approved archival/reconciliation plan.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261030201000_InventoryReservedIssuePartialFulfillment")]
public sealed class InventoryReservedIssuePartialFulfillment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "FulfilledQuantity",
            table: "InventoryMovementLines",
            type: "decimal(18,6)",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddCheckConstraint(
            name: "CK_InventoryMovementLine_FulfilledQuantity",
            table: "InventoryMovementLines",
            sql: "[FulfilledQuantity] >= 0 AND [FulfilledQuantity] <= [Quantity]");

        migrationBuilder.CreateIndex(
            name: "IX_InventoryMovements_TenantId_SourceType_SourceId",
            table: "InventoryMovements",
            columns: new[] { "TenantId", "SourceType", "SourceId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A rollback that forgets consumed holds could allow the original reserved
        // draft to post again. Require an explicitly reconciled zero-consumption
        // state before dropping the control column.
        migrationBuilder.Sql(@"IF EXISTS (
            SELECT 1 FROM dbo.InventoryMovementLines WHERE FulfilledQuantity <> 0
        ) THROW 51050, 'Partial issue evidence must be reconciled before rollback.', 1;");

        migrationBuilder.DropIndex(
            name: "IX_InventoryMovements_TenantId_SourceType_SourceId",
            table: "InventoryMovements");

        migrationBuilder.DropCheckConstraint(
            name: "CK_InventoryMovementLine_FulfilledQuantity",
            table: "InventoryMovementLines");

        migrationBuilder.DropColumn(
            name: "FulfilledQuantity",
            table: "InventoryMovementLines");
    }
}
