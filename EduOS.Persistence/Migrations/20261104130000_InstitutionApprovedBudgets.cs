using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Additive institution-wide finance planning only. Existing journals are untouched.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261104130000_InstitutionApprovedBudgets")]
public sealed class InstitutionApprovedBudgets : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("InstitutionBudgets", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            PublicId = t.Column<Guid>("uniqueidentifier", nullable: false),
            ClientRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            Name = t.Column<string>("nvarchar(150)", maxLength: 150, nullable: false),
            StartDate = t.Column<DateOnly>("date", nullable: false),
            EndDate = t.Column<DateOnly>("date", nullable: false),
            StateCode = t.Column<string>("nvarchar(20)", maxLength: 20, nullable: false),
            SubmittedByUserId = t.Column<long>("bigint", nullable: false),
            SubmittedAtUtc = t.Column<DateTime>("datetime2", nullable: false),
            ReviewedByUserId = t.Column<long>("bigint", nullable: true),
            ReviewedAtUtc = t.Column<DateTime>("datetime2", nullable: true),
            ReviewRequestId = t.Column<Guid>("uniqueidentifier", nullable: true),
            ReviewReason = t.Column<string>("nvarchar(800)", maxLength: 800, nullable: true),
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
            t.PrimaryKey("PK_InstitutionBudgets", x => x.Id);
            t.UniqueConstraint("AK_InstitutionBudgets_TenantId_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_InstitutionBudgets_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_InstitutionBudgets_AspNetUsers_SubmittedByUserId", x => x.SubmittedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_InstitutionBudgets_AspNetUsers_ReviewedByUserId", x => x.ReviewedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_InstitutionBudget_Valid", "[EndDate] >= [StartDate] AND [StateCode] IN (N'Submitted',N'Approved',N'Rejected') AND (([StateCode] = N'Submitted' AND [ReviewedByUserId] IS NULL AND [ReviewedAtUtc] IS NULL) OR ([StateCode] <> N'Submitted' AND [ReviewedByUserId] IS NOT NULL AND [ReviewedAtUtc] IS NOT NULL)) AND ([ReviewedByUserId] IS NULL OR [ReviewedByUserId] <> [SubmittedByUserId])");
        });
        m.CreateTable("InstitutionBudgetLines", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            InstitutionBudgetId = t.Column<long>("bigint", nullable: false),
            AccountId = t.Column<long>("bigint", nullable: false),
            AccountCodeSnapshot = t.Column<string>("nvarchar(50)", maxLength: 50, nullable: false),
            AccountNameSnapshot = t.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
            AccountTypeSnapshot = t.Column<int>("int", nullable: false),
            PlannedAmountBdt = t.Column<decimal>("decimal(18,2)", nullable: false),
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
            t.PrimaryKey("PK_InstitutionBudgetLines", x => x.Id);
            t.UniqueConstraint("AK_InstitutionBudgetLines_TenantId_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_InstitutionBudgetLines_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_InstitutionBudgetLines_InstitutionBudgets_TenantId_InstitutionBudgetId",
                x => new { x.TenantId, x.InstitutionBudgetId }, "InstitutionBudgets", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_InstitutionBudgetLines_Accounts_TenantId_AccountId",
                x => new { x.TenantId, x.AccountId }, "Accounts", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_InstitutionBudgetLine_Valid", "[PlannedAmountBdt] > 0 AND [AccountTypeSnapshot] IN (4,5)");
        });
        m.CreateIndex("IX_InstitutionBudgets_PublicId", "InstitutionBudgets", "PublicId", unique: true);
        m.CreateIndex("IX_InstitutionBudgets_TenantId_ClientRequestId", "InstitutionBudgets",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("IX_InstitutionBudgets_TenantId_ReviewRequestId", "InstitutionBudgets",
            new[] { "TenantId", "ReviewRequestId" }, unique: true, filter: "[ReviewRequestId] IS NOT NULL");
        m.CreateIndex("IX_InstitutionBudgets_TenantId_StartDate_EndDate_StateCode", "InstitutionBudgets",
            new[] { "TenantId", "StartDate", "EndDate", "StateCode" });
        m.CreateIndex("IX_InstitutionBudgets_SubmittedByUserId", "InstitutionBudgets", "SubmittedByUserId");
        m.CreateIndex("IX_InstitutionBudgets_ReviewedByUserId", "InstitutionBudgets", "ReviewedByUserId");
        m.CreateIndex("IX_InstitutionBudgetLines_TenantId_InstitutionBudgetId_AccountId", "InstitutionBudgetLines",
            new[] { "TenantId", "InstitutionBudgetId", "AccountId" }, unique: true);
        m.CreateIndex("IX_InstitutionBudgetLines_TenantId_AccountId", "InstitutionBudgetLines",
            new[] { "TenantId", "AccountId" });
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("InstitutionBudgetLines");
        m.DropTable("InstitutionBudgets");
    }
}
