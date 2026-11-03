using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

// Additive SQL-owned extension. No existing Finance, Document or Academic records are modified.
[DbContext(typeof(EduOSDbContext))]
[Migration("20261103120000_ResearchGrantsAccreditation")]
public sealed class ResearchGrantsAccreditation : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("ResearchGrantSchemes", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            Code = t.Column<string>("nvarchar(40)", maxLength: 40, nullable: false),
            CreateRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            CreateSha256 = t.Column<string>("char(64)", maxLength: 64, nullable: false),
            Name = t.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
            CurrencyCode = t.Column<string>("char(3)", maxLength: 3, nullable: false),
            MaxAward = t.Column<decimal>("decimal(19,2)", nullable: false),
            IsActive = t.Column<bool>("bit", nullable: false, defaultValue: true),
            CreatedByUserId = t.Column<long>("bigint", nullable: false),
            CreatedAtUtc = t.Column<DateTime>("datetime2", nullable: false),
            RowVersion = t.Column<byte[]>("rowversion", rowVersion: true, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_ResearchGrantSchemes", x => x.Id);
            t.UniqueConstraint("AK_ResearchGrantSchemes_Tenant_Id", x => new { x.TenantId, x.Id });
            t.CheckConstraint("CK_ResearchGrantSchemes_Max", "[MaxAward] > 0");
        });
        m.CreateIndex("UX_ResearchGrantSchemes_Code", "ResearchGrantSchemes", new[] { "TenantId", "Code" }, unique: true);
        m.CreateIndex("UX_ResearchGrantSchemes_Create", "ResearchGrantSchemes", new[] { "TenantId", "CreateRequestId" }, unique: true);

        m.CreateTable("ResearchGrants", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            PublicId = t.Column<Guid>("uniqueidentifier", nullable: false),
            ProposalId = t.Column<long>("bigint", nullable: false),
            SchemeId = t.Column<long>("bigint", nullable: false),
            OwnerUserId = t.Column<long>("bigint", nullable: false),
            Title = t.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
            RequestedAmount = t.Column<decimal>("decimal(19,2)", nullable: false),
            ApprovedAmount = t.Column<decimal>("decimal(19,2)", nullable: true),
            PendingAmount = t.Column<decimal>("decimal(19,2)", nullable: true),
            BudgetVersion = t.Column<int>("int", nullable: false, defaultValue: 0),
            StateCode = t.Column<string>("nvarchar(24)", maxLength: 24, nullable: false),
            CreatedAtUtc = t.Column<DateTime>("datetime2", nullable: false),
            UpdatedAtUtc = t.Column<DateTime>("datetime2", nullable: false),
            RowVersion = t.Column<byte[]>("rowversion", rowVersion: true, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_ResearchGrants", x => x.Id);
            t.UniqueConstraint("AK_ResearchGrants_Tenant_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_ResearchGrants_Proposal", x => new { x.TenantId, x.ProposalId },
                "ResearchProposals", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ResearchGrants_Scheme", x => new { x.TenantId, x.SchemeId },
                "ResearchGrantSchemes", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_ResearchGrants_Amount", "[RequestedAmount] > 0 AND ([ApprovedAmount] IS NULL OR [ApprovedAmount] > 0) AND ([PendingAmount] IS NULL OR [PendingAmount] > 0)");
            t.CheckConstraint("CK_ResearchGrants_State", "[StateCode] IN (N'Draft',N'Submitted',N'Rejected',N'Active',N'AmendmentPending',N'ClosurePending',N'Closed',N'Withdrawn')");
        });
        m.CreateIndex("UX_ResearchGrants_Public", "ResearchGrants", new[] { "TenantId", "PublicId" }, unique: true);
        m.CreateIndex("IX_ResearchGrants_Proposal", "ResearchGrants", new[] { "TenantId", "ProposalId", "StateCode" });
        m.CreateIndex("IX_ResearchGrants_Owner", "ResearchGrants", new[] { "TenantId", "OwnerUserId", "UpdatedAtUtc" });

        m.CreateTable("ResearchGrantBudgets", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            GrantId = t.Column<long>("bigint", nullable: false),
            VersionNo = t.Column<int>("int", nullable: false),
            ApprovedAmount = t.Column<decimal>("decimal(19,2)", nullable: false),
            CurrencyCode = t.Column<string>("char(3)", maxLength: 3, nullable: false),
            ApprovedByUserId = t.Column<long>("bigint", nullable: false),
            Reason = t.Column<string>("nvarchar(500)", maxLength: 500, nullable: false),
            ApprovedAtUtc = t.Column<DateTime>("datetime2", nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_ResearchGrantBudgets", x => x.Id);
            t.ForeignKey("FK_ResearchGrantBudgets_Grant", x => new { x.TenantId, x.GrantId },
                "ResearchGrants", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_ResearchGrantBudgets_Positive", "[VersionNo] > 0 AND [ApprovedAmount] > 0");
        });
        m.CreateIndex("UX_ResearchGrantBudgets_Version", "ResearchGrantBudgets", new[] { "TenantId", "GrantId", "VersionNo" }, unique: true);

        m.CreateTable("ResearchGrantMilestones", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            GrantId = t.Column<long>("bigint", nullable: false),
            Title = t.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
            DueDate = t.Column<DateTime>("date", nullable: false),
            Outcome = t.Column<string>("nvarchar(2000)", maxLength: 2000, nullable: true),
            CompletedByUserId = t.Column<long>("bigint", nullable: true),
            CompletedAtUtc = t.Column<DateTime>("datetime2", nullable: true),
            CreatedAtUtc = t.Column<DateTime>("datetime2", nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_ResearchGrantMilestones", x => x.Id);
            t.ForeignKey("FK_ResearchGrantMilestones_Grant", x => new { x.TenantId, x.GrantId },
                "ResearchGrants", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_ResearchGrantMilestones_Due", "ResearchGrantMilestones", new[] { "TenantId", "GrantId", "DueDate" });

        m.CreateTable("ResearchGrantEvents", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            GrantId = t.Column<long>("bigint", nullable: false),
            ClientRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            RequestSha256 = t.Column<string>("char(64)", maxLength: 64, nullable: false),
            ActorUserId = t.Column<long>("bigint", nullable: false),
            ActionCode = t.Column<string>("nvarchar(24)", maxLength: 24, nullable: false),
            FromState = t.Column<string>("nvarchar(24)", maxLength: 24, nullable: false),
            ToState = t.Column<string>("nvarchar(24)", maxLength: 24, nullable: false),
            Reason = t.Column<string>("nvarchar(500)", maxLength: 500, nullable: true),
            ResultSnapshot = t.Column<string>("nvarchar(max)", nullable: false),
            OccurredAtUtc = t.Column<DateTime>("datetime2", nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_ResearchGrantEvents", x => x.Id);
            t.ForeignKey("FK_ResearchGrantEvents_Grant", x => new { x.TenantId, x.GrantId },
                "ResearchGrants", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("UX_ResearchGrantEvents_Request", "ResearchGrantEvents", new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("IX_ResearchGrantEvents_History", "ResearchGrantEvents", new[] { "TenantId", "GrantId", "OccurredAtUtc" });

        m.CreateTable("AccreditationFrameworks", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            Code = t.Column<string>("nvarchar(40)", maxLength: 40, nullable: false),
            Name = t.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
            VersionNo = t.Column<int>("int", nullable: false),
            StateCode = t.Column<string>("nvarchar(16)", maxLength: 16, nullable: false),
            CreateRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            CreateSha256 = t.Column<string>("char(64)", maxLength: 64, nullable: false),
            PublishRequestId = t.Column<Guid>("uniqueidentifier", nullable: true),
            PublishSha256 = t.Column<string>("char(64)", maxLength: 64, nullable: true),
            CreatedByUserId = t.Column<long>("bigint", nullable: false),
            PublishedByUserId = t.Column<long>("bigint", nullable: true),
            PublishedAtUtc = t.Column<DateTime>("datetime2", nullable: true),
            CreatedAtUtc = t.Column<DateTime>("datetime2", nullable: false),
            RowVersion = t.Column<byte[]>("rowversion", rowVersion: true, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_AccreditationFrameworks", x => x.Id);
            t.UniqueConstraint("AK_AccreditationFrameworks_Tenant_Id", x => new { x.TenantId, x.Id });
            t.CheckConstraint("CK_AccreditationFrameworks_State", "[VersionNo] > 0 AND [StateCode] IN (N'Draft',N'Published')");
        });
        m.CreateIndex("UX_AccreditationFrameworks_CodeVersion", "AccreditationFrameworks", new[] { "TenantId", "Code", "VersionNo" }, unique: true);
        m.CreateIndex("UX_AccreditationFrameworks_Create", "AccreditationFrameworks", new[] { "TenantId", "CreateRequestId" }, unique: true);
        m.CreateIndex("UX_AccreditationFrameworks_Publish", "AccreditationFrameworks", new[] { "TenantId", "PublishRequestId" }, unique: true, filter: "[PublishRequestId] IS NOT NULL");

        m.CreateTable("AccreditationStandards", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            FrameworkId = t.Column<long>("bigint", nullable: false),
            CreateRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            CreateSha256 = t.Column<string>("char(64)", maxLength: 64, nullable: false),
            Code = t.Column<string>("nvarchar(40)", maxLength: 40, nullable: false),
            Title = t.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
            Rubric = t.Column<string>("nvarchar(2000)", maxLength: 2000, nullable: false),
            RequiredEvidence = t.Column<string>("nvarchar(2000)", maxLength: 2000, nullable: false),
            ValidityDays = t.Column<int>("int", nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_AccreditationStandards", x => x.Id);
            t.UniqueConstraint("AK_AccreditationStandards_Tenant_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_AccreditationStandards_Framework", x => new { x.TenantId, x.FrameworkId },
                "AccreditationFrameworks", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_AccreditationStandards_Validity", "[ValidityDays] BETWEEN 1 AND 3650");
        });
        m.CreateIndex("UX_AccreditationStandards_Code", "AccreditationStandards", new[] { "TenantId", "FrameworkId", "Code" }, unique: true);
        m.CreateIndex("UX_AccreditationStandards_Create", "AccreditationStandards", new[] { "TenantId", "CreateRequestId" }, unique: true);

        m.CreateTable("AccreditationAssessments", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            PublicId = t.Column<Guid>("uniqueidentifier", nullable: false),
            StandardId = t.Column<long>("bigint", nullable: false),
            CampusId = t.Column<long>("bigint", nullable: false),
            AcademicProgramId = t.Column<long>("bigint", nullable: true),
            OwnerUserId = t.Column<long>("bigint", nullable: false),
            ReviewerUserId = t.Column<long>("bigint", nullable: true),
            PeriodStart = t.Column<DateTime>("date", nullable: false),
            PeriodEnd = t.Column<DateTime>("date", nullable: false),
            CurrentRevision = t.Column<int>("int", nullable: false, defaultValue: 0),
            EvidenceSummary = t.Column<string>("nvarchar(3000)", maxLength: 3000, nullable: false),
            EvidenceSha256 = t.Column<string>("char(64)", maxLength: 64, nullable: false),
            StateCode = t.Column<string>("nvarchar(24)", maxLength: 24, nullable: false),
            ApprovedAtUtc = t.Column<DateTime>("datetime2", nullable: true),
            ExpiresAtUtc = t.Column<DateTime>("datetime2", nullable: true),
            CreatedAtUtc = t.Column<DateTime>("datetime2", nullable: false),
            UpdatedAtUtc = t.Column<DateTime>("datetime2", nullable: false),
            RowVersion = t.Column<byte[]>("rowversion", rowVersion: true, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_AccreditationAssessments", x => x.Id);
            t.UniqueConstraint("AK_AccreditationAssessments_Tenant_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_AccreditationAssessments_Standard", x => new { x.TenantId, x.StandardId },
                "AccreditationStandards", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_AccreditationAssessments_Campus", x => new { x.TenantId, x.CampusId },
                "Campuses", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_AccreditationAssessments_Program", x => new { x.TenantId, x.AcademicProgramId },
                "AcademicPrograms", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_AccreditationAssessments_Period", "[PeriodStart] <= [PeriodEnd] AND [CurrentRevision] >= 0");
            t.CheckConstraint("CK_AccreditationAssessments_State", "[StateCode] IN (N'Draft',N'Submitted',N'Assigned',N'ChangesRequested',N'Approved',N'Rejected',N'Withdrawn')");
        });
        m.CreateIndex("UX_AccreditationAssessments_Public", "AccreditationAssessments", new[] { "TenantId", "PublicId" }, unique: true);
        m.CreateIndex("IX_AccreditationAssessments_Campus", "AccreditationAssessments", new[] { "TenantId", "CampusId", "StateCode", "UpdatedAtUtc" });
        m.CreateIndex("IX_AccreditationAssessments_Standard", "AccreditationAssessments", new[] { "TenantId", "StandardId", "PeriodEnd" });

        m.CreateTable("AccreditationEvidenceVersions", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            AssessmentId = t.Column<long>("bigint", nullable: false),
            RevisionNo = t.Column<int>("int", nullable: false),
            EvidenceSummary = t.Column<string>("nvarchar(3000)", maxLength: 3000, nullable: false),
            EvidenceSha256 = t.Column<string>("char(64)", maxLength: 64, nullable: false),
            SubmittedByUserId = t.Column<long>("bigint", nullable: false),
            SubmittedAtUtc = t.Column<DateTime>("datetime2", nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_AccreditationEvidenceVersions", x => x.Id);
            t.ForeignKey("FK_AccreditationEvidenceVersions_Assessment", x => new { x.TenantId, x.AssessmentId },
                "AccreditationAssessments", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_AccreditationEvidenceVersions_Revision", "[RevisionNo] > 0");
        });
        m.CreateIndex("UX_AccreditationEvidenceVersions_Revision", "AccreditationEvidenceVersions", new[] { "TenantId", "AssessmentId", "RevisionNo" }, unique: true);

        m.CreateTable("AccreditationEvents", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            AssessmentId = t.Column<long>("bigint", nullable: false),
            ClientRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            RequestSha256 = t.Column<string>("char(64)", maxLength: 64, nullable: false),
            ActorUserId = t.Column<long>("bigint", nullable: false),
            ActionCode = t.Column<string>("nvarchar(24)", maxLength: 24, nullable: false),
            FromState = t.Column<string>("nvarchar(24)", maxLength: 24, nullable: false),
            ToState = t.Column<string>("nvarchar(24)", maxLength: 24, nullable: false),
            RevisionNo = t.Column<int>("int", nullable: false),
            Reason = t.Column<string>("nvarchar(500)", maxLength: 500, nullable: true),
            ResultSnapshot = t.Column<string>("nvarchar(max)", nullable: false),
            OccurredAtUtc = t.Column<DateTime>("datetime2", nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_AccreditationEvents", x => x.Id);
            t.ForeignKey("FK_AccreditationEvents_Assessment", x => new { x.TenantId, x.AssessmentId },
                "AccreditationAssessments", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("UX_AccreditationEvents_Request", "AccreditationEvents", new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("IX_AccreditationEvents_History", "AccreditationEvents", new[] { "TenantId", "AssessmentId", "OccurredAtUtc" });
    }

    protected override void Down(MigrationBuilder m)
    {
        // Reversing this migration deletes legal audit evidence; never run Down against live data.
        m.DropTable("AccreditationEvents");
        m.DropTable("AccreditationEvidenceVersions");
        m.DropTable("AccreditationAssessments");
        m.DropTable("AccreditationStandards");
        m.DropTable("AccreditationFrameworks");
        m.DropTable("ResearchGrantEvents");
        m.DropTable("ResearchGrantMilestones");
        m.DropTable("ResearchGrantBudgets");
        m.DropTable("ResearchGrants");
        m.DropTable("ResearchGrantSchemes");
    }
}
