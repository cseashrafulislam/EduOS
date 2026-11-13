using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>Pre-production Batch optionality with independent Section-based roll and offering constraints.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261113190000_OptionalAcademicBatch")]
public sealed class OptionalAcademicBatch : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_SubjectOfferings_TenantId_AcademicBatchId_Code", "SubjectOfferings");
        migrationBuilder.DropIndex("IX_StudentEnrollments_TenantId_AcademicBatchId_RollNo", "StudentEnrollments");
        migrationBuilder.DropIndex("IX_ResultPublications_TenantId_AssessmentId_AcademicBatchId_VersionNo", "ResultPublications");
        foreach (var table in new[] { "SubjectOfferings", "StudentEnrollments", "AttendanceSessions", "ResultPublications" })
            migrationBuilder.AlterColumn<long>("AcademicBatchId", table, type: "bigint",
                nullable: true, oldClrType: typeof(long), oldType: "bigint");

        migrationBuilder.CreateIndex("IX_SubjectOfferings_TenantId_AcademicBatchId_AcademicYearId_AcademicSectionId_Code",
            "SubjectOfferings", new[] { "TenantId", "AcademicBatchId", "AcademicYearId", "AcademicSectionId", "Code" },
            unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex("IX_SubjectOfferings_TenantId_AcademicBatchId_AcademicYearId_AcademicSectionId_CurriculumSubjectId",
            "SubjectOfferings", new[] { "TenantId", "AcademicBatchId", "AcademicYearId", "AcademicSectionId", "CurriculumSubjectId" },
            unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex("IX_StudentEnrollments_TenantId_AcademicYearId_AcademicSectionId_RollNo",
            "StudentEnrollments", new[] { "TenantId", "AcademicYearId", "AcademicSectionId", "RollNo" },
            unique: true, filter: "[IsCurrent] = 1 AND [IsDeleted] = 0 AND [AcademicSectionId] IS NOT NULL AND [RollNo] <> ''");
        migrationBuilder.CreateIndex("IX_ResultPublications_TenantId_AssessmentId_AcademicBatchId_AcademicSectionId_VersionNo",
            "ResultPublications", new[] { "TenantId", "AssessmentId", "AcademicBatchId", "AcademicSectionId", "VersionNo" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_SubjectOfferings_TenantId_AcademicBatchId_AcademicYearId_AcademicSectionId_Code", "SubjectOfferings");
        migrationBuilder.DropIndex("IX_SubjectOfferings_TenantId_AcademicBatchId_AcademicYearId_AcademicSectionId_CurriculumSubjectId", "SubjectOfferings");
        migrationBuilder.DropIndex("IX_StudentEnrollments_TenantId_AcademicYearId_AcademicSectionId_RollNo", "StudentEnrollments");
        migrationBuilder.DropIndex("IX_ResultPublications_TenantId_AssessmentId_AcademicBatchId_AcademicSectionId_VersionNo", "ResultPublications");
        foreach (var table in new[] { "SubjectOfferings", "StudentEnrollments", "AttendanceSessions", "ResultPublications" })
            migrationBuilder.AlterColumn<long>("AcademicBatchId", table, type: "bigint",
                nullable: false, oldClrType: typeof(long), oldType: "bigint", oldNullable: true);
        migrationBuilder.CreateIndex("IX_SubjectOfferings_TenantId_AcademicBatchId_Code", "SubjectOfferings",
            new[] { "TenantId", "AcademicBatchId", "Code" }, unique: true);
        migrationBuilder.CreateIndex("IX_StudentEnrollments_TenantId_AcademicBatchId_RollNo", "StudentEnrollments",
            new[] { "TenantId", "AcademicBatchId", "RollNo" }, unique: true, filter: "[RollNo] <> ''");
        migrationBuilder.CreateIndex("IX_ResultPublications_TenantId_AssessmentId_AcademicBatchId_VersionNo", "ResultPublications",
            new[] { "TenantId", "AssessmentId", "AcademicBatchId", "VersionNo" }, unique: true);
    }
}
