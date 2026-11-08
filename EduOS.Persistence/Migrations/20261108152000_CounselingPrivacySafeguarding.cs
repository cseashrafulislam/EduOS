using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20261108152000_CounselingPrivacySafeguarding")]
public sealed class CounselingPrivacySafeguarding : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<Guid?>("RedactionRequestId", "CounselingCases", "uniqueidentifier", nullable: true);
        m.AddColumn<long?>("RedactionRequestedByUserId", "CounselingCases", "bigint", nullable: true);
        m.AddColumn<DateTime?>("RedactionRequestedAtUtc", "CounselingCases", "datetime2", nullable: true);
        m.AddColumn<DateTime?>("RedactedAtUtc", "CounselingCases", "datetime2", nullable: true);
        m.AddColumn<long?>("RedactedByUserId", "CounselingCases", "bigint", nullable: true);
        m.CreateIndex("IX_CounselingCases_RedactionRequestedByUserId", "CounselingCases", "RedactionRequestedByUserId");
        m.CreateIndex("IX_CounselingCases_RedactedByUserId", "CounselingCases", "RedactedByUserId");
        m.AddForeignKey("FK_CounselingCases_AspNetUsers_RedactionRequestedByUserId",
            "CounselingCases", "RedactionRequestedByUserId", "AspNetUsers", "Id",
            onDelete: ReferentialAction.Restrict);
        m.AddForeignKey("FK_CounselingCases_AspNetUsers_RedactedByUserId",
            "CounselingCases", "RedactedByUserId", "AspNetUsers", "Id",
            onDelete: ReferentialAction.Restrict);

        m.CreateTable("CounselingCampusPolicies", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            CampusId = t.Column<long>("bigint", nullable: false),
            RetentionDays = t.Column<int>("int", nullable: false),
            LegalHold = t.Column<bool>("bit", nullable: false),
            EmergencyGuidanceEn = t.Column<string>("nvarchar(350)", maxLength: 350, nullable: false),
            EmergencyGuidanceBn = t.Column<string>("nvarchar(350)", maxLength: 350, nullable: false),
            ApprovedByUserId = t.Column<long>("bigint", nullable: false),
            ApprovedAtUtc = t.Column<DateTime>("datetime2", nullable: false),
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
            t.PrimaryKey("PK_CounselingCampusPolicies", x => x.Id);
            t.UniqueConstraint("AK_CounselingCampusPolicies_TenantId_Id", x => new { x.TenantId, x.Id });
            t.CheckConstraint("CK_CounselingCampusPolicy_Retention", "[RetentionDays] BETWEEN 30 AND 3650");
            t.ForeignKey("FK_CounselingCampusPolicies_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CounselingCampusPolicies_Campuses", x =>
                new { x.TenantId, x.CampusId }, "Campuses",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CounselingCampusPolicies_AspNetUsers_ApprovedByUserId", x => x.ApprovedByUserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_CounselingCampusPolicies_TenantId_CampusId", "CounselingCampusPolicies",
            new[] { "TenantId", "CampusId" }, unique: true);
        m.CreateIndex("IX_CounselingCampusPolicies_ApprovedByUserId", "CounselingCampusPolicies", "ApprovedByUserId");

        m.CreateTable("CounselingPolicyChanges", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            PublicId = t.Column<Guid>("uniqueidentifier", nullable: false),
            ClientRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            ReviewRequestId = t.Column<Guid?>("uniqueidentifier", nullable: true),
            CampusId = t.Column<long>("bigint", nullable: false),
            State = t.Column<string>("nvarchar(12)", maxLength: 12, nullable: false),
            RetentionDays = t.Column<int>("int", nullable: false),
            LegalHold = t.Column<bool>("bit", nullable: false),
            EmergencyGuidanceEn = t.Column<string>("nvarchar(350)", maxLength: 350, nullable: false),
            EmergencyGuidanceBn = t.Column<string>("nvarchar(350)", maxLength: 350, nullable: false),
            ExpectedPolicyVersion = t.Column<byte[]>("varbinary(8)", maxLength: 8, nullable: true),
            Reason = t.Column<string>("nvarchar(500)", maxLength: 500, nullable: false),
            ProposedByUserId = t.Column<long>("bigint", nullable: false),
            ProposedAtUtc = t.Column<DateTime>("datetime2", nullable: false),
            ReviewedByUserId = t.Column<long?>("bigint", nullable: true),
            ReviewedAtUtc = t.Column<DateTime?>("datetime2", nullable: true),
            ReviewReason = t.Column<string>("nvarchar(500)", maxLength: 500, nullable: true),
            CreatedAt = t.Column<DateTime>("datetime2", nullable: false),
            CreatedBy = t.Column<long?>("bigint", nullable: true),
            UpdatedAt = t.Column<DateTime?>("datetime2", nullable: true),
            UpdatedBy = t.Column<long?>("bigint", nullable: true),
            IsDeleted = t.Column<bool>("bit", nullable: false),
            DeletedAt = t.Column<DateTime?>("datetime2", nullable: true),
            DeletedBy = t.Column<long?>("bigint", nullable: true),
            RowVersion = t.Column<byte[]>("rowversion", rowVersion: true, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_CounselingPolicyChanges", x => x.Id);
            t.UniqueConstraint("AK_CounselingPolicyChanges_TenantId_Id", x => new { x.TenantId, x.Id });
            t.CheckConstraint("CK_CounselingPolicyChange_Retention", "[RetentionDays] BETWEEN 30 AND 3650");
            t.CheckConstraint("CK_CounselingPolicyChange_State",
                "[State] IN ('Submitted','Approved','Rejected')");
            t.CheckConstraint("CK_CounselingPolicyChange_Reviewer",
                "([State]='Submitted' AND [ReviewedByUserId] IS NULL AND [ReviewedAtUtc] IS NULL AND [ReviewRequestId] IS NULL) OR " +
                "([State]<>'Submitted' AND [ReviewedByUserId] IS NOT NULL AND [ReviewedAtUtc] IS NOT NULL AND " +
                "[ReviewRequestId] IS NOT NULL AND [ReviewedByUserId]<>[ProposedByUserId])");
            t.ForeignKey("FK_CounselingPolicyChanges_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CounselingPolicyChanges_Campuses", x =>
                new { x.TenantId, x.CampusId }, "Campuses",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CounselingPolicyChanges_AspNetUsers_ProposedByUserId", x => x.ProposedByUserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CounselingPolicyChanges_AspNetUsers_ReviewedByUserId", x => x.ReviewedByUserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_CounselingPolicyChanges_PublicId", "CounselingPolicyChanges", "PublicId", unique: true);
        m.CreateIndex("IX_CounselingPolicyChanges_TenantId_ClientRequestId", "CounselingPolicyChanges",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("IX_CounselingPolicyChanges_TenantId_ReviewRequestId", "CounselingPolicyChanges",
            new[] { "TenantId", "ReviewRequestId" }, unique: true,
            filter: "[ReviewRequestId] IS NOT NULL");
        m.CreateIndex("IX_CounselingPolicyChanges_TenantId_CampusId", "CounselingPolicyChanges",
            new[] { "TenantId", "CampusId" }, unique: true,
            filter: "[State] = N'Submitted' AND [IsDeleted] = 0");
        m.CreateIndex("IX_CounselingPolicyChanges_ProposedByUserId", "CounselingPolicyChanges", "ProposedByUserId");
        m.CreateIndex("IX_CounselingPolicyChanges_ReviewedByUserId", "CounselingPolicyChanges", "ReviewedByUserId");
    }

    protected override void Down(MigrationBuilder m)
    {
        m.Sql("IF EXISTS (SELECT 1 FROM dbo.CounselingCampusPolicies) OR " +
            "EXISTS (SELECT 1 FROM dbo.CounselingPolicyChanges) OR " +
            "EXISTS (SELECT 1 FROM dbo.CounselingCases WHERE RedactedAtUtc IS NOT NULL OR RedactionRequestId IS NOT NULL) " +
            "THROW 51033, 'Cannot drop active counseling privacy policies or redaction evidence.', 1;");
        m.DropTable("CounselingPolicyChanges");
        m.DropTable("CounselingCampusPolicies");
        m.DropForeignKey("FK_CounselingCases_AspNetUsers_RedactionRequestedByUserId", "CounselingCases");
        m.DropForeignKey("FK_CounselingCases_AspNetUsers_RedactedByUserId", "CounselingCases");
        m.DropIndex("IX_CounselingCases_RedactionRequestedByUserId", "CounselingCases");
        m.DropIndex("IX_CounselingCases_RedactedByUserId", "CounselingCases");
        foreach (var name in new[] { "RedactionRequestId", "RedactionRequestedByUserId",
            "RedactionRequestedAtUtc", "RedactedAtUtc", "RedactedByUserId" })
            m.DropColumn(name, "CounselingCases");
    }
}
