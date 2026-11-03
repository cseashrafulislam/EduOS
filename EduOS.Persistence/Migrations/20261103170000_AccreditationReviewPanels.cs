using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

// Published standards keep a frozen quorum. Existing standards and reviewers retain one-person review.
[DbContext(typeof(EduOSDbContext))]
[Migration("20261103170000_AccreditationReviewPanels")]
public sealed class AccreditationReviewPanels : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<int>("RequiredReviewerCount", "AccreditationStandards", type: "int",
            nullable: false, defaultValue: 1);
        m.AddCheckConstraint("CK_AccreditationStandards_Reviewers", "AccreditationStandards",
            "[RequiredReviewerCount] BETWEEN 1 AND 3");
        m.CreateTable("AccreditationReviewAssignments", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            AssessmentId = t.Column<long>("bigint", nullable: false),
            RevisionNo = t.Column<int>("int", nullable: false),
            ReviewerUserId = t.Column<long>("bigint", nullable: false),
            AssignedByUserId = t.Column<long>("bigint", nullable: false),
            AssignedAtUtc = t.Column<DateTime>("datetime2", nullable: false),
            DecisionCode = t.Column<string>("nvarchar(24)", maxLength: 24, nullable: false),
            DecisionReason = t.Column<string>("nvarchar(500)", maxLength: 500, nullable: true),
            DecidedAtUtc = t.Column<DateTime>("datetime2", nullable: true)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_AccreditationReviewAssignments", x => x.Id);
            t.ForeignKey("FK_AccreditationReviewAssignments_Assessment", x => new { x.TenantId, x.AssessmentId },
                "AccreditationAssessments", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_AccreditationReviewAssignments_Decision", "[RevisionNo] > 0 AND " +
                "(([DecisionCode] = N'Pending' AND [DecisionReason] IS NULL AND [DecidedAtUtc] IS NULL) OR " +
                "([DecisionCode] IN (N'Approved',N'Rejected',N'ChangesRequested') AND [DecisionReason] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL))");
        });
        m.CreateIndex("UX_AccreditationReviewAssignments_Reviewer", "AccreditationReviewAssignments",
            new[] { "TenantId", "AssessmentId", "RevisionNo", "ReviewerUserId" }, unique: true);
        m.CreateIndex("IX_AccreditationReviewAssignments_Inbox", "AccreditationReviewAssignments",
            new[] { "TenantId", "ReviewerUserId", "DecisionCode", "AssessmentId" });
        // Historical states remain intact; the old ReviewerUserId is retained only as a legacy snapshot.
        m.Sql("""
            INSERT INTO dbo.AccreditationReviewAssignments
                (TenantId, AssessmentId, RevisionNo, ReviewerUserId, AssignedByUserId,
                 AssignedAtUtc, DecisionCode, DecisionReason, DecidedAtUtc)
            SELECT a.TenantId, a.Id, a.CurrentRevision, a.ReviewerUserId, a.OwnerUserId,
                   a.UpdatedAtUtc,
                   CASE a.StateCode WHEN N'Approved' THEN N'Approved'
                       WHEN N'Rejected' THEN N'Rejected' ELSE N'Pending' END,
                   CASE WHEN a.StateCode IN (N'Approved',N'Rejected')
                       THEN N'Legacy review decision migrated from accreditation history.' ELSE NULL END,
                   CASE WHEN a.StateCode IN (N'Approved',N'Rejected')
                       THEN a.UpdatedAtUtc ELSE NULL END
            FROM dbo.AccreditationAssessments AS a
            WHERE a.ReviewerUserId IS NOT NULL AND a.CurrentRevision > 0
              AND NOT EXISTS (SELECT 1 FROM dbo.AccreditationReviewAssignments AS r
                  WHERE r.TenantId = a.TenantId AND r.AssessmentId = a.Id
                  AND r.RevisionNo = a.CurrentRevision AND r.ReviewerUserId = a.ReviewerUserId);
            """);
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("AccreditationReviewAssignments");
        m.DropCheckConstraint("CK_AccreditationStandards_Reviewers", "AccreditationStandards");
        m.DropColumn("RequiredReviewerCount", "AccreditationStandards");
    }
}
