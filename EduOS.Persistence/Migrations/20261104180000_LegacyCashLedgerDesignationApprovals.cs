using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Append-only historical cash designation proposals; the original GL and journals are untouched.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261104180000_LegacyCashLedgerDesignationApprovals")]
public sealed class LegacyCashLedgerDesignationApprovals : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("CashLedgerDesignationRequests", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            AccountId = t.Column<long>("bigint", nullable: false),
            ClientRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            Reason = t.Column<string>("nvarchar(500)", maxLength: 500, nullable: false),
            StateCode = t.Column<string>("nvarchar(20)", maxLength: 20, nullable: false),
            SubmittedByUserId = t.Column<long>("bigint", nullable: false),
            SubmittedAtUtc = t.Column<DateTime>("datetime2", nullable: false),
            ReviewedByUserId = t.Column<long>("bigint", nullable: true),
            ReviewedAtUtc = t.Column<DateTime>("datetime2", nullable: true),
            ReviewRequestId = t.Column<Guid>("uniqueidentifier", nullable: true),
            ReviewReason = t.Column<string>("nvarchar(500)", maxLength: 500, nullable: true),
            CreatedAt = t.Column<DateTime>("datetime2", nullable: false),
            CreatedBy = t.Column<long>("bigint", nullable: true),
            UpdatedAt = t.Column<DateTime>("datetime2", nullable: true),
            UpdatedBy = t.Column<long>("bigint", nullable: true),
            IsDeleted = t.Column<bool>("bit", nullable: false),
            DeletedAt = t.Column<DateTime>("datetime2", nullable: true),
            DeletedBy = t.Column<long>("bigint", nullable: true),
            RowVersion = t.Column<byte[]>("rowversion", rowVersion: true, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_CashLedgerDesignationRequests", x => x.Id);
            t.UniqueConstraint("AK_CashLedgerDesignationRequests_TenantId_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_CashLedgerDesignationRequests_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CashLedgerDesignationRequests_Accounts_TenantId_AccountId", x =>
                new { x.TenantId, x.AccountId }, "Accounts", new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CashLedgerDesignationRequests_AspNetUsers_SubmittedByUserId", x => x.SubmittedByUserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CashLedgerDesignationRequests_AspNetUsers_ReviewedByUserId", x => x.ReviewedByUserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_CashLedgerDesignation_Workflow",
                "[StateCode] IN (N'Submitted',N'Approved',N'Rejected') AND " +
                "(([StateCode] = N'Submitted' AND [ReviewedByUserId] IS NULL AND [ReviewedAtUtc] IS NULL AND [ReviewRequestId] IS NULL) OR " +
                "([StateCode] <> N'Submitted' AND [ReviewedByUserId] IS NOT NULL AND [ReviewedAtUtc] IS NOT NULL AND [ReviewRequestId] IS NOT NULL AND [ReviewedByUserId] <> [SubmittedByUserId]))");
        });
        m.CreateIndex("IX_CashLedgerDesignationRequests_TenantId_AccountId", "CashLedgerDesignationRequests",
            new[] { "TenantId", "AccountId" }, unique: true,
            filter: "[StateCode] = N'Submitted' AND [IsDeleted] = 0");
        m.CreateIndex("IX_CashLedgerDesignationRequests_TenantId_ClientRequestId", "CashLedgerDesignationRequests",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("IX_CashLedgerDesignationRequests_TenantId_ReviewRequestId", "CashLedgerDesignationRequests",
            new[] { "TenantId", "ReviewRequestId" }, unique: true, filter: "[ReviewRequestId] IS NOT NULL");
        m.CreateIndex("IX_CashLedgerDesignationRequests_SubmittedByUserId", "CashLedgerDesignationRequests", "SubmittedByUserId");
        m.CreateIndex("IX_CashLedgerDesignationRequests_ReviewedByUserId", "CashLedgerDesignationRequests", "ReviewedByUserId");
    }

    protected override void Down(MigrationBuilder m) => m.DropTable("CashLedgerDesignationRequests");
}
