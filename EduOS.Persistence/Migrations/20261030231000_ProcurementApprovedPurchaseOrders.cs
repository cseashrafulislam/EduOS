using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20261030231000_ProcurementApprovedPurchaseOrders")]
public sealed class ProcurementApprovedPurchaseOrders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProcurementPurchaseOrders",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrderNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                CampusId = table.Column<long>(type: "bigint", nullable: false),
                RequisitionId = table.Column<long>(type: "bigint", nullable: false),
                SupplierId = table.Column<long>(type: "bigint", nullable: false),
                CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                StateCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                ExpectedDeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DecidedByUserId = table.Column<long>(type: "bigint", nullable: true),
                DecisionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProcurementPurchaseOrders", x => x.Id);
                table.UniqueConstraint("AK_ProcurementPurchaseOrders_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_ProcurementPurchaseOrder_State",
                    "[StateCode] IN (N'Draft',N'Submitted',N'Approved',N'Rejected',N'Cancelled')");
                table.CheckConstraint("CK_ProcurementPurchaseOrder_Currency", "[CurrencyCode] = N'BDT'");
                table.ForeignKey("FK_ProcurementPurchaseOrders_Tenants_TenantId",
                    x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ProcurementPurchaseOrders_Campuses_TenantId_CampusId",
                    x => new { x.TenantId, x.CampusId }, "Campuses", new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ProcurementPurchaseOrders_ProcurementRequisitions_TenantId_RequisitionId",
                    x => new { x.TenantId, x.RequisitionId }, "ProcurementRequisitions", new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ProcurementPurchaseOrders_ProcurementSuppliers_TenantId_SupplierId",
                    x => new { x.TenantId, x.SupplierId }, "ProcurementSuppliers", new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ProcurementPurchaseOrders_AspNetUsers_DecidedByUserId",
                    x => x.DecidedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateTable(
            name: "ProcurementPurchaseOrderLines",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                PurchaseOrderId = table.Column<long>(type: "bigint", nullable: false),
                RequisitionLineId = table.Column<long>(type: "bigint", nullable: false),
                LineNo = table.Column<int>(type: "int", nullable: false),
                InventoryItemId = table.Column<long>(type: "bigint", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                UnitPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProcurementPurchaseOrderLines", x => x.Id);
                table.UniqueConstraint("AK_ProcurementPurchaseOrderLines_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_ProcurementPurchaseOrderLine_QtyPrice",
                    "[Quantity] > 0 AND [UnitPrice] >= 0");
                table.ForeignKey("FK_ProcurementPurchaseOrderLines_Tenants_TenantId",
                    x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ProcurementPurchaseOrderLines_ProcurementPurchaseOrders_TenantId_PurchaseOrderId",
                    x => new { x.TenantId, x.PurchaseOrderId }, "ProcurementPurchaseOrders",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ProcurementPurchaseOrderLines_ProcurementRequisitionLines_TenantId_RequisitionLineId",
                    x => new { x.TenantId, x.RequisitionLineId }, "ProcurementRequisitionLines",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ProcurementPurchaseOrderLines_InventoryItems_TenantId_InventoryItemId",
                    x => new { x.TenantId, x.InventoryItemId }, "InventoryItems",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_ProcurementPurchaseOrders_PublicId", "ProcurementPurchaseOrders",
            "PublicId", unique: true);
        migrationBuilder.CreateIndex("IX_ProcurementPurchaseOrders_DecidedByUserId", "ProcurementPurchaseOrders",
            "DecidedByUserId");
        migrationBuilder.CreateIndex("IX_ProcurementPurchaseOrders_TenantId_ClientRequestId", "ProcurementPurchaseOrders",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_ProcurementPurchaseOrders_TenantId_OrderNumber", "ProcurementPurchaseOrders",
            new[] { "TenantId", "OrderNumber" }, unique: true);
        migrationBuilder.CreateIndex("IX_ProcurementPurchaseOrders_TenantId_RequisitionId", "ProcurementPurchaseOrders",
            new[] { "TenantId", "RequisitionId" }, unique: true);
        migrationBuilder.CreateIndex("IX_ProcurementPurchaseOrders_TenantId_CampusId_StateCode_CreatedAt",
            "ProcurementPurchaseOrders", new[] { "TenantId", "CampusId", "StateCode", "CreatedAt" });
        migrationBuilder.CreateIndex("IX_ProcurementPurchaseOrders_TenantId_SupplierId", "ProcurementPurchaseOrders",
            new[] { "TenantId", "SupplierId" });
        migrationBuilder.CreateIndex("IX_ProcurementPurchaseOrderLines_TenantId_PurchaseOrderId_LineNo",
            "ProcurementPurchaseOrderLines", new[] { "TenantId", "PurchaseOrderId", "LineNo" }, unique: true);
        migrationBuilder.CreateIndex("IX_ProcurementPurchaseOrderLines_TenantId_RequisitionLineId",
            "ProcurementPurchaseOrderLines", new[] { "TenantId", "RequisitionLineId" }, unique: true);
        migrationBuilder.CreateIndex("IX_ProcurementPurchaseOrderLines_TenantId_InventoryItemId",
            "ProcurementPurchaseOrderLines", new[] { "TenantId", "InventoryItemId" });
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("ProcurementPurchaseOrderLines");
        migrationBuilder.DropTable("ProcurementPurchaseOrders");
    }
}
