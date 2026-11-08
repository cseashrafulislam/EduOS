using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>
/// Additive version-scoped educational and lawful-source metadata. Historical
/// Documents keep their existing verification/access state and nullable fields.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261108223500_EducationDigitalContentReview")]
public partial class EducationDigitalContentReview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>("ContentCurriculumId", "Documents", "bigint", nullable: true);
        migrationBuilder.AddColumn<long>("ContentLevelId", "Documents", "bigint", nullable: true);
        migrationBuilder.AddColumn<long>("ContentSubjectId", "Documents", "bigint", nullable: true);
        migrationBuilder.AddColumn<string>("ContentChapter", "Documents", "nvarchar(150)", maxLength: 150, nullable: true);
        migrationBuilder.AddColumn<string>("ContentTopic", "Documents", "nvarchar(150)", maxLength: 150, nullable: true);
        migrationBuilder.AddColumn<string>("ContentLanguageCode", "Documents", "nvarchar(10)", maxLength: 10, nullable: true);
        migrationBuilder.AddColumn<string>("ContentSourceUrl", "Documents", "nvarchar(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<string>("ContentRightsHolder", "Documents", "nvarchar(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<string>("ContentLicenceCode", "Documents", "nvarchar(32)", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<string>("ContentLicenceEvidence", "Documents", "nvarchar(500)", maxLength: 500, nullable: true);
        migrationBuilder.CreateIndex("IX_Documents_TenantId_ContentCurriculumId", "Documents",
            new[] { "TenantId", "ContentCurriculumId" });
        migrationBuilder.CreateIndex("IX_Documents_TenantId_ContentLevelId", "Documents",
            new[] { "TenantId", "ContentLevelId" });
        migrationBuilder.CreateIndex("IX_Documents_TenantId_ContentSubjectId", "Documents",
            new[] { "TenantId", "ContentSubjectId" });
        migrationBuilder.CreateIndex("IX_Documents_TenantId_ContentCurriculumId_ContentSubjectId_IsActive", "Documents",
            new[] { "TenantId", "ContentCurriculumId", "ContentSubjectId", "IsActive" });
        migrationBuilder.AddForeignKey("FK_Documents_AcademicCurriculums_TenantId_ContentCurriculumId", "Documents",
            new[] { "TenantId", "ContentCurriculumId" }, "AcademicCurriculums",
            new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("FK_Documents_AcademicLevels_TenantId_ContentLevelId", "Documents",
            new[] { "TenantId", "ContentLevelId" }, "AcademicLevels",
            new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("FK_Documents_Subjects_TenantId_ContentSubjectId", "Documents",
            new[] { "TenantId", "ContentSubjectId" }, "Subjects",
            new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_Documents_AcademicCurriculums_TenantId_ContentCurriculumId", "Documents");
        migrationBuilder.DropForeignKey("FK_Documents_AcademicLevels_TenantId_ContentLevelId", "Documents");
        migrationBuilder.DropForeignKey("FK_Documents_Subjects_TenantId_ContentSubjectId", "Documents");
        migrationBuilder.DropIndex("IX_Documents_TenantId_ContentCurriculumId", "Documents");
        migrationBuilder.DropIndex("IX_Documents_TenantId_ContentLevelId", "Documents");
        migrationBuilder.DropIndex("IX_Documents_TenantId_ContentSubjectId", "Documents");
        migrationBuilder.DropIndex("IX_Documents_TenantId_ContentCurriculumId_ContentSubjectId_IsActive", "Documents");
        foreach (var name in new[] { "ContentCurriculumId", "ContentLevelId", "ContentSubjectId",
            "ContentChapter", "ContentTopic", "ContentLanguageCode", "ContentSourceUrl",
            "ContentRightsHolder", "ContentLicenceCode", "ContentLicenceEvidence" })
            migrationBuilder.DropColumn(name, "Documents");
    }
}
