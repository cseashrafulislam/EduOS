using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// Database-only filtered uniqueness on the existing polymorphic source reference.
/// This is one canonical movement ledger, not a second stock balance table.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261030174000_InventoryTransferReceiptUniqueness")]
public sealed class InventoryTransferReceiptUniqueness : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "UX_InventoryTransferReceipt_Tenant_Source",
            table: "InventoryMovements",
            columns: new[] { "TenantId", "SourceId" },
            unique: true,
            filter: "[SourceType] = N'TransferReceipt' AND [SourceId] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("UX_InventoryTransferReceipt_Tenant_Source", "InventoryMovements");
    }
}
