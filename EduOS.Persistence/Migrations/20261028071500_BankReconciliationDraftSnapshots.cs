using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// Additive, empty-on-upgrade statement intake. No posted journal or bank-book rows are modified.
/// Rollback requires an approved export/backup of draft evidence before dropping these new tables.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261028071500_BankReconciliationDraftSnapshots")]
public sealed class BankReconciliationDraftSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BankReconciliationBatches",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                BankAccountId = table.Column<long>(type: "bigint", nullable: false),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StatementIdentifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                SourceFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                OpeningBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                ClosingBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                MovementCount = table.Column<int>(type: "int", nullable: false),
                StateCode = table.Column<int>(type: "int", nullable: false),
                PreparedByUserId = table.Column<long>(type: "bigint", nullable: false),
                PreparedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                table.PrimaryKey("PK_BankReconciliationBatches", x => x.Id);
                table.UniqueConstraint("AK_BankReconciliationBatches_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey("FK_BankReconciliationBatches_Tenants_TenantId", x => x.TenantId,
                    "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_BankReconciliationBatches_BankAccounts_TenantId_BankAccountId",
                    x => new { x.TenantId, x.BankAccountId }, "BankAccounts",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_BankReconciliationBatches_AspNetUsers_PreparedByUserId",
                    x => x.PreparedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_BankReconciliationBatch_Valid",
                    "[ToDate] >= [FromDate] AND [MovementCount] BETWEEN 1 AND 500 AND [StateCode] = 1");
            });
        migrationBuilder.CreateIndex("IX_BankReconciliationBatches_PreparedByUserId",
            "BankReconciliationBatches", "PreparedByUserId");
        migrationBuilder.CreateIndex("UX_BankReconciliationBatches_Tenant_Request",
            "BankReconciliationBatches", new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("UX_BankReconciliationBatches_Tenant_Bank_Fingerprint",
            "BankReconciliationBatches", new[] { "TenantId", "BankAccountId", "SourceFingerprint" }, unique: true);
        migrationBuilder.CreateIndex("IX_BankReconciliationBatches_Tenant_Bank_Prepared",
            "BankReconciliationBatches", new[] { "TenantId", "BankAccountId", "PreparedAt", "Id" });

        migrationBuilder.CreateTable(
            name: "BankReconciliationStatementLines",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                BankReconciliationBatchId = table.Column<long>(type: "bigint", nullable: false),
                RowNumber = table.Column<int>(type: "int", nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                Reference = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                Deposit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Withdrawal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
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
                table.PrimaryKey("PK_BankReconciliationStatementLines", x => x.Id);
                table.UniqueConstraint("AK_BankReconciliationStatementLines_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey("FK_BankReconciliationStatementLines_Tenants_TenantId", x => x.TenantId,
                    "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_BankReconciliationStatementLines_BankReconciliationBatches_TenantId_BankReconciliationBatchId",
                    x => new { x.TenantId, x.BankReconciliationBatchId }, "BankReconciliationBatches",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_BankReconciliationStatementLine_Valid",
                    "[RowNumber] BETWEEN 1 AND 500 AND (([Deposit] > 0 AND [Withdrawal] = 0) OR ([Withdrawal] > 0 AND [Deposit] = 0))");
            });
        migrationBuilder.CreateIndex("UX_BankReconciliationStatementLines_Tenant_Batch_Row",
            "BankReconciliationStatementLines", new[] { "TenantId", "BankReconciliationBatchId", "RowNumber" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Never discard real statement evidence on an accidental rollback.
        migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [dbo].[BankReconciliationBatches])
            THROW 51000, 'Bank reconciliation drafts exist; export and approve a retention plan before rollback.', 1;");
        migrationBuilder.DropTable("BankReconciliationStatementLines");
        migrationBuilder.DropTable("BankReconciliationBatches");
    }
}
