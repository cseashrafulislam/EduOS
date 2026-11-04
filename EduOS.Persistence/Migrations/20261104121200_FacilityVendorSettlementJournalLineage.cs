using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// No new payment balance table: canonical posted, approved and contra journals
/// remain the single source of truth. Filtered keys prevent duplicate active
/// reversal and duplicate vendor bank execution reference.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261104121200_FacilityVendorSettlementJournalLineage")]
public sealed class FacilityVendorSettlementJournalLineage : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.Sql(@"
IF EXISTS (
 SELECT 1 FROM dbo.Journals
 WHERE SourceType = N'FacilityVendorServicePaymentReversal'
   AND SourceId IS NOT NULL AND IsDeleted = 0 AND [State] <> 5 AND [State] <> 7
 GROUP BY TenantId, SourceId HAVING COUNT_BIG(*) > 1
)
 THROW 53701, 'Duplicate active service payment reversal must be reconciled.', 1;
IF EXISTS (
 SELECT 1 FROM dbo.Journals
 WHERE SourceType = N'FacilityVendorServicePayment'
   AND ReferenceNumber IS NOT NULL AND IsDeleted = 0 AND [State] = 4
 GROUP BY TenantId, ReferenceNumber HAVING COUNT_BIG(*) > 1
)
 THROW 53702, 'Duplicate posted service payment bank reference must be reconciled.', 1;
");
        m.CreateIndex("UX_Journals_FacilityVendorPaymentReversal", "Journals",
            new[] { "TenantId", "SourceId" }, unique: true,
            filter: "[SourceType] = N'FacilityVendorServicePaymentReversal' AND [SourceId] IS NOT NULL AND [IsDeleted] = 0 AND [State] <> 5 AND [State] <> 7");
        m.CreateIndex("UX_Journals_FacilityVendorPaymentBankReference", "Journals",
            new[] { "TenantId", "ReferenceNumber" }, unique: true,
            filter: "[SourceType] = N'FacilityVendorServicePayment' AND [ReferenceNumber] IS NOT NULL AND [IsDeleted] = 0 AND [State] = 4");
        m.CreateIndex("IX_Journals_FacilityVendorPaymentLineage", "Journals",
            new[] { "TenantId", "SourceType", "SourceId", "State", "JournalDate" });
    }
    protected override void Down(MigrationBuilder m)
    {
        m.DropIndex("IX_Journals_FacilityVendorPaymentLineage", "Journals");
        m.DropIndex("UX_Journals_FacilityVendorPaymentBankReference", "Journals");
        m.DropIndex("UX_Journals_FacilityVendorPaymentReversal", "Journals");
    }
}
