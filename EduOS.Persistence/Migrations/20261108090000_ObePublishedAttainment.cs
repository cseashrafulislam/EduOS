using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

// SQL-owned, additive OBE evidence: canonical published grade snapshots remain authoritative.
[DbContext(typeof(EduOSDbContext))]
[Migration("20261108090000_ObePublishedAttainment")]
public sealed class ObePublishedAttainment : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("ObePolicies", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            CampusId = t.Column<long>("bigint", nullable: false),
            AcademicProgramId = t.Column<long>("bigint", nullable: false),
            VersionNo = t.Column<int>("int", nullable: false),
            CreateRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            InputSha256 = t.Column<string>("char(64)", maxLength: 64, nullable: false),
            CreatedByUserId = t.Column<long>("bigint", nullable: false),
            CreatedAtUtc = t.Column<DateTime>("datetime2", nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_ObePolicies", x => x.Id);
            t.UniqueConstraint("AK_ObePolicies_Tenant_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_ObePolicies_Program", x => new { x.TenantId, x.AcademicProgramId },
                "AcademicPrograms", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ObePolicies_Campus", x => new { x.TenantId, x.CampusId },
                "Campuses", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_ObePolicies_Version", "[VersionNo] BETWEEN 1 AND 1000");
        });
        m.CreateIndex("UX_ObePolicies_Version", "ObePolicies",
            new[] { "TenantId", "CampusId", "AcademicProgramId", "VersionNo" }, unique: true);
        m.CreateIndex("UX_ObePolicies_Request", "ObePolicies", new[] { "TenantId", "CreateRequestId" }, unique: true);

        m.CreateTable("ObeCourseOutcomes", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            PolicyId = t.Column<long>("bigint", nullable: false),
            CurriculumSubjectId = t.Column<long>("bigint", nullable: false),
            Code = t.Column<string>("nvarchar(40)", maxLength: 40, nullable: false),
            Statement = t.Column<string>("nvarchar(1000)", maxLength: 1000, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_ObeCourseOutcomes", x => x.Id);
            t.UniqueConstraint("AK_ObeCourseOutcomes_Tenant_Id", x => new { x.TenantId, x.Id });
            t.UniqueConstraint("AK_ObeCourseOutcomes_Tenant_Policy_Id", x => new { x.TenantId, x.PolicyId, x.Id });
            t.ForeignKey("FK_ObeCourseOutcomes_Policy", x => new { x.TenantId, x.PolicyId },
                "ObePolicies", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ObeCourseOutcomes_Course", x => new { x.TenantId, x.CurriculumSubjectId },
                "CurriculumSubjects", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("UX_ObeCourseOutcomes_Code", "ObeCourseOutcomes", new[] { "TenantId", "PolicyId", "Code" }, unique: true);
        m.CreateIndex("UX_ObeCourseOutcomes_Course", "ObeCourseOutcomes", new[] { "TenantId", "PolicyId", "CurriculumSubjectId" }, unique: true);

        m.CreateTable("ObePloContributions", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            PolicyId = t.Column<long>("bigint", nullable: false),
            CourseOutcomeId = t.Column<long>("bigint", nullable: false),
            ProgramOutcomeId = t.Column<long>("bigint", nullable: false),
            WeightPercent = t.Column<decimal>("decimal(9,4)", nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_ObePloContributions", x => x.Id);
            t.ForeignKey("FK_ObePloContributions_Policy", x => new { x.TenantId, x.PolicyId },
                "ObePolicies", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ObePloContributions_CLO", x => new { x.TenantId, x.PolicyId, x.CourseOutcomeId },
                "ObeCourseOutcomes", new[] { "TenantId", "PolicyId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ObePloContributions_PLO", x => new { x.TenantId, x.ProgramOutcomeId },
                "AccreditationProgramOutcomes", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_ObePloContributions_Weight", "[WeightPercent] > 0 AND [WeightPercent] <= 100");
        });
        m.CreateIndex("UX_ObePloContributions_Link", "ObePloContributions",
            new[] { "TenantId", "PolicyId", "ProgramOutcomeId", "CourseOutcomeId" }, unique: true);
        m.CreateIndex("IX_ObePloContributions_CLO", "ObePloContributions", new[] { "TenantId", "CourseOutcomeId" });

        m.CreateTable("ObeAttainmentReports", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            Reference = t.Column<Guid>("uniqueidentifier", nullable: false),
            PolicyId = t.Column<long>("bigint", nullable: false),
            ResultPublicationId = t.Column<long>("bigint", nullable: false),
            PublicationVersionNo = t.Column<int>("int", nullable: false),
            StateCode = t.Column<string>("nvarchar(16)", maxLength: 16, nullable: false),
            SubmitRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            SubmittedByUserId = t.Column<long>("bigint", nullable: false),
            SubmittedAtUtc = t.Column<DateTime>("datetime2", nullable: false),
            SnapshotJson = t.Column<string>("nvarchar(max)", nullable: false),
            SnapshotSha256 = t.Column<string>("char(64)", maxLength: 64, nullable: false),
            ReviewedByUserId = t.Column<long>("bigint", nullable: true),
            ReviewedAtUtc = t.Column<DateTime>("datetime2", nullable: true),
            ReviewRequestId = t.Column<Guid>("uniqueidentifier", nullable: true),
            ReviewReason = t.Column<string>("nvarchar(500)", maxLength: 500, nullable: true)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_ObeAttainmentReports", x => x.Id);
            t.ForeignKey("FK_ObeAttainmentReports_Policy", x => new { x.TenantId, x.PolicyId },
                "ObePolicies", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ObeAttainmentReports_Publication", x => new { x.TenantId, x.ResultPublicationId },
                "ResultPublications", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_ObeAttainmentReports_State",
                "[StateCode] IN (N'Submitted',N'Approved',N'Rejected') AND [PublicationVersionNo] > 0");
            t.CheckConstraint("CK_ObeAttainmentReports_Review",
                "([StateCode] = N'Submitted' AND [ReviewedByUserId] IS NULL AND [ReviewedAtUtc] IS NULL " +
                "AND [ReviewRequestId] IS NULL AND [ReviewReason] IS NULL) OR " +
                "([StateCode] IN (N'Approved',N'Rejected') AND [ReviewedByUserId] IS NOT NULL " +
                "AND [ReviewedAtUtc] IS NOT NULL AND [ReviewRequestId] IS NOT NULL " +
                "AND LEN([ReviewReason]) BETWEEN 10 AND 500)");
        });
        m.CreateIndex("UX_ObeAttainmentReports_Reference", "ObeAttainmentReports", new[] { "TenantId", "Reference" }, unique: true);
        m.CreateIndex("UX_ObeAttainmentReports_Submit", "ObeAttainmentReports", new[] { "TenantId", "SubmitRequestId" }, unique: true);
        m.CreateIndex("UX_ObeAttainmentReports_Scope", "ObeAttainmentReports", new[] { "TenantId", "PolicyId", "ResultPublicationId" }, unique: true);
        m.CreateIndex("UX_ObeAttainmentReports_Review", "ObeAttainmentReports", new[] { "TenantId", "ReviewRequestId" }, unique: true, filter: "[ReviewRequestId] IS NOT NULL");
        m.CreateIndex("IX_ObeAttainmentReports_Report", "ObeAttainmentReports", new[] { "TenantId", "SubmittedAtUtc", "StateCode" });
        // Prevent later ad-hoc edits to the policy or the submitted grade snapshot.
        m.Sql("""
            CREATE TRIGGER dbo.TR_ObePolicies_Immutable ON dbo.ObePolicies AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted)
                    THROW 51001, 'An OBE policy version is immutable.', 1;
            END
            """);
        m.Sql("""
            CREATE TRIGGER dbo.TR_ObeCourseOutcomes_Immutable ON dbo.ObeCourseOutcomes AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted)
                    THROW 51002, 'An OBE course outcome is immutable.', 1;
            END
            """);
        m.Sql("""
            CREATE TRIGGER dbo.TR_ObePloContributions_Immutable ON dbo.ObePloContributions AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted)
                    THROW 51003, 'An OBE contribution is immutable.', 1;
            END
            """);
        m.Sql("""
            CREATE TRIGGER dbo.TR_ObeAttainmentReports_Freeze ON dbo.ObeAttainmentReports AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (
                    SELECT 1 FROM deleted AS d LEFT JOIN inserted AS i ON i.Id = d.Id
                    WHERE i.Id IS NULL OR d.StateCode <> N'Submitted' OR
                        i.StateCode NOT IN (N'Approved', N'Rejected') OR
                        i.TenantId <> d.TenantId OR i.Reference <> d.Reference OR
                        i.PolicyId <> d.PolicyId OR i.ResultPublicationId <> d.ResultPublicationId OR
                        i.PublicationVersionNo <> d.PublicationVersionNo OR
                        i.SubmitRequestId <> d.SubmitRequestId OR
                        i.SubmittedByUserId <> d.SubmittedByUserId OR
                        i.SubmittedAtUtc <> d.SubmittedAtUtc OR
                        i.SnapshotSha256 <> d.SnapshotSha256 OR i.SnapshotJson <> d.SnapshotJson OR
                        i.ReviewedByUserId IS NULL OR i.ReviewedAtUtc IS NULL OR
                        i.ReviewRequestId IS NULL OR i.ReviewReason IS NULL
                ) THROW 51004, 'An OBE report can only receive one immutable independent decision.', 1;
            END
            """);
    }

    protected override void Down(MigrationBuilder m)
    {
        m.Sql("DROP TRIGGER IF EXISTS dbo.TR_ObeAttainmentReports_Freeze;");
        m.Sql("DROP TRIGGER IF EXISTS dbo.TR_ObePloContributions_Immutable;");
        m.Sql("DROP TRIGGER IF EXISTS dbo.TR_ObeCourseOutcomes_Immutable;");
        m.Sql("DROP TRIGGER IF EXISTS dbo.TR_ObePolicies_Immutable;");
        m.DropTable("ObeAttainmentReports");
        m.DropTable("ObePloContributions");
        m.DropTable("ObeCourseOutcomes");
        m.DropTable("ObePolicies");
    }
}
