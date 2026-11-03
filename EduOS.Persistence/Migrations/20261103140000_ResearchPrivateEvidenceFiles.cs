using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

// Dedicated, immutable attachment links. The existing FileAsset owns bytes and scanner metadata.
[DbContext(typeof(EduOSDbContext))]
[Migration("20261103140000_ResearchPrivateEvidenceFiles")]
public sealed class ResearchPrivateEvidenceFiles : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("ResearchEvidenceFiles", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            ProposalId = t.Column<long>("bigint", nullable: true),
            AssessmentId = t.Column<long>("bigint", nullable: true),
            RevisionNo = t.Column<int>("int", nullable: false),
            FileAssetId = t.Column<long>("bigint", nullable: false),
            AttachedByUserId = t.Column<long>("bigint", nullable: false),
            AttachedAtUtc = t.Column<DateTime>("datetime2", nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_ResearchEvidenceFiles", x => x.Id);
            t.ForeignKey("FK_ResearchEvidenceFiles_Proposal", x => new { x.TenantId, x.ProposalId },
                "ResearchProposals", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ResearchEvidenceFiles_Assessment", x => new { x.TenantId, x.AssessmentId },
                "AccreditationAssessments", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ResearchEvidenceFiles_Asset", x => new { x.TenantId, x.FileAssetId },
                "FileAssets", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_ResearchEvidenceFiles_OneOwner",
                "(([ProposalId] IS NOT NULL AND [AssessmentId] IS NULL) OR ([ProposalId] IS NULL AND [AssessmentId] IS NOT NULL))");
            t.CheckConstraint("CK_ResearchEvidenceFiles_Revision", "[RevisionNo] > 0");
        });
        m.CreateIndex("UX_ResearchEvidenceFiles_Asset", "ResearchEvidenceFiles",
            new[] { "TenantId", "FileAssetId" }, unique: true);
        m.CreateIndex("IX_ResearchEvidenceFiles_Proposal", "ResearchEvidenceFiles",
            new[] { "TenantId", "ProposalId", "RevisionNo" });
        m.CreateIndex("IX_ResearchEvidenceFiles_Assessment", "ResearchEvidenceFiles",
            new[] { "TenantId", "AssessmentId", "RevisionNo" });
    }

    protected override void Down(MigrationBuilder m)
    {
        // Do not invoke on a live institute: removes immutable research evidence lineage.
        m.DropTable("ResearchEvidenceFiles");
    }
}
