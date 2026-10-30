using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20261030195000_ProcurementRequisitionMakerChecker")]
public sealed class ProcurementRequisitionMakerChecker : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProcurementRequisitions",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RequestNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                CampusId = table.Column<long>(type: "bigint", nullable: false),
                Purpose = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                StateCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
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
                table.PrimaryKey("PK_ProcurementRequisitions", x => x.Id);
                table.UniqueConstraint("AK_ProcurementRequisitions_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_ProcurementRequisition_State",
                    "[StateCode] IN (N'Draft',N'Submitted',N'Approved',N'Rejected',N'Cancelled')");
                table.ForeignKey("FK_ProcurementRequisitions_Tenants_TenantId",
                    x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ProcurementRequisitions_Campuses_TenantId_CampusId",
                    x => new { x.TenantId, x.CampusId }, "Campuses",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ProcurementRequisitions_AspNetUsers_DecidedByUserId",
                    x => x.DecidedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateTable(
            name: "ProcurementRequisitionLines",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                RequisitionId = table.Column<long>(type: "bigint", nullable: false),
                LineNo = table.Column<int>(type: "int", nullable: false),
                InventoryItemId = table.Column<long>(type: "bigint", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                EstimatedUnitPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                Remarks = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
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
                table.PrimaryKey("PK_ProcurementRequisitionLines", x => x.Id);
                table.UniqueConstraint("AK_ProcurementRequisitionLines_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_ProcurementRequisitionLine_QtyPrice",
                    "[Quantity] > 0 AND [EstimatedUnitPrice] >= 0");
                table.ForeignKey("FK_ProcurementRequisitionLines_Tenants_TenantId",
                    x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ProcurementRequisitionLines_ProcurementRequisitions_TenantId_RequisitionId",
                    x => new { x.TenantId, x.RequisitionId }, "ProcurementRequisitions",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_ProcurementRequisitionLines_InventoryItems_TenantId_InventoryItemId",
                    x => new { x.TenantId, x.InventoryItemId }, "InventoryItems",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_ProcurementRequisitions_DecidedByUserId", "ProcurementRequisitions", "DecidedByUserId");
        migrationBuilder.CreateIndex("IX_ProcurementRequisitions_TenantId_CampusId_StateCode_CreatedAt",
            "ProcurementRequisitions", new[] { "TenantId", "CampusId", "StateCode", "CreatedAt" });
        migrationBuilder.CreateIndex("IX_ProcurementRequisitions_TenantId_ClientRequestId",
            "ProcurementRequisitions", new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_ProcurementRequisitions_TenantId_RequestNumber",
            "ProcurementRequisitions", new[] { "TenantId", "RequestNumber" }, unique: true);
        migrationBuilder.CreateIndex("IX_ProcurementRequisitions_PublicId", "ProcurementRequisitions", "PublicId", unique: true);
        migrationBuilder.CreateIndex("IX_ProcurementRequisitionLines_TenantId_InventoryItemId",
            "ProcurementRequisitionLines", new[] { "TenantId", "InventoryItemId" });
        migrationBuilder.CreateIndex("IX_ProcurementRequisitionLines_TenantId_RequisitionId_LineNo",
            "ProcurementRequisitionLines", new[] { "TenantId", "RequisitionId", "LineNo" }, unique: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("ProcurementRequisitionLines");
        migrationBuilder.DropTable("ProcurementRequisitions");
    }
}
