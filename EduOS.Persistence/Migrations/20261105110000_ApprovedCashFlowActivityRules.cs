using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Append-only approved counterparty classification; never alters posted cash/GL data.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261105110000_ApprovedCashFlowActivityRules")]
public sealed class ApprovedCashFlowActivityRules : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("CashFlowActivityRules", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            AccountId = t.Column<long>("bigint", nullable: false),
            EffectiveFromDate = t.Column<DateOnly>("date", nullable: false),
            ActivityCode = t.Column<string>("nvarchar(20)", maxLength: 20, nullable: false),
            StateCode = t.Column<string>("nvarchar(20)", maxLength: 20, nullable: false),
            ClientRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            Reason = t.Column<string>("nvarchar(500)", maxLength: 500, nullable: false),
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
            t.PrimaryKey("PK_CashFlowActivityRules", x => x.Id);
            t.UniqueConstraint("AK_CashFlowActivityRules_TenantId_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_CashFlowActivityRules_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CashFlowActivityRules_Accounts_TenantId_AccountId", x =>
                new { x.TenantId, x.AccountId }, "Accounts", new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CashFlowActivityRules_AspNetUsers_SubmittedByUserId", x => x.SubmittedByUserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CashFlowActivityRules_AspNetUsers_ReviewedByUserId", x => x.ReviewedByUserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_CashFlowActivityRule_Approval",
                "[ActivityCode] IN (N'Operating',N'Investing',N'Financing') AND " +
                "[StateCode] IN (N'Submitted',N'Approved',N'Rejected') AND " +
                "(([StateCode] = N'Submitted' AND [ReviewedByUserId] IS NULL AND [ReviewedAtUtc] IS NULL AND [ReviewRequestId] IS NULL) OR " +
                "([StateCode] <> N'Submitted' AND [ReviewedByUserId] IS NOT NULL AND [ReviewedAtUtc] IS NOT NULL AND [ReviewRequestId] IS NOT NULL AND [ReviewedByUserId] <> [SubmittedByUserId]))");
        });
        m.CreateIndex("IX_CashFlowActivityRules_TenantId_AccountId_EffectiveFromDate", "CashFlowActivityRules",
            new[] { "TenantId", "AccountId", "EffectiveFromDate" }, unique: true,
            filter: "[StateCode] IN (N'Submitted',N'Approved') AND [IsDeleted] = 0");
        m.CreateIndex("IX_CashFlowActivityRules_TenantId_ClientRequestId", "CashFlowActivityRules",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("IX_CashFlowActivityRules_TenantId_ReviewRequestId", "CashFlowActivityRules",
            new[] { "TenantId", "ReviewRequestId" }, unique: true, filter: "[ReviewRequestId] IS NOT NULL");
        m.CreateIndex("IX_CashFlowActivityRules_SubmittedByUserId", "CashFlowActivityRules", "SubmittedByUserId");
        m.CreateIndex("IX_CashFlowActivityRules_ReviewedByUserId", "CashFlowActivityRules", "ReviewedByUserId");
    }

    protected override void Down(MigrationBuilder m) => m.DropTable("CashFlowActivityRules");
}
