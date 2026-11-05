using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Additive manual cash-flow classification only; does not touch posted GL.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261105160000_CashFlowJournalAllocationReview")]
public sealed class CashFlowJournalAllocationReview : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("CashFlowJournalAllocations", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            JournalId = t.Column<long>("bigint", nullable: false),
            JournalFingerprint = t.Column<string>("nvarchar(64)", maxLength: 64, nullable: false),
            Direction = t.Column<string>("nvarchar(10)", maxLength: 10, nullable: false),
            OperatingAmount = t.Column<decimal>("decimal(18,2)", nullable: false),
            InvestingAmount = t.Column<decimal>("decimal(18,2)", nullable: false),
            FinancingAmount = t.Column<decimal>("decimal(18,2)", nullable: false),
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
            t.PrimaryKey("PK_CashFlowJournalAllocations", x => x.Id);
            t.UniqueConstraint("AK_CashFlowJournalAllocations_TenantId_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_CashFlowJournalAllocations_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CashFlowJournalAllocations_Journals_TenantId_JournalId", x =>
                new { x.TenantId, x.JournalId }, "Journals", new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CashFlowJournalAllocations_AspNetUsers_SubmittedByUserId", x => x.SubmittedByUserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CashFlowJournalAllocations_AspNetUsers_ReviewedByUserId", x => x.ReviewedByUserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_CashFlowJournalAllocation_Approval",
                "[Direction] IN (N'Inflow',N'Outflow') AND [StateCode] IN (N'Submitted',N'Approved',N'Rejected') AND " +
                "[OperatingAmount] >= 0 AND [InvestingAmount] >= 0 AND [FinancingAmount] >= 0 AND " +
                "([OperatingAmount]+[InvestingAmount]+[FinancingAmount]) > 0 AND " +
                "(([StateCode] = N'Submitted' AND [ReviewedByUserId] IS NULL AND [ReviewedAtUtc] IS NULL AND [ReviewRequestId] IS NULL) OR " +
                "([StateCode] <> N'Submitted' AND [ReviewedByUserId] IS NOT NULL AND [ReviewedAtUtc] IS NOT NULL AND [ReviewRequestId] IS NOT NULL AND [ReviewedByUserId] <> [SubmittedByUserId]))");
        });
        m.CreateIndex("IX_CashFlowJournalAllocations_TenantId_JournalId", "CashFlowJournalAllocations",
            new[] { "TenantId", "JournalId" }, unique: true,
            filter: "[StateCode] IN (N'Submitted',N'Approved') AND [IsDeleted] = 0");
        m.CreateIndex("IX_CashFlowJournalAllocations_TenantId_ClientRequestId", "CashFlowJournalAllocations",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("IX_CashFlowJournalAllocations_TenantId_ReviewRequestId", "CashFlowJournalAllocations",
            new[] { "TenantId", "ReviewRequestId" }, unique: true, filter: "[ReviewRequestId] IS NOT NULL");
        m.CreateIndex("IX_CashFlowJournalAllocations_SubmittedByUserId", "CashFlowJournalAllocations", "SubmittedByUserId");
        m.CreateIndex("IX_CashFlowJournalAllocations_ReviewedByUserId", "CashFlowJournalAllocations", "ReviewedByUserId");
    }

    protected override void Down(MigrationBuilder m) => m.DropTable("CashFlowJournalAllocations");
}
