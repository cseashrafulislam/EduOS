using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// Aligns the two new Facilities sources with the existing DbContext convention
/// that requires globally unique PublicId values. Existing tenant-scoped keys
/// and historical Finance/Payroll rows remain unchanged.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261104121300_FacilityPublicIdConventionIndexes")]
public sealed class FacilityPublicIdConventionIndexes : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.Sql(@"
IF EXISTS (
 SELECT 1 FROM dbo.FacilityVendorServiceBills
 GROUP BY PublicId HAVING COUNT_BIG(*) > 1
)
 THROW 53711, 'Duplicate Facilities vendor bill PublicId values require reconciliation.', 1;
IF EXISTS (
 SELECT 1 FROM dbo.FacilityLabourCostAllocations
 GROUP BY PublicId HAVING COUNT_BIG(*) > 1
)
 THROW 53712, 'Duplicate Facilities labour allocation PublicId values require reconciliation.', 1;
");
        m.CreateIndex("IX_FacilityVendorServiceBills_PublicId",
            "FacilityVendorServiceBills", "PublicId", unique: true);
        m.CreateIndex("IX_FacilityLabourCostAllocations_PublicId",
            "FacilityLabourCostAllocations", "PublicId", unique: true);
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropIndex("IX_FacilityLabourCostAllocations_PublicId", "FacilityLabourCostAllocations");
        m.DropIndex("IX_FacilityVendorServiceBills_PublicId", "FacilityVendorServiceBills");
    }
}
