using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>Partial PO receipt audit with exclusive trusted canonical inventory provenance.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261031190000_V3ProcurementGoodsReceipts")]
public sealed class V3ProcurementGoodsReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("ProcurementGoodsReceipts", columns: table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            PurchaseOrderId = table.Column<long>(type: "bigint", nullable: false),
            CampusId = table.Column<long>(type: "bigint", nullable: false),
            LocationId = table.Column<long>(type: "bigint", nullable: false),
            StateCode = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
            Reason = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false),
            CreateFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            SubmitRequestId = table.Column<Guid?>(type: "uniqueidentifier", nullable: true),
            SubmitFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
            ReviewRequestId = table.Column<Guid?>(type: "uniqueidentifier", nullable: true),
            ReviewFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
            SubmittedAtUtc = table.Column<DateTime?>(type: "datetime2", nullable: true),
            SubmittedByUserId = table.Column<long?>(type: "bigint", nullable: true),
            ReviewedAtUtc = table.Column<DateTime?>(type: "datetime2", nullable: true),
            ReviewedByUserId = table.Column<long?>(type: "bigint", nullable: true),
            ReviewReason = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: true),
            ValuationMode = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
            PostedMovementId = table.Column<long?>(type: "bigint", nullable: true),
            CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
            UpdatedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
            CreatedBy = table.Column<long?>(type: "bigint", nullable: true),
            UpdatedBy = table.Column<long?>(type: "bigint", nullable: true),
            IsDeleted = table.Column<bool>(type: "bit", nullable: false),
            DeletedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
            DeletedBy = table.Column<long?>(type: "bigint", nullable: true),
            RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
            TenantId = table.Column<long>(type: "bigint", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_ProcurementGoodsReceipts", x => x.Id);
            table.UniqueConstraint("AK_ProcurementGoodsReceipts_TenantId_Id", x => new { x.TenantId, x.Id });
            table.CheckConstraint("CK_ProcurementGoodsReceipt_State", "[StateCode] IN (N'Draft',N'Submitted',N'Posted',N'Rejected')");
            table.CheckConstraint("CK_ProcurementGoodsReceipt_Review", "([StateCode] IN (N'Draft',N'Submitted') AND [ReviewedAtUtc] IS NULL AND [ReviewedByUserId] IS NULL) OR ([StateCode] IN (N'Posted',N'Rejected') AND [ReviewedAtUtc] IS NOT NULL AND [ReviewedByUserId] IS NOT NULL)");
            table.ForeignKey("FK_ProcurementGoodsReceipts_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_ProcurementGoodsReceipts_ProcurementPurchaseOrders_TenantId_PurchaseOrderId",
                x => new { x.TenantId, x.PurchaseOrderId }, "ProcurementPurchaseOrders", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_ProcurementGoodsReceipts_Campuses_TenantId_CampusId",
                x => new { x.TenantId, x.CampusId }, "Campuses", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_ProcurementGoodsReceipts_InventoryLocations_TenantId_LocationId",
                x => new { x.TenantId, x.LocationId }, "InventoryLocations", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_ProcurementGoodsReceipts_InventoryMovements_TenantId_PostedMovementId",
                x => new { x.TenantId, x.PostedMovementId }, "InventoryMovements", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateTable("ProcurementGoodsReceiptLines", columns: table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            GoodsReceiptId = table.Column<long>(type: "bigint", nullable: false),
            LineNo = table.Column<int>(type: "int", nullable: false),
            PurchaseOrderLineId = table.Column<long>(type: "bigint", nullable: false),
            InventoryItemId = table.Column<long>(type: "bigint", nullable: false),
            LotOrBatchNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
            Quantity = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
            ApprovedPoQuantity = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
            ApprovedPoUnitPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
            PostedUnitCost = table.Column<decimal?>(type: "decimal(18,4)", nullable: true),
            ValuationReason = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: true),
            CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
            UpdatedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
            CreatedBy = table.Column<long?>(type: "bigint", nullable: true),
            UpdatedBy = table.Column<long?>(type: "bigint", nullable: true),
            IsDeleted = table.Column<bool>(type: "bit", nullable: false),
            DeletedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
            DeletedBy = table.Column<long?>(type: "bigint", nullable: true),
            RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
            TenantId = table.Column<long>(type: "bigint", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_ProcurementGoodsReceiptLines", x => x.Id);
            table.UniqueConstraint("AK_ProcurementGoodsReceiptLines_TenantId_Id", x => new { x.TenantId, x.Id });
            table.CheckConstraint("CK_ProcurementGoodsReceiptLine_QuantityPrice", "[LineNo] > 0 AND [Quantity] > 0 AND [ApprovedPoQuantity] > 0 AND [ApprovedPoUnitPrice] >= 0 AND ([PostedUnitCost] IS NULL OR [PostedUnitCost] > 0)");
            table.ForeignKey("FK_ProcurementGoodsReceiptLines_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_ProcurementGoodsReceiptLines_ProcurementGoodsReceipts_TenantId_GoodsReceiptId",
                x => new { x.TenantId, x.GoodsReceiptId }, "ProcurementGoodsReceipts", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_ProcurementGoodsReceiptLines_ProcurementPurchaseOrderLines_TenantId_PurchaseOrderLineId",
                x => new { x.TenantId, x.PurchaseOrderLineId }, "ProcurementPurchaseOrderLines", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_ProcurementGoodsReceiptLines_InventoryItems_TenantId_InventoryItemId",
                x => new { x.TenantId, x.InventoryItemId }, "InventoryItems", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_ProcurementGoodsReceipts_PublicId", "ProcurementGoodsReceipts", "PublicId", unique: true);
        migrationBuilder.CreateIndex("IX_ProcurementGoodsReceipts_TenantId_ClientRequestId", "ProcurementGoodsReceipts", new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_ProcurementGoodsReceipts_TenantId_PurchaseOrderId_StateCode_CreatedAt", "ProcurementGoodsReceipts", new[] { "TenantId", "PurchaseOrderId", "StateCode", "CreatedAt" });
        migrationBuilder.CreateIndex("IX_ProcurementGoodsReceipts_TenantId_CampusId", "ProcurementGoodsReceipts", new[] { "TenantId", "CampusId" });
        migrationBuilder.CreateIndex("IX_ProcurementGoodsReceipts_TenantId_LocationId", "ProcurementGoodsReceipts", new[] { "TenantId", "LocationId" });
        migrationBuilder.CreateIndex("IX_ProcurementGoodsReceipts_TenantId_PostedMovementId", "ProcurementGoodsReceipts", new[] { "TenantId", "PostedMovementId" }, unique: true, filter: "[PostedMovementId] IS NOT NULL");
        migrationBuilder.CreateIndex("IX_ProcurementGoodsReceiptLines_TenantId_GoodsReceiptId_LineNo", "ProcurementGoodsReceiptLines", new[] { "TenantId", "GoodsReceiptId", "LineNo" }, unique: true);
        migrationBuilder.CreateIndex("IX_ProcurementGoodsReceiptLines_TenantId_GoodsReceiptId_PurchaseOrderLineId_LotOrBatchNo", "ProcurementGoodsReceiptLines", new[] { "TenantId", "GoodsReceiptId", "PurchaseOrderLineId", "LotOrBatchNo" }, unique: true);
        migrationBuilder.CreateIndex("IX_ProcurementGoodsReceiptLines_TenantId_PurchaseOrderLineId", "ProcurementGoodsReceiptLines", new[] { "TenantId", "PurchaseOrderLineId" });
        migrationBuilder.CreateIndex("IX_ProcurementGoodsReceiptLines_TenantId_InventoryItemId", "ProcurementGoodsReceiptLines", new[] { "TenantId", "InventoryItemId" });
        migrationBuilder.CreateIndex("UX_InventoryMovements_ProcurementGoodsReceipt", "InventoryMovements", new[] { "TenantId", "SourceId" }, unique: true,
            filter: "[SourceType] = N'ProcurementGoodsReceipt' AND [SourceId] IS NOT NULL");

        migrationBuilder.Sql(@"CREATE OR ALTER TRIGGER dbo.TR_ProcurementGoodsReceipt_SnapshotImmutable
ON dbo.ProcurementGoodsReceipts AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i INNER JOIN deleted d ON i.Id = d.Id
        WHERE i.TenantId <> d.TenantId OR i.PublicId <> d.PublicId OR i.ClientRequestId <> d.ClientRequestId
            OR i.PurchaseOrderId <> d.PurchaseOrderId OR i.CampusId <> d.CampusId
            OR i.LocationId <> d.LocationId OR i.Reason <> d.Reason OR i.CreateFingerprint <> d.CreateFingerprint)
        THROW 51120, 'Goods receipt source snapshot cannot change.', 1;
END;");
        migrationBuilder.Sql(@"CREATE OR ALTER TRIGGER dbo.TR_ProcurementGoodsReceiptLine_SnapshotImmutable
ON dbo.ProcurementGoodsReceiptLines AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i INNER JOIN deleted d ON i.Id = d.Id
        WHERE i.TenantId <> d.TenantId OR i.GoodsReceiptId <> d.GoodsReceiptId
            OR i.[LineNo] <> d.[LineNo] OR i.PurchaseOrderLineId <> d.PurchaseOrderLineId
            OR i.InventoryItemId <> d.InventoryItemId OR i.LotOrBatchNo <> d.LotOrBatchNo
            OR i.Quantity <> d.Quantity OR i.ApprovedPoQuantity <> d.ApprovedPoQuantity
            OR i.ApprovedPoUnitPrice <> d.ApprovedPoUnitPrice)
        THROW 51121, 'Goods receipt line source snapshot cannot change.', 1;
END;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM dbo.ProcurementGoodsReceipts) OR EXISTS(SELECT 1 FROM dbo.ProcurementGoodsReceiptLines) THROW 51122, 'Goods receipt history requires controlled retention before rollback.', 1;");
        migrationBuilder.DropIndex("UX_InventoryMovements_ProcurementGoodsReceipt", "InventoryMovements");
        migrationBuilder.DropTable("ProcurementGoodsReceiptLines");
        migrationBuilder.DropTable("ProcurementGoodsReceipts");
    }
}
