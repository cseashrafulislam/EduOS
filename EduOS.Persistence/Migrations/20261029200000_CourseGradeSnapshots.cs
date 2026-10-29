using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// Additive per-course immutable published evidence. Older publications retain
/// HasCourseGradeSnapshots=false and are never silently reconstructed from
/// mutable current marks. Export new evidence before a rollback.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261029200000_CourseGradeSnapshots")]
public sealed class CourseGradeSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("CountsTowardCgpa", "Assessments", type: "bit",
            nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>("HasCourseGradeSnapshots", "ResultPublications", type: "bit",
            nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<decimal>("CreditHours", "StudentResultSummaries", type: "decimal(18,2)",
            nullable: true);
        migrationBuilder.AddColumn<int>("CourseCount", "StudentResultSummaries", type: "int",
            nullable: true);
        migrationBuilder.CreateTable(
            name: "StudentResultCourseSnapshots",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                ResultPublicationId = table.Column<long>(type: "bigint", nullable: false),
                StudentEnrollmentId = table.Column<long>(type: "bigint", nullable: false),
                AssessmentSubjectId = table.Column<long>(type: "bigint", nullable: false),
                CurriculumSubjectId = table.Column<long>(type: "bigint", nullable: false),
                StudentSubjectRegistrationId = table.Column<long>(type: "bigint", nullable: false),
                FullMarks = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                ObtainedMarks = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                CreditHours = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                GradePoint = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                GradeLetter = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                IsPassed = table.Column<bool>(type: "bit", nullable: false),
                IsWithheld = table.Column<bool>(type: "bit", nullable: false),
                IsAbsent = table.Column<bool>(type: "bit", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StudentResultCourseSnapshots", x => x.Id);
                table.UniqueConstraint("AK_StudentResultCourseSnapshots_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_StudentResultCourseSnapshots_Grade",
                    "[FullMarks] > 0 AND [ObtainedMarks] >= 0 AND [ObtainedMarks] <= [FullMarks] AND [CreditHours] >= 0 AND [GradePoint] >= 0");
                table.ForeignKey("FK_StudentResultCourseSnapshots_Tenants_TenantId", x => x.TenantId,
                    "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentResultCourseSnapshots_ResultPublications_TenantId_ResultPublicationId",
                    x => new { x.TenantId, x.ResultPublicationId }, "ResultPublications",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentResultCourseSnapshots_StudentEnrollments_TenantId_StudentEnrollmentId",
                    x => new { x.TenantId, x.StudentEnrollmentId }, "StudentEnrollments",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentResultCourseSnapshots_AssessmentSubjects_TenantId_AssessmentSubjectId",
                    x => new { x.TenantId, x.AssessmentSubjectId }, "AssessmentSubjects",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentResultCourseSnapshots_CurriculumSubjects_TenantId_CurriculumSubjectId",
                    x => new { x.TenantId, x.CurriculumSubjectId }, "CurriculumSubjects",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentResultCourseSnapshots_StudentSubjectRegistrations_TenantId_StudentSubjectRegistrationId",
                    x => new { x.TenantId, x.StudentSubjectRegistrationId }, "StudentSubjectRegistrations",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_StudentResultCourseSnapshots_TenantId_ResultPublicationId_StudentEnrollmentId_AssessmentSubjectId",
            "StudentResultCourseSnapshots",
            new[] { "TenantId", "ResultPublicationId", "StudentEnrollmentId", "AssessmentSubjectId" }, unique: true);
        migrationBuilder.CreateIndex("IX_StudentResultCourseSnapshots_TenantId_StudentEnrollmentId_CurriculumSubjectId",
            "StudentResultCourseSnapshots", new[] { "TenantId", "StudentEnrollmentId", "CurriculumSubjectId" });
        migrationBuilder.CreateIndex("IX_StudentResultCourseSnapshots_TenantId_AssessmentSubjectId",
            "StudentResultCourseSnapshots", new[] { "TenantId", "AssessmentSubjectId" });
        migrationBuilder.CreateIndex("IX_StudentResultCourseSnapshots_TenantId_CurriculumSubjectId",
            "StudentResultCourseSnapshots", new[] { "TenantId", "CurriculumSubjectId" });
        migrationBuilder.CreateIndex("IX_StudentResultCourseSnapshots_TenantId_StudentSubjectRegistrationId",
            "StudentResultCourseSnapshots", new[] { "TenantId", "StudentSubjectRegistrationId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("StudentResultCourseSnapshots");
        migrationBuilder.DropColumn("CourseCount", "StudentResultSummaries");
        migrationBuilder.DropColumn("CreditHours", "StudentResultSummaries");
        migrationBuilder.DropColumn("HasCourseGradeSnapshots", "ResultPublications");
        migrationBuilder.DropColumn("CountsTowardCgpa", "Assessments");
    }
}
