using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

// Append-only claims with a single independent decision. No finance posting or grant budget mutation.
[DbContext(typeof(EduOSDbContext))]
[Migration("20261103160000_ResearchGrantVerifiedOutputs")]
public sealed class ResearchGrantVerifiedOutputs : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        // Composite foreign key requires this unique principal key to exist before CREATE TABLE.
        m.AddUniqueConstraint("AK_ResearchGrantMilestones_Tenant_Grant_Id", "ResearchGrantMilestones",
            new[] { "TenantId", "GrantId", "Id" });
        m.CreateTable("ResearchGrantOutputs", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            GrantId = t.Column<long>("bigint", nullable: false),
            PublicId = t.Column<Guid>("uniqueidentifier", nullable: false),
            SubmitRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            SubmitSha256 = t.Column<string>("char(64)", maxLength: 64, nullable: false),
            MilestoneId = t.Column<long>("bigint", nullable: true),
            KindCode = t.Column<string>("nvarchar(24)", maxLength: 24, nullable: false),
            Title = t.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
            Summary = t.Column<string>("nvarchar(2000)", maxLength: 2000, nullable: false),
            ReferenceUrl = t.Column<string>("nvarchar(500)", maxLength: 500, nullable: true),
            OccurredOn = t.Column<DateTime>("date", nullable: false),
            SubmittedByUserId = t.Column<long>("bigint", nullable: false),
            SubmittedAtUtc = t.Column<DateTime>("datetime2", nullable: false),
            StateCode = t.Column<string>("nvarchar(16)", maxLength: 16, nullable: false),
            DecisionRequestId = t.Column<Guid>("uniqueidentifier", nullable: true),
            DecisionSha256 = t.Column<string>("char(64)", maxLength: 64, nullable: true),
            DecisionByUserId = t.Column<long>("bigint", nullable: true),
            DecisionReason = t.Column<string>("nvarchar(500)", maxLength: 500, nullable: true),
            DecisionAtUtc = t.Column<DateTime>("datetime2", nullable: true),
            RowVersion = t.Column<byte[]>("rowversion", rowVersion: true, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_ResearchGrantOutputs", x => x.Id);
            t.ForeignKey("FK_ResearchGrantOutputs_Grant", x => new { x.TenantId, x.GrantId },
                "ResearchGrants", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ResearchGrantOutputs_Milestone", x => new { x.TenantId, x.GrantId, x.MilestoneId },
                "ResearchGrantMilestones", new[] { "TenantId", "GrantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.CheckConstraint("CK_ResearchGrantOutputs_Kind", "[KindCode] IN (N'Publication',N'Dataset',N'Patent',N'Prototype',N'CommunityImpact',N'Other')");
            t.CheckConstraint("CK_ResearchGrantOutputs_State", "([StateCode] = N'Submitted' AND [DecisionRequestId] IS NULL AND [DecisionSha256] IS NULL AND [DecisionByUserId] IS NULL AND [DecisionAtUtc] IS NULL AND [DecisionReason] IS NULL) OR ([StateCode] IN (N'Verified',N'Rejected') AND [DecisionRequestId] IS NOT NULL AND [DecisionSha256] IS NOT NULL AND [DecisionByUserId] IS NOT NULL AND [DecisionAtUtc] IS NOT NULL AND [DecisionReason] IS NOT NULL)");
            t.CheckConstraint("CK_ResearchGrantOutputs_Independent", "[DecisionByUserId] IS NULL OR [DecisionByUserId] <> [SubmittedByUserId]");
        });
        m.CreateIndex("UX_ResearchGrantOutputs_Public", "ResearchGrantOutputs",
            new[] { "TenantId", "PublicId" }, unique: true);
        m.CreateIndex("UX_ResearchGrantOutputs_Submit", "ResearchGrantOutputs",
            new[] { "TenantId", "SubmitRequestId" }, unique: true);
        m.CreateIndex("UX_ResearchGrantOutputs_Decision", "ResearchGrantOutputs",
            new[] { "TenantId", "DecisionRequestId" }, unique: true, filter: "[DecisionRequestId] IS NOT NULL");
        m.CreateIndex("IX_ResearchGrantOutputs_Grant_State", "ResearchGrantOutputs",
            new[] { "TenantId", "GrantId", "StateCode", "SubmittedAtUtc" });
        m.CreateIndex("IX_ResearchGrantOutputs_Report", "ResearchGrantOutputs",
            new[] { "TenantId", "OccurredOn", "KindCode", "StateCode" });
    }

    protected override void Down(MigrationBuilder m)
    {
        // A rollback against live records would destroy immutable outcome decisions.
        m.DropTable("ResearchGrantOutputs");
        m.DropUniqueConstraint("AK_ResearchGrantMilestones_Tenant_Grant_Id", "ResearchGrantMilestones");
    }
}
