using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

// Additive empty-on-upgrade match proposal history; does not change existing statements or journals.
[DbContext(typeof(EduOSDbContext))]
[Migration("20261028114500_BankReconciliationMatchEvents")]
public sealed class BankReconciliationMatchEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BankReconciliationMatchEvents",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BankReconciliationBatchId = table.Column<long>(type: "bigint", nullable: false),
                ActionCode = table.Column<int>(type: "int", nullable: false),
                OriginalMatchEventId = table.Column<long>(type: "bigint", nullable: true),
                StatementRowNumber = table.Column<int>(type: "int", nullable: false),
                JournalLineId = table.Column<long>(type: "bigint", nullable: false),
                JournalLineVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                Reference = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                Deposit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Withdrawal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
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
                table.PrimaryKey("PK_BankReconciliationMatchEvents", x => x.Id);
                table.UniqueConstraint("AK_BankReconciliationMatchEvents_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey("FK_BankReconciliationMatchEvents_Tenants_TenantId", x => x.TenantId,
                    "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_BankReconciliationMatchEvents_Batches_Tenant_Batch",
                    x => new { x.TenantId, x.BankReconciliationBatchId }, "BankReconciliationBatches",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_BankReconciliationMatchEvents_JournalLines_Tenant_Line",
                    x => new { x.TenantId, x.JournalLineId }, "JournalLines",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_BankReconciliationMatchEvents_Users_Actor",
                    x => x.ActorUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_BankReconciliationMatchEvents_Original_Tenant_Id",
                    x => new { x.TenantId, x.OriginalMatchEventId }, "BankReconciliationMatchEvents",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_BankReconciliationMatchEvent_Valid",
                    "[StatementRowNumber] BETWEEN 1 AND 500 AND [ActionCode] IN (1,2) AND " +
                    "(([ActionCode] = 1 AND [OriginalMatchEventId] IS NULL AND [Reason] IS NULL) OR " +
                    "([ActionCode] = 2 AND [OriginalMatchEventId] IS NOT NULL AND LEN([Reason]) > 0)) AND " +
                    "(([Deposit] > 0 AND [Withdrawal] = 0) OR ([Withdrawal] > 0 AND [Deposit] = 0))");
            });
        migrationBuilder.CreateIndex("UX_BankReconciliationMatchEvents_Tenant_Request",
            "BankReconciliationMatchEvents", new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("UX_BankReconciliationMatchEvents_Tenant_Withdrawal",
            "BankReconciliationMatchEvents", new[] { "TenantId", "OriginalMatchEventId" },
            unique: true, filter: "[OriginalMatchEventId] IS NOT NULL");
        migrationBuilder.CreateIndex("IX_BankReconciliationMatchEvents_Tenant_Batch_Row",
            "BankReconciliationMatchEvents",
            new[] { "TenantId", "BankReconciliationBatchId", "StatementRowNumber", "ActionCode" });
        migrationBuilder.CreateIndex("IX_BankReconciliationMatchEvents_Tenant_Line_Action",
            "BankReconciliationMatchEvents", new[] { "TenantId", "JournalLineId", "ActionCode" });
        migrationBuilder.CreateIndex("IX_BankReconciliationMatchEvents_ActorUserId",
            "BankReconciliationMatchEvents", "ActorUserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [dbo].[BankReconciliationMatchEvents])
            THROW 51001, 'Match decision evidence exists; retain and export before rollback.', 1;");
        migrationBuilder.DropTable("BankReconciliationMatchEvents");
    }
}
