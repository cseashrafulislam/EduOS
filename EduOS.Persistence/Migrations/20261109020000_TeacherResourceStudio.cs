using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>Additive, tenant/campus-scoped teacher-authored resources; FileAsset owns all bytes.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261109020000_TeacherResourceStudio")]
public partial class TeacherResourceStudio : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("TeacherResources", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            PublicId = t.Column<Guid>("uniqueidentifier", nullable: false),
            AuthorEmployeeId = t.Column<long>("bigint", nullable: false),
            CampusId = t.Column<long>("bigint", nullable: false),
            FileAssetId = t.Column<long>("bigint", nullable: false),
            LessonPlanId = t.Column<long>("bigint", nullable: true),
            AcademicLevelId = t.Column<long>("bigint", nullable: true),
            SubjectId = t.Column<long>("bigint", nullable: true),
            SupersedesResourceId = t.Column<long>("bigint", nullable: true),
            VersionNo = t.Column<int>("int", nullable: false),
            Title = t.Column<string>("nvarchar(250)", maxLength: 250, nullable: false),
            KindCode = t.Column<string>("nvarchar(24)", maxLength: 24, nullable: false),
            ScopeCode = t.Column<string>("nvarchar(24)", maxLength: 24, nullable: false),
            StateCode = t.Column<string>("nvarchar(24)", maxLength: 24, nullable: false),
            Topic = t.Column<string>("nvarchar(150)", maxLength: 150, nullable: true),
            LanguageCode = t.Column<string>("nvarchar(10)", maxLength: 10, nullable: false),
            SourceUrl = t.Column<string>("nvarchar(1000)", maxLength: 1000, nullable: true),
            RightsHolder = t.Column<string>("nvarchar(200)", maxLength: 200, nullable: false),
            LicenceCode = t.Column<string>("nvarchar(32)", maxLength: 32, nullable: false),
            LicenceEvidence = t.Column<string>("nvarchar(500)", maxLength: 500, nullable: false),
            ExpiresAtUtc = t.Column<DateTime>("datetime2", nullable: true),
            ReviewedByUserId = t.Column<long>("bigint", nullable: true),
            ReviewedAtUtc = t.Column<DateTime>("datetime2", nullable: true),
            ReviewNote = t.Column<string>("nvarchar(500)", maxLength: 500, nullable: true),
            PublishedAtUtc = t.Column<DateTime>("datetime2", nullable: true),
            WithdrawnAtUtc = t.Column<DateTime>("datetime2", nullable: true),
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
            t.PrimaryKey("PK_TeacherResources", x => x.Id);
            t.UniqueConstraint("AK_TeacherResources_TenantId_Id", x => new { x.TenantId, x.Id });
            t.CheckConstraint("CK_TeacherResources_Version", "[VersionNo] >= 1");
            t.CheckConstraint("CK_TeacherResources_State", "[StateCode] IN (N'Draft', N'Submitted', N'Approved', N'Rejected', N'Withdrawn')");
            t.CheckConstraint("CK_TeacherResources_Scope", "[ScopeCode] IN (N'Private', N'Campus', N'Institution')");
            t.CheckConstraint("CK_TeacherResources_Kind", "[KindCode] IN (N'LessonPlan', N'Slide', N'Worksheet', N'Assessment', N'Exemplar', N'Other')");
            t.ForeignKey("FK_TeacherResources_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_TeacherResources_Employees_TenantId_AuthorEmployeeId", x =>
                new { x.TenantId, x.AuthorEmployeeId }, "Employees", new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_TeacherResources_Campuses_TenantId_CampusId", x =>
                new { x.TenantId, x.CampusId }, "Campuses", new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_TeacherResources_FileAssets_TenantId_FileAssetId", x =>
                new { x.TenantId, x.FileAssetId }, "FileAssets", new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_TeacherResources_LessonPlans_TenantId_LessonPlanId", x =>
                new { x.TenantId, x.LessonPlanId }, "LessonPlans", new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_TeacherResources_AcademicLevels_TenantId_AcademicLevelId", x =>
                new { x.TenantId, x.AcademicLevelId }, "AcademicLevels", new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_TeacherResources_Subjects_TenantId_SubjectId", x =>
                new { x.TenantId, x.SubjectId }, "Subjects", new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_TeacherResources_TeacherResources_TenantId_SupersedesResourceId", x =>
                new { x.TenantId, x.SupersedesResourceId }, "TeacherResources", new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_TeacherResources_AspNetUsers_ReviewedByUserId", x =>
                x.ReviewedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_TeacherResources_TenantId_PublicId", "TeacherResources",
            new[] { "TenantId", "PublicId" }, unique: true);
        m.CreateIndex("IX_TeacherResources_PublicId", "TeacherResources", "PublicId", unique: true);
        m.CreateIndex("IX_TeacherResources_TenantId_FileAssetId", "TeacherResources",
            new[] { "TenantId", "FileAssetId" }, unique: true);
        m.CreateIndex("IX_TeacherResources_TenantId_SupersedesResourceId", "TeacherResources",
            new[] { "TenantId", "SupersedesResourceId" }, unique: true,
            filter: "[SupersedesResourceId] IS NOT NULL AND [IsDeleted] = 0");
        m.CreateIndex("IX_TeacherResources_TenantId_CampusId_StateCode_CreatedAt", "TeacherResources",
            new[] { "TenantId", "CampusId", "StateCode", "CreatedAt" });
        m.CreateIndex("IX_TeacherResources_TenantId_AuthorEmployeeId_CreatedAt", "TeacherResources",
            new[] { "TenantId", "AuthorEmployeeId", "CreatedAt" });
        m.CreateIndex("IX_TeacherResources_TenantId_LessonPlanId", "TeacherResources",
            new[] { "TenantId", "LessonPlanId" });
        m.CreateIndex("IX_TeacherResources_TenantId_AcademicLevelId", "TeacherResources",
            new[] { "TenantId", "AcademicLevelId" });
        m.CreateIndex("IX_TeacherResources_TenantId_SubjectId", "TeacherResources",
            new[] { "TenantId", "SubjectId" });
        m.CreateIndex("IX_TeacherResources_ReviewedByUserId", "TeacherResources", "ReviewedByUserId");
    }

    protected override void Down(MigrationBuilder m) => m.DropTable("TeacherResources");
}
