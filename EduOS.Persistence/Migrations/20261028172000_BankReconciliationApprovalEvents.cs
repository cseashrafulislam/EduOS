using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

// Additive append-only workflow. No alteration of historical statement or posted GL data.
[DbContext(typeof(EduOSDbContext))]
[Migration("20261028172000_BankReconciliationApprovalEvents")]
public sealed class BankReconciliationApprovalEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BankReconciliationApprovalEvents",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BankReconciliationBatchId = table.Column<long>(type: "bigint", nullable: false),
                ActionCode = table.Column<int>(type: "int", nullable: false),
                EvidenceFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                MatchedCount = table.Column<int>(type: "int", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                ActorUserId = table.Column<long>(type: "bigint", nullable: false),
                EventAt = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                table.PrimaryKey("PK_BankReconciliationApprovalEvents", x => x.Id);
                table.UniqueConstraint("AK_BankReconciliationApprovalEvents_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey("FK_BankReconciliationApprovalEvents_Tenants_TenantId",
                    x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_BankReconciliationApprovalEvents_Batches_Tenant_Batch",
                    x => new { x.TenantId, x.BankReconciliationBatchId }, "BankReconciliationBatches",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_BankReconciliationApprovalEvents_Users_Actor",
                    x => x.ActorUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_BankReconciliationApprovalEvent_Valid",
                    "[ActionCode] BETWEEN 2 AND 5 AND [MatchedCount] BETWEEN 1 AND 500 AND " +
                    "LEN([EvidenceFingerprint]) = 64 AND " +
                    "([ActionCode] IN (2,3) OR LEN([Reason]) > 0)");
            });
        migrationBuilder.CreateIndex("UX_BankReconciliationApprovalEvents_Tenant_Request",
            "BankReconciliationApprovalEvents", new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("UX_BankReconciliationApprovalEvents_Tenant_Batch_Action",
            "BankReconciliationApprovalEvents", new[] { "TenantId", "BankReconciliationBatchId", "ActionCode" }, unique: true);
        migrationBuilder.CreateIndex("IX_BankReconciliationApprovalEvents_ActorUserId",
            "BankReconciliationApprovalEvents", "ActorUserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [dbo].[BankReconciliationApprovalEvents])
            THROW 51002, 'Clearance evidence exists; rollback requires approved retention/export.', 1;");
        migrationBuilder.DropTable("BankReconciliationApprovalEvents");
    }
}
