using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

// SQL-owned additive extension: immutable academic outcome versions and auditable Finance links.
[DbContext(typeof(EduOSDbContext))]
[Migration("20261103190000_ResearchQualityExtensions")]
public sealed class ResearchQualityExtensions : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<bool>("RequiresOutcomeMapping", "AccreditationStandards", type: "bit",
            nullable: false, defaultValue: false);
        m.AddColumn<bool>("RequiresPublishedAcademicEvidence", "AccreditationStandards", type: "bit",
            nullable: false, defaultValue: false);
        m.CreateIndex("IX_AccreditationAssessments_Compliance", "AccreditationAssessments",
            new[] { "TenantId", "CampusId", "AcademicProgramId", "StandardId", "StateCode", "ApprovedAtUtc" });
        m.CreateTable("AccreditationProgramOutcomes", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            AcademicProgramId = t.Column<long>("bigint", nullable: false),
            Code = t.Column<string>("nvarchar(40)", maxLength: 40, nullable: false),
            VersionNo = t.Column<int>("int", nullable: false),
            Statement = t.Column<string>("nvarchar(1000)", maxLength: 1000, nullable: false),
            CreateRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            CreateSha256 = t.Column<string>("char(64)", maxLength: 64, nullable: false),
            CreatedByUserId = t.Column<long>("bigint", nullable: false),
            CreatedAtUtc = t.Column<DateTime>("datetime2", nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_AccreditationProgramOutcomes", x => x.Id);
            t.UniqueConstraint("AK_AccreditationProgramOutcomes_Tenant_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_AccreditationProgramOutcomes_Program", x => new { x.TenantId, x.AcademicProgramId },
                "AcademicPrograms", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_AccreditationProgramOutcomes_Version", "[VersionNo] BETWEEN 1 AND 1000");
        });
        m.CreateIndex("UX_AccreditationProgramOutcomes_CodeVersion", "AccreditationProgramOutcomes",
            new[] { "TenantId", "AcademicProgramId", "Code", "VersionNo" }, unique: true);
        m.CreateIndex("UX_AccreditationProgramOutcomes_Request", "AccreditationProgramOutcomes",
            new[] { "TenantId", "CreateRequestId" }, unique: true);

        m.CreateTable("AccreditationStandardOutcomeMaps", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            StandardId = t.Column<long>("bigint", nullable: false),
            OutcomeId = t.Column<long>("bigint", nullable: false),
            CreateRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            CreatedByUserId = t.Column<long>("bigint", nullable: false),
            CreatedAtUtc = t.Column<DateTime>("datetime2", nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_AccreditationStandardOutcomeMaps", x => x.Id);
            t.ForeignKey("FK_AccreditationStandardOutcomeMaps_Standard", x => new { x.TenantId, x.StandardId },
                "AccreditationStandards", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_AccreditationStandardOutcomeMaps_Outcome", x => new { x.TenantId, x.OutcomeId },
                "AccreditationProgramOutcomes", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("UX_AccreditationStandardOutcomeMaps_Link", "AccreditationStandardOutcomeMaps",
            new[] { "TenantId", "StandardId", "OutcomeId" }, unique: true);
        m.CreateIndex("UX_AccreditationStandardOutcomeMaps_Request", "AccreditationStandardOutcomeMaps",
            new[] { "TenantId", "CreateRequestId" }, unique: true);

        m.CreateTable("AccreditationAcademicEvidenceLinks", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            AssessmentId = t.Column<long>("bigint", nullable: false),
            ResultPublicationId = t.Column<long>("bigint", nullable: false),
            PublicationVersionNo = t.Column<int>("int", nullable: false),
            PublishedAtUtc = t.Column<DateTime>("datetime2", nullable: false),
            CreateRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            CreatedByUserId = t.Column<long>("bigint", nullable: false),
            CreatedAtUtc = t.Column<DateTime>("datetime2", nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_AccreditationAcademicEvidenceLinks", x => x.Id);
            t.ForeignKey("FK_AccreditationAcademicEvidenceLinks_Assessment", x => new { x.TenantId, x.AssessmentId },
                "AccreditationAssessments", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_AccreditationAcademicEvidenceLinks_Publication", x => new { x.TenantId, x.ResultPublicationId },
                "ResultPublications", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("UX_AccreditationAcademicEvidenceLinks_Link", "AccreditationAcademicEvidenceLinks",
            new[] { "TenantId", "AssessmentId", "ResultPublicationId" }, unique: true);
        m.CreateIndex("UX_AccreditationAcademicEvidenceLinks_Request", "AccreditationAcademicEvidenceLinks",
            new[] { "TenantId", "CreateRequestId" }, unique: true);

        m.CreateTable("ResearchGrantFinanceAccounts", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            SchemeId = t.Column<long>("bigint", nullable: false),
            AccountId = t.Column<long>("bigint", nullable: false),
            FlowCode = t.Column<string>("nvarchar(16)", maxLength: 16, nullable: false),
            NormalSide = t.Column<string>("nvarchar(6)", maxLength: 6, nullable: false),
            CreateRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            CreatedByUserId = t.Column<long>("bigint", nullable: false),
            CreatedAtUtc = t.Column<DateTime>("datetime2", nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_ResearchGrantFinanceAccounts", x => x.Id);
            t.ForeignKey("FK_ResearchGrantFinanceAccounts_Scheme", x => new { x.TenantId, x.SchemeId },
                "ResearchGrantSchemes", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ResearchGrantFinanceAccounts_Account", x => new { x.TenantId, x.AccountId },
                "Accounts", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_ResearchGrantFinanceAccounts_Flow",
                "[FlowCode] IN (N'Disbursement',N'Expense',N'Refund') AND [NormalSide] IN (N'Debit',N'Credit')");
        });
        m.CreateIndex("UX_ResearchGrantFinanceAccounts_Account", "ResearchGrantFinanceAccounts",
            new[] { "TenantId", "SchemeId", "AccountId" }, unique: true);
        m.CreateIndex("UX_ResearchGrantFinanceAccounts_Request", "ResearchGrantFinanceAccounts",
            new[] { "TenantId", "CreateRequestId" }, unique: true);

        m.CreateTable("ResearchGrantJournalLinks", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            GrantId = t.Column<long>("bigint", nullable: false),
            JournalId = t.Column<long>("bigint", nullable: false),
            CreateRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            CreatedByUserId = t.Column<long>("bigint", nullable: false),
            CreatedAtUtc = t.Column<DateTime>("datetime2", nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_ResearchGrantJournalLinks", x => x.Id);
            t.ForeignKey("FK_ResearchGrantJournalLinks_Grant", x => new { x.TenantId, x.GrantId },
                "ResearchGrants", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ResearchGrantJournalLinks_Journal", x => new { x.TenantId, x.JournalId },
                "Journals", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("UX_ResearchGrantJournalLinks_Journal", "ResearchGrantJournalLinks",
            new[] { "TenantId", "JournalId" }, unique: true);
        m.CreateIndex("UX_ResearchGrantJournalLinks_Request", "ResearchGrantJournalLinks",
            new[] { "TenantId", "CreateRequestId" }, unique: true);
        m.CreateIndex("IX_ResearchGrantJournalLinks_Grant", "ResearchGrantJournalLinks",
            new[] { "TenantId", "GrantId", "JournalId" });
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("ResearchGrantJournalLinks");
        m.DropTable("ResearchGrantFinanceAccounts");
        m.DropTable("AccreditationAcademicEvidenceLinks");
        m.DropTable("AccreditationStandardOutcomeMaps");
        m.DropTable("AccreditationProgramOutcomes");
        m.DropColumn("RequiresPublishedAcademicEvidence", "AccreditationStandards");
        m.DropColumn("RequiresOutcomeMapping", "AccreditationStandards");
        m.DropIndex("IX_AccreditationAssessments_Compliance", "AccreditationAssessments");
    }
}
