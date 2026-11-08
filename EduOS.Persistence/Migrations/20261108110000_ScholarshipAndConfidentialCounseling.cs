using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20261108110000_ScholarshipAndConfidentialCounseling")]
public sealed class ScholarshipAndConfidentialCounseling : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("ScholarshipApplications", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            PublicId = t.Column<Guid>("uniqueidentifier", nullable: false),
            ClientRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            StudentEnrollmentId = t.Column<long>("bigint", nullable: false),
            DiscountRuleId = t.Column<long>("bigint", nullable: false),
            EvidenceFileAssetId = t.Column<long>("bigint", nullable: true),
            RenewalOfApplicationId = t.Column<long>("bigint", nullable: true),
            EffectiveFrom = t.Column<DateOnly>("date", nullable: false),
            EffectiveTo = t.Column<DateOnly>("date", nullable: true),
            EligibilityStatement = t.Column<string>("nvarchar(700)", maxLength: 700, nullable: false),
            State = t.Column<string>("nvarchar(20)", maxLength: 20, nullable: false),
            SubmittedByUserId = t.Column<long>("bigint", nullable: false),
            SubmittedAtUtc = t.Column<DateTime>("datetime2", nullable: false),
            ReviewedByUserId = t.Column<long>("bigint", nullable: true),
            ReviewedAtUtc = t.Column<DateTime>("datetime2", nullable: true),
            DecisionReason = t.Column<string>("nvarchar(500)", maxLength: 500, nullable: true),
            StudentDiscountId = t.Column<long>("bigint", nullable: true),
            AwardedAtUtc = t.Column<DateTime>("datetime2", nullable: true),
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
            t.PrimaryKey("PK_ScholarshipApplications", x => x.Id);
            t.UniqueConstraint("AK_ScholarshipApplications_TenantId_Id", x => new { x.TenantId, x.Id });
            t.CheckConstraint("CK_ScholarshipApplications_State",
                "[State] IN ('Submitted','Approved','Rejected','Awarded','Cancelled')");
            t.ForeignKey("FK_ScholarshipApplications_Tenants_TenantId",
                x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipApplications_StudentEnrollments_TenantId_StudentEnrollmentId",
                x => new { x.TenantId, x.StudentEnrollmentId }, "StudentEnrollments",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipApplications_DiscountRules_TenantId_DiscountRuleId",
                x => new { x.TenantId, x.DiscountRuleId }, "DiscountRules",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipApplications_FileAssets_TenantId_EvidenceFileAssetId",
                x => new { x.TenantId, x.EvidenceFileAssetId }, "FileAssets",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipApplications_ScholarshipApplications_TenantId_RenewalOfApplicationId",
                x => new { x.TenantId, x.RenewalOfApplicationId }, "ScholarshipApplications",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipApplications_StudentDiscounts_TenantId_StudentDiscountId",
                x => new { x.TenantId, x.StudentDiscountId }, "StudentDiscounts",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipApplications_AspNetUsers_SubmittedByUserId",
                x => x.SubmittedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipApplications_AspNetUsers_ReviewedByUserId",
                x => x.ReviewedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
        });
        m.CreateTable("CounselingCases", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            PublicId = t.Column<Guid>("uniqueidentifier", nullable: false),
            ClientRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            StudentId = t.Column<long>("bigint", nullable: false),
            CampusId = t.Column<long>("bigint", nullable: false),
            State = t.Column<string>("nvarchar(20)", maxLength: 20, nullable: false),
            IsUrgent = t.Column<bool>("bit", nullable: false),
            OpenedByUserId = t.Column<long>("bigint", nullable: false),
            OpenedAtUtc = t.Column<DateTime>("datetime2", nullable: false),
            AssignedCounselorUserId = t.Column<long>("bigint", nullable: true),
            AssignedByUserId = t.Column<long>("bigint", nullable: true),
            AssignedAtUtc = t.Column<DateTime>("datetime2", nullable: true),
            ClosedAtUtc = t.Column<DateTime>("datetime2", nullable: true),
            ClosedByUserId = t.Column<long>("bigint", nullable: true),
            ProtectedSummary = t.Column<string>("nvarchar(max)", maxLength: 8000, nullable: false),
            GuardianContactConsented = t.Column<bool>("bit", nullable: false),
            GuardianConsentAtUtc = t.Column<DateTime>("datetime2", nullable: true),
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
            t.PrimaryKey("PK_CounselingCases", x => x.Id);
            t.UniqueConstraint("AK_CounselingCases_TenantId_Id", x => new { x.TenantId, x.Id });
            t.CheckConstraint("CK_CounselingCases_State",
                "[State] IN ('Requested','Assigned','Active','Escalated','Closed')");
            t.ForeignKey("FK_CounselingCases_Tenants_TenantId",
                x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CounselingCases_Students_TenantId_StudentId",
                x => new { x.TenantId, x.StudentId }, "Students",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CounselingCases_Campuses_TenantId_CampusId",
                x => new { x.TenantId, x.CampusId }, "Campuses",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CounselingCases_AspNetUsers_OpenedByUserId",
                x => x.OpenedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CounselingCases_AspNetUsers_AssignedCounselorUserId",
                x => x.AssignedCounselorUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CounselingCases_AspNetUsers_AssignedByUserId",
                x => x.AssignedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CounselingCases_AspNetUsers_ClosedByUserId",
                x => x.ClosedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
        });
        m.CreateTable("ScholarshipApplicationEvents", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            ScholarshipApplicationId = t.Column<long>("bigint", nullable: false),
            ClientRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            Action = t.Column<string>("nvarchar(24)", maxLength: 24, nullable: false),
            Reason = t.Column<string>("nvarchar(500)", maxLength: 500, nullable: false),
            ActorUserId = t.Column<long>("bigint", nullable: false),
            OccurredAtUtc = t.Column<DateTime>("datetime2", nullable: false),
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
            t.PrimaryKey("PK_ScholarshipApplicationEvents", x => x.Id);
            t.UniqueConstraint("AK_ScholarshipApplicationEvents_TenantId_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_ScholarshipApplicationEvents_Tenants_TenantId",
                x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipApplicationEvents_ScholarshipApplications_TenantId_ScholarshipApplicationId",
                x => new { x.TenantId, x.ScholarshipApplicationId }, "ScholarshipApplications",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_ScholarshipApplicationEvents_AspNetUsers_ActorUserId",
                x => x.ActorUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
        });
        m.CreateTable("CounselingCaseEvents", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            CounselingCaseId = t.Column<long>("bigint", nullable: false),
            ClientRequestId = t.Column<Guid>("uniqueidentifier", nullable: false),
            Action = t.Column<string>("nvarchar(24)", maxLength: 24, nullable: false),
            ProtectedNote = t.Column<string>("nvarchar(max)", maxLength: 8000, nullable: false),
            AppointmentAtUtc = t.Column<DateTime>("datetime2", nullable: true),
            ActorUserId = t.Column<long>("bigint", nullable: false),
            OccurredAtUtc = t.Column<DateTime>("datetime2", nullable: false),
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
            t.PrimaryKey("PK_CounselingCaseEvents", x => x.Id);
            t.UniqueConstraint("AK_CounselingCaseEvents_TenantId_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_CounselingCaseEvents_Tenants_TenantId",
                x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CounselingCaseEvents_CounselingCases_TenantId_CounselingCaseId",
                x => new { x.TenantId, x.CounselingCaseId }, "CounselingCases",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_CounselingCaseEvents_AspNetUsers_ActorUserId",
                x => x.ActorUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_ScholarshipApplications_PublicId", "ScholarshipApplications", "PublicId", unique: true);
        m.CreateIndex("IX_ScholarshipApplications_TenantId_ClientRequestId", "ScholarshipApplications",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("IX_ScholarshipApplications_TenantId_State_SubmittedAtUtc", "ScholarshipApplications",
            new[] { "TenantId", "State", "SubmittedAtUtc" });
        m.CreateIndex("IX_ScholarshipApplications_TenantId_StudentEnrollmentId_SubmittedAtUtc", "ScholarshipApplications",
            new[] { "TenantId", "StudentEnrollmentId", "SubmittedAtUtc" });
        m.CreateIndex("IX_ScholarshipApplications_TenantId_DiscountRuleId", "ScholarshipApplications",
            new[] { "TenantId", "DiscountRuleId" });
        m.CreateIndex("IX_ScholarshipApplications_TenantId_EvidenceFileAssetId", "ScholarshipApplications",
            new[] { "TenantId", "EvidenceFileAssetId" });
        m.CreateIndex("IX_ScholarshipApplications_TenantId_RenewalOfApplicationId", "ScholarshipApplications",
            new[] { "TenantId", "RenewalOfApplicationId" });
        m.CreateIndex("IX_ScholarshipApplications_TenantId_StudentDiscountId", "ScholarshipApplications",
            new[] { "TenantId", "StudentDiscountId" });
        m.CreateIndex("IX_ScholarshipApplications_SubmittedByUserId", "ScholarshipApplications", "SubmittedByUserId");
        m.CreateIndex("IX_ScholarshipApplications_ReviewedByUserId", "ScholarshipApplications", "ReviewedByUserId");
        m.CreateIndex("IX_ScholarshipApplicationEvents_TenantId_ClientRequestId", "ScholarshipApplicationEvents",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("IX_ScholarshipApplicationEvents_TenantId_ScholarshipApplicationId_Id",
            "ScholarshipApplicationEvents", new[] { "TenantId", "ScholarshipApplicationId", "Id" });
        m.CreateIndex("IX_ScholarshipApplicationEvents_ActorUserId", "ScholarshipApplicationEvents", "ActorUserId");
        m.CreateIndex("IX_CounselingCases_PublicId", "CounselingCases", "PublicId", unique: true);
        m.CreateIndex("IX_CounselingCases_TenantId_ClientRequestId", "CounselingCases",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("IX_CounselingCases_TenantId_CampusId_State_OpenedAtUtc", "CounselingCases",
            new[] { "TenantId", "CampusId", "State", "OpenedAtUtc" });
        m.CreateIndex("IX_CounselingCases_TenantId_AssignedCounselorUserId_State", "CounselingCases",
            new[] { "TenantId", "AssignedCounselorUserId", "State" });
        m.CreateIndex("IX_CounselingCases_TenantId_StudentId", "CounselingCases",
            new[] { "TenantId", "StudentId" });
        m.CreateIndex("IX_CounselingCases_OpenedByUserId", "CounselingCases", "OpenedByUserId");
        m.CreateIndex("IX_CounselingCases_AssignedCounselorUserId", "CounselingCases", "AssignedCounselorUserId");
        m.CreateIndex("IX_CounselingCases_AssignedByUserId", "CounselingCases", "AssignedByUserId");
        m.CreateIndex("IX_CounselingCases_ClosedByUserId", "CounselingCases", "ClosedByUserId");
        m.CreateIndex("IX_CounselingCaseEvents_TenantId_ClientRequestId", "CounselingCaseEvents",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        m.CreateIndex("IX_CounselingCaseEvents_TenantId_CounselingCaseId_Id", "CounselingCaseEvents",
            new[] { "TenantId", "CounselingCaseId", "Id" });
        m.CreateIndex("IX_CounselingCaseEvents_ActorUserId", "CounselingCaseEvents", "ActorUserId");
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("CounselingCaseEvents");
        m.DropTable("ScholarshipApplicationEvents");
        m.DropTable("CounselingCases");
        m.DropTable("ScholarshipApplications");
    }
}
