using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20261108140000_ScholarshipCashDisbursement")]
public sealed class ScholarshipCashDisbursement : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.DropForeignKey("FK_ScholarshipApplications_DiscountRules_TenantId_DiscountRuleId", "ScholarshipApplications");
        m.DropIndex("IX_ScholarshipApplications_TenantId_DiscountRuleId", "ScholarshipApplications");
        m.AlterColumn<long>("DiscountRuleId", "ScholarshipApplications", "bigint", nullable: true,
            oldClrType: typeof(long), oldType: "bigint");
        m.AddColumn<string>("AwardMode", "ScholarshipApplications", "nvarchar(12)",
            maxLength: 12, nullable: false, defaultValue: "FeeCredit");
        m.AddColumn<decimal>("CashAmount", "ScholarshipApplications", "decimal(18,2)", nullable: true);
        m.CreateIndex("IX_ScholarshipApplications_TenantId_DiscountRuleId", "ScholarshipApplications",
            new[] { "TenantId", "DiscountRuleId" });
        m.AddForeignKey(name: "FK_ScholarshipApplications_DiscountRules_TenantId_DiscountRuleId",
            table: "ScholarshipApplications", columns: new[] { "TenantId", "DiscountRuleId" },
            principalTable: "DiscountRules", principalColumns: new[] { "TenantId", "Id" },
            onDelete: ReferentialAction.Restrict);
        m.AddCheckConstraint("CK_ScholarshipApplications_Benefit", "ScholarshipApplications",
            "([AwardMode]='FeeCredit' AND [DiscountRuleId] IS NOT NULL AND [CashAmount] IS NULL) OR " +
            "([AwardMode]='CashGrant' AND [DiscountRuleId] IS NULL AND [CashAmount]>0 AND [StudentDiscountId] IS NULL)");

        m.CreateTable("ScholarshipDisbursements", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            PublicId = t.Column<Guid>("uniqueidentifier", nullable: false),
            ClientRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            ReviewRequestId = t.Column<Guid>("uniqueidentifier", nullable: true),
            ScholarshipApplicationId = t.Column<long>("bigint", nullable: false),
            Amount = t.Column<decimal>("decimal(18,2)", nullable: false),
            Method = t.Column<string>("nvarchar(8)", maxLength: 8, nullable: false),
            BeneficiaryType = t.Column<string>("nvarchar(8)", maxLength: 8, nullable: false),
            GuardianId = t.Column<long>("bigint", nullable: true),
            BeneficiaryNameSnapshot = t.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
            ExpenseAccountId = t.Column<long>("bigint", nullable: false),
            PaymentAccountId = t.Column<long>("bigint", nullable: false),
            BankAccountId = t.Column<long>("bigint", nullable: true),
            AccountingPeriodId = t.Column<long>("bigint", nullable: false),
            PaymentDate = t.Column<DateOnly>("date", nullable: false),
            ExternalPaymentReference = t.Column<string>("nvarchar(100)", maxLength: 100, nullable: false),
            State = t.Column<string>("nvarchar(16)", maxLength: 16, nullable: false),
            PreparedByUserId = t.Column<long>("bigint", nullable: false),
            SubmittedAtUtc = t.Column<DateTime>("datetime2", nullable: false),
            ReviewedByUserId = t.Column<long>("bigint", nullable: true),
            ReviewedAtUtc = t.Column<DateTime>("datetime2", nullable: true),
            ReviewReason = t.Column<string>("nvarchar(500)", maxLength: 500, nullable: true),
            JournalId = t.Column<long>("bigint", nullable: true),
            ReversalJournalId = t.Column<long>("bigint", nullable: true),
            ReversalRequestId = t.Column<Guid>("uniqueidentifier", nullable: true),
            ReversalReviewRequestId = t.Column<Guid>("uniqueidentifier", nullable: true),
            ReversalRequestedByUserId = t.Column<long>("bigint", nullable: true),
            ReversalRequestedAtUtc = t.Column<DateTime>("datetime2", nullable: true),
            ReversalAccountingPeriodId = t.Column<long>("bigint", nullable: true),
            ReversalDate = t.Column<DateOnly>("date", nullable: true),
            ExternalReversalReference = t.Column<string>("nvarchar(100)", maxLength: 100, nullable: true),
            ReversalReason = t.Column<string>("nvarchar(500)", maxLength: 500, nullable: true),
            ReversedByUserId = t.Column<long>("bigint", nullable: true),
            ReversedAtUtc = t.Column<DateTime>("datetime2", nullable: true),
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
            t.PrimaryKey("PK_ScholarshipDisbursements", x => x.Id);
            t.UniqueConstraint("AK_ScholarshipDisbursements_TenantId_Id", x => new { x.TenantId, x.Id });
            t.CheckConstraint("CK_ScholarshipDisbursements_State",
                "[State] IN ('Submitted','Posted','Rejected','ReversalRequested','Reversed')");
            t.CheckConstraint("CK_ScholarshipDisbursements_Amount", "[Amount] > 0 AND [Amount] <= 99999999");
            t.CheckConstraint("CK_ScholarshipDisbursements_Method",
                "([Method]='Cash' AND [BankAccountId] IS NULL) OR ([Method]='Bank' AND [BankAccountId] IS NOT NULL)");
            t.CheckConstraint("CK_ScholarshipDisbursements_Beneficiary",
                "([BeneficiaryType]='Student' AND [GuardianId] IS NULL) OR " +
                "([BeneficiaryType]='Guardian' AND [GuardianId] IS NOT NULL)");
            t.ForeignKey("FK_ScholarshipDisbursements_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipDisbursements_ScholarshipApplications", x =>
                new { x.TenantId, x.ScholarshipApplicationId }, "ScholarshipApplications",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipDisbursements_Guardians", x =>
                new { x.TenantId, x.GuardianId }, "Guardians",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipDisbursements_ExpenseAccounts", x =>
                new { x.TenantId, x.ExpenseAccountId }, "Accounts",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipDisbursements_PaymentAccounts", x =>
                new { x.TenantId, x.PaymentAccountId }, "Accounts",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipDisbursements_BankAccounts", x =>
                new { x.TenantId, x.BankAccountId }, "BankAccounts",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipDisbursements_AccountingPeriods", x =>
                new { x.TenantId, x.AccountingPeriodId }, "AccountingPeriods",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipDisbursements_ReversalAccountingPeriods", x =>
                new { x.TenantId, x.ReversalAccountingPeriodId }, "AccountingPeriods",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipDisbursements_Journals", x =>
                new { x.TenantId, x.JournalId }, "Journals",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipDisbursements_ReversalJournals", x =>
                new { x.TenantId, x.ReversalJournalId }, "Journals",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipDisbursements_PreparedBy", x => x.PreparedByUserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipDisbursements_ReviewedBy", x => x.ReviewedByUserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipDisbursements_ReversalRequestedBy", x => x.ReversalRequestedByUserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipDisbursements_ReversedBy", x => x.ReversedByUserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_ScholarshipDisbursements_PublicId", "ScholarshipDisbursements", "PublicId", unique: true);
        m.CreateIndex("IX_ScholarshipDisbursements_TenantId_ClientRequestId", "ScholarshipDisbursements",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("IX_ScholarshipDisbursements_TenantId_ReviewRequestId", "ScholarshipDisbursements",
            new[] { "TenantId", "ReviewRequestId" }, unique: true,
            filter: "[ReviewRequestId] IS NOT NULL");
        m.CreateIndex("IX_ScholarshipDisbursements_TenantId_ScholarshipApplicationId", "ScholarshipDisbursements",
            new[] { "TenantId", "ScholarshipApplicationId" }, unique: true,
            filter: "[State] <> N'Rejected' AND [IsDeleted] = 0");
        m.CreateIndex("IX_ScholarshipDisbursements_TenantId_Method_PaymentAccountId_ExternalPaymentReference",
            "ScholarshipDisbursements", new[] { "TenantId", "Method", "PaymentAccountId", "ExternalPaymentReference" }, unique: true);
        foreach (var column in new[] { "GuardianId", "ExpenseAccountId", "PaymentAccountId", "BankAccountId",
            "AccountingPeriodId", "ReversalAccountingPeriodId", "JournalId", "ReversalJournalId" })
            m.CreateIndex("IX_ScholarshipDisbursements_TenantId_" + column,
                "ScholarshipDisbursements", new[] { "TenantId", column });
        foreach (var column in new[] { "PreparedByUserId", "ReviewedByUserId", "ReversalRequestedByUserId", "ReversedByUserId" })
            m.CreateIndex("IX_ScholarshipDisbursements_" + column, "ScholarshipDisbursements", column);
    }

    protected override void Down(MigrationBuilder m)
    {
        // A disbursed cash grant cannot be represented by the original fee-only schema.
        m.Sql("IF EXISTS (SELECT 1 FROM dbo.ScholarshipApplications WHERE AwardMode = 'CashGrant') " +
            "THROW 51120, 'Reverse cash scholarships before rolling back schema.', 1;");
        m.DropTable("ScholarshipDisbursements");
        m.DropCheckConstraint("CK_ScholarshipApplications_Benefit", "ScholarshipApplications");
        m.DropForeignKey("FK_ScholarshipApplications_DiscountRules_TenantId_DiscountRuleId", "ScholarshipApplications");
        m.DropIndex("IX_ScholarshipApplications_TenantId_DiscountRuleId", "ScholarshipApplications");
        m.DropColumn("AwardMode", "ScholarshipApplications");
        m.DropColumn("CashAmount", "ScholarshipApplications");
        m.AlterColumn<long>("DiscountRuleId", "ScholarshipApplications", "bigint", nullable: false,
            oldClrType: typeof(long), oldType: "bigint", oldNullable: true);
        m.CreateIndex("IX_ScholarshipApplications_TenantId_DiscountRuleId", "ScholarshipApplications",
            new[] { "TenantId", "DiscountRuleId" });
        m.AddForeignKey(name: "FK_ScholarshipApplications_DiscountRules_TenantId_DiscountRuleId",
            table: "ScholarshipApplications", columns: new[] { "TenantId", "DiscountRuleId" },
            principalTable: "DiscountRules", principalColumns: new[] { "TenantId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }
}
