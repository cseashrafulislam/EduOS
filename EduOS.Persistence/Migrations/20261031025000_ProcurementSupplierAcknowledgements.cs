using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20261031025000_ProcurementSupplierAcknowledgements")]
public sealed class ProcurementSupplierAcknowledgements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProcurementSupplierAcknowledgements",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CampusId = table.Column<long>(type: "bigint", nullable: false),
                PurchaseOrderId = table.Column<long>(type: "bigint", nullable: false),
                SupplierId = table.Column<long>(type: "bigint", nullable: false),
                SupplierReference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                StateCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                ResponseDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                PromisedDeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                table.PrimaryKey("PK_ProcurementSupplierAcknowledgements", x => x.Id);
                table.UniqueConstraint("AK_ProcurementSupplierAcknowledgements_TenantId_Id",
                    x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_ProcurementSupplierAcknowledgement_State",
                    "[StateCode] IN (N'Draft',N'Submitted',N'Confirmed',N'Rejected')");
                table.CheckConstraint("CK_ProcurementSupplierAcknowledgement_Dates",
                    "[PromisedDeliveryDate] >= [ResponseDate]");
                table.ForeignKey("FK_ProcurementSupplierAcknowledgements_Tenants_TenantId",
                    x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ProcurementSupplierAcknowledgements_Campuses_TenantId_CampusId",
                    x => new { x.TenantId, x.CampusId }, "Campuses", new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ProcurementSupplierAcknowledgements_ProcurementPurchaseOrders_TenantId_PurchaseOrderId",
                    x => new { x.TenantId, x.PurchaseOrderId }, "ProcurementPurchaseOrders",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ProcurementSupplierAcknowledgements_ProcurementSuppliers_TenantId_SupplierId",
                    x => new { x.TenantId, x.SupplierId }, "ProcurementSuppliers",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ProcurementSupplierAcknowledgements_AspNetUsers_DecidedByUserId",
                    x => x.DecidedByUserId, "AspNetUsers", "Id",
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_ProcurementSupplierAcknowledgements_TenantId_ClientRequestId",
            "ProcurementSupplierAcknowledgements", new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_ProcurementSupplierAcknowledgements_TenantId_PurchaseOrderId",
            "ProcurementSupplierAcknowledgements", new[] { "TenantId", "PurchaseOrderId" }, unique: true);
        migrationBuilder.CreateIndex("IX_ProcurementSupplierAcknowledgements_TenantId_SupplierId",
            "ProcurementSupplierAcknowledgements", new[] { "TenantId", "SupplierId" });
        migrationBuilder.CreateIndex("IX_ProcurementSupplierAcknowledgements_TenantId_CampusId_StateCode_CreatedAt",
            "ProcurementSupplierAcknowledgements",
            new[] { "TenantId", "CampusId", "StateCode", "CreatedAt" });
        migrationBuilder.CreateIndex("IX_ProcurementSupplierAcknowledgements_DecidedByUserId",
            "ProcurementSupplierAcknowledgements", "DecidedByUserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Preserve accepted supplier evidence; never silently erase historical confirmations.
        migrationBuilder.Sql(
            "IF EXISTS (SELECT 1 FROM [dbo].[ProcurementSupplierAcknowledgements]) " +
            "THROW 51021, 'Supplier confirmations exist; rollback requires a documented data retention plan.', 1;");
        migrationBuilder.DropTable("ProcurementSupplierAcknowledgements");
    }
}
