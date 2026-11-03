using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>One active Finance expense and reversal per canonical Facility stock issue.
/// No stock, existing journal, or account balances are rewritten.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261103214000_FacilityPartsFinanceLineage")]
public sealed class FacilityPartsFinanceLineage : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.Sql(@"
IF EXISTS (
    SELECT 1 FROM dbo.Journals
    WHERE SourceType = N'InventoryMovement' AND SourceId IS NOT NULL
        AND IsDeleted = 0 AND State NOT IN (5,7)
    GROUP BY TenantId, SourceId HAVING COUNT_BIG(*) > 1
)
    THROW 52031, 'Pre-existing duplicate active InventoryMovement journals require controlled reconciliation before Facilities GL migration.', 1;
IF EXISTS (
    SELECT 1 FROM dbo.Journals
    WHERE SourceType = N'FacilityPartsExpenseReversal' AND SourceId IS NOT NULL
        AND IsDeleted = 0 AND State NOT IN (5,7)
    GROUP BY TenantId, SourceId HAVING COUNT_BIG(*) > 1
)
    THROW 52032, 'Pre-existing duplicate Facilities Finance reversals require controlled reconciliation.', 1;
");
        m.CreateIndex("UX_Journals_FacilityPartsExpense", "Journals",
            new[] { "TenantId", "SourceId" }, unique: true,
            filter: "[SourceType] = N'InventoryMovement' AND [SourceId] IS NOT NULL AND [IsDeleted] = 0 AND [State] <> 5 AND [State] <> 7");
        m.CreateIndex("UX_Journals_FacilityPartsReversal", "Journals",
            new[] { "TenantId", "SourceId" }, unique: true,
            filter: "[SourceType] = N'FacilityPartsExpenseReversal' AND [SourceId] IS NOT NULL AND [IsDeleted] = 0 AND [State] <> 5 AND [State] <> 7");
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropIndex("UX_Journals_FacilityPartsReversal", "Journals");
        m.DropIndex("UX_Journals_FacilityPartsExpense", "Journals");
    }
}
