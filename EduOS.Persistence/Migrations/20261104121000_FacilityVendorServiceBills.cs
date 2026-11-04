using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// Additive non-stock vendor invoice source and a single active payable Journal
/// per bill. No pre-existing PurchaseOrder, GoodsReceipt or Finance row is changed.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261104121000_FacilityVendorServiceBills")]
public sealed class FacilityVendorServiceBills : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.Sql(@"
IF EXISTS (
 SELECT 1 FROM dbo.Journals
 WHERE [SourceType] = N'FacilityVendorServiceBill' AND [SourceId] IS NOT NULL
   AND [IsDeleted] = 0 AND [State] <> 5 AND [State] <> 7
 GROUP BY [TenantId], [SourceId] HAVING COUNT_BIG(*) > 1
)
 THROW 52401, 'Existing duplicate FacilityVendorServiceBill GL sources require reconciliation.', 1;
");
        m.CreateTable("FacilityVendorServiceBills", columns: t => new
        {
            Id = t.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>(type: "bigint", nullable: false),
            PublicId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            ClientRequestId = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            FacilityWorkOrderId = t.Column<long>(type: "bigint", nullable: false),
            CampusId = t.Column<long>(type: "bigint", nullable: false),
            SupplierId = t.Column<long>(type: "bigint", nullable: false),
            SupplierInvoiceNumber = t.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
            NormalizedSupplierInvoiceNumber = t.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
            InvoiceDate = t.Column<DateOnly>(type: "date", nullable: false),
            DueDate = t.Column<DateOnly>(type: "date", nullable: false),
            AmountBdt = t.Column<decimal>(type: "decimal(18,2)", nullable: false),
            StateCode = t.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
            Reason = t.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false),
            PreparedByUserId = t.Column<long>(type: "bigint", nullable: false),
            SubmitRequestId = t.Column<Guid>(type: "uniqueidentifier", nullable: true),
            SubmittedAtUtc = t.Column<DateTime>(type: "datetime2", nullable: true),
            ReviewRequestId = t.Column<Guid>(type: "uniqueidentifier", nullable: true),
            ReviewFingerprint = t.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
            ReviewedAtUtc = t.Column<DateTime>(type: "datetime2", nullable: true),
            ReviewedByUserId = t.Column<long>(type: "bigint", nullable: true),
            ReviewReason = t.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: true),
            JournalId = t.Column<long>(type: "bigint", nullable: true),
            CreatedAt = t.Column<DateTime>(type: "datetime2", nullable: false),
            CreatedBy = t.Column<long>(type: "bigint", nullable: true),
            UpdatedAt = t.Column<DateTime>(type: "datetime2", nullable: true),
            UpdatedBy = t.Column<long>(type: "bigint", nullable: true),
            IsDeleted = t.Column<bool>(type: "bit", nullable: false),
            DeletedAt = t.Column<DateTime>(type: "datetime2", nullable: true),
            DeletedBy = t.Column<long>(type: "bigint", nullable: true),
            RowVersion = t.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_FacilityVendorServiceBills", x => x.Id);
            t.UniqueConstraint("AK_FacilityVendorServiceBills_TenantId_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_FacilityVendorServiceBills_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_FacilityVendorServiceBills_FacilityWorkOrders_TenantId_FacilityWorkOrderId",
                x => new { x.TenantId, x.FacilityWorkOrderId },
                "FacilityWorkOrders", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_FacilityVendorServiceBills_Campuses_TenantId_CampusId",
                x => new { x.TenantId, x.CampusId },
                "Campuses", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_FacilityVendorServiceBills_ProcurementSuppliers_TenantId_SupplierId",
                x => new { x.TenantId, x.SupplierId },
                "ProcurementSuppliers", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_FacilityVendorServiceBills_TenantId_PublicId",
            "FacilityVendorServiceBills", new[] { "TenantId", "PublicId" }, unique: true);
        m.CreateIndex("IX_FacilityVendorServiceBills_TenantId_ClientRequestId",
            "FacilityVendorServiceBills", new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("IX_FacilityVendorServiceBills_TenantId_SubmitRequestId",
            "FacilityVendorServiceBills", new[] { "TenantId", "SubmitRequestId" }, unique: true,
            filter: "[SubmitRequestId] IS NOT NULL");
        m.CreateIndex("IX_FacilityVendorServiceBills_TenantId_ReviewRequestId",
            "FacilityVendorServiceBills", new[] { "TenantId", "ReviewRequestId" }, unique: true,
            filter: "[ReviewRequestId] IS NOT NULL");
        m.CreateIndex("IX_FacilityVendorServiceBills_TenantId_FacilityWorkOrderId",
            "FacilityVendorServiceBills", new[] { "TenantId", "FacilityWorkOrderId" }, unique: true,
            filter: "[IsDeleted] = 0");
        m.CreateIndex("IX_FacilityVendorServiceBills_TenantId_SupplierId_NormalizedSupplierInvoiceNumber",
            "FacilityVendorServiceBills", new[] { "TenantId", "SupplierId", "NormalizedSupplierInvoiceNumber" }, unique: true,
            filter: "[IsDeleted] = 0");
        m.CreateIndex("IX_FacilityVendorServiceBills_TenantId_CampusId_StateCode_InvoiceDate",
            "FacilityVendorServiceBills", new[] { "TenantId", "CampusId", "StateCode", "InvoiceDate" });
        m.AddCheckConstraint("CK_FacilityVendorServiceBills_StateAmount", "FacilityVendorServiceBills",
            "[StateCode] IN (N'Draft',N'Submitted',N'Posted',N'Rejected') AND [AmountBdt] > 0");
        m.CreateIndex("UX_Journals_FacilityVendorServiceBill", "Journals",
            new[] { "TenantId", "SourceId" }, unique: true,
            filter: "[SourceType] = N'FacilityVendorServiceBill' AND [SourceId] IS NOT NULL AND [IsDeleted] = 0 AND [State] <> 5 AND [State] <> 7");
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropIndex("UX_Journals_FacilityVendorServiceBill", "Journals");
        m.DropTable("FacilityVendorServiceBills");
    }
}
