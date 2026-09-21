using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260921190000_AddCanonicalStudentEnrollment")]
public partial class AddCanonicalStudentEnrollment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "StudentEnrollments",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StudentId = table.Column<long>(type: "bigint", nullable: false),
                CampusId = table.Column<long>(type: "bigint", nullable: false),
                AcademicYearId = table.Column<long>(type: "bigint", nullable: false),
                AcademicTermId = table.Column<long>(type: "bigint", nullable: true),
                AcademicProgramId = table.Column<long>(type: "bigint", nullable: false),
                AcademicLevelId = table.Column<long>(type: "bigint", nullable: false),
                AcademicBatchId = table.Column<long>(type: "bigint", nullable: false),
                AcademicCurriculumId = table.Column<long>(type: "bigint", nullable: false),
                MediumId = table.Column<long>(type: "bigint", nullable: true),
                ShiftId = table.Column<long>(type: "bigint", nullable: true),
                AcademicTrackId = table.Column<long>(type: "bigint", nullable: true),
                RollNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                EnrollmentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                EnrollmentStatus = table.Column<int>(type: "int", nullable: false),
                IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StudentEnrollments", x => x.Id);
                table.ForeignKey("FK_StudentEnrollments_AcademicBatches_AcademicBatchId", x => x.AcademicBatchId, "AcademicBatches", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentEnrollments_AcademicCurriculums_AcademicCurriculumId", x => x.AcademicCurriculumId, "AcademicCurriculums", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentEnrollments_AcademicLevels_AcademicLevelId", x => x.AcademicLevelId, "AcademicLevels", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentEnrollments_AcademicPrograms_AcademicProgramId", x => x.AcademicProgramId, "AcademicPrograms", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentEnrollments_AcademicTerms_AcademicTermId", x => x.AcademicTermId, "AcademicTerms", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentEnrollments_AcademicTracks_AcademicTrackId", x => x.AcademicTrackId, "AcademicTracks", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentEnrollments_AcademicYears_AcademicYearId", x => x.AcademicYearId, "AcademicYears", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentEnrollments_Campuses_CampusId", x => x.CampusId, "Campuses", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentEnrollments_Mediums_MediumId", x => x.MediumId, "Mediums", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentEnrollments_Shifts_ShiftId", x => x.ShiftId, "Shifts", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentEnrollments_Students_StudentId", x => x.StudentId, "Students", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentEnrollments_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "StudentSubjectRegistrations",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                StudentEnrollmentId = table.Column<long>(type: "bigint", nullable: false),
                StudentId = table.Column<long>(type: "bigint", nullable: false),
                AcademicYearId = table.Column<long>(type: "bigint", nullable: false),
                AcademicTermId = table.Column<long>(type: "bigint", nullable: true),
                AcademicBatchId = table.Column<long>(type: "bigint", nullable: false),
                AcademicCurriculumId = table.Column<long>(type: "bigint", nullable: false),
                CurriculumSubjectId = table.Column<long>(type: "bigint", nullable: false),
                SubjectId = table.Column<long>(type: "bigint", nullable: false),
                IsRequired = table.Column<bool>(type: "bit", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                DecidedByUserId = table.Column<long>(type: "bigint", nullable: true),
                FullMarksSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                PassMarksSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                CreditHoursSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                SubjectCodeSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                SubjectNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StudentSubjectRegistrations", x => x.Id);
                table.ForeignKey("FK_StudentSubjectRegistrations_AcademicBatches_AcademicBatchId", x => x.AcademicBatchId, "AcademicBatches", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentSubjectRegistrations_AcademicCurriculums_AcademicCurriculumId", x => x.AcademicCurriculumId, "AcademicCurriculums", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentSubjectRegistrations_AcademicTerms_AcademicTermId", x => x.AcademicTermId, "AcademicTerms", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentSubjectRegistrations_AcademicYears_AcademicYearId", x => x.AcademicYearId, "AcademicYears", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentSubjectRegistrations_CurriculumSubjects_CurriculumSubjectId", x => x.CurriculumSubjectId, "CurriculumSubjects", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentSubjectRegistrations_StudentEnrollments_StudentEnrollmentId", x => x.StudentEnrollmentId, "StudentEnrollments", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentSubjectRegistrations_Students_StudentId", x => x.StudentId, "Students", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentSubjectRegistrations_Subjects_SubjectId", x => x.SubjectId, "Subjects", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentSubjectRegistrations_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "IX_StudentEnrollments_AcademicBatchId", table: "StudentEnrollments", column: "AcademicBatchId");
        migrationBuilder.CreateIndex(name: "IX_StudentEnrollments_AcademicCurriculumId", table: "StudentEnrollments", column: "AcademicCurriculumId");
        migrationBuilder.CreateIndex(name: "IX_StudentEnrollments_AcademicLevelId", table: "StudentEnrollments", column: "AcademicLevelId");
        migrationBuilder.CreateIndex(name: "IX_StudentEnrollments_AcademicProgramId", table: "StudentEnrollments", column: "AcademicProgramId");
        migrationBuilder.CreateIndex(name: "IX_StudentEnrollments_AcademicTermId", table: "StudentEnrollments", column: "AcademicTermId");
        migrationBuilder.CreateIndex(name: "IX_StudentEnrollments_AcademicTrackId", table: "StudentEnrollments", column: "AcademicTrackId");
        migrationBuilder.CreateIndex(name: "IX_StudentEnrollments_AcademicYearId", table: "StudentEnrollments", column: "AcademicYearId");
        migrationBuilder.CreateIndex(name: "IX_StudentEnrollments_CampusId", table: "StudentEnrollments", column: "CampusId");
        migrationBuilder.CreateIndex(name: "IX_StudentEnrollments_MediumId", table: "StudentEnrollments", column: "MediumId");
        migrationBuilder.CreateIndex(name: "IX_StudentEnrollments_ShiftId", table: "StudentEnrollments", column: "ShiftId");
        migrationBuilder.CreateIndex(name: "IX_StudentEnrollments_StudentId", table: "StudentEnrollments", column: "StudentId");
        migrationBuilder.CreateIndex(name: "IX_StudentEnrollments_TenantId", table: "StudentEnrollments", column: "TenantId");
        migrationBuilder.CreateIndex(name: "UX_StudentEnrollments_Tenant_Request", table: "StudentEnrollments", columns: new[] { "TenantId", "ClientRequestId" }, unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex(name: "UX_StudentEnrollments_Tenant_CurrentStudent", table: "StudentEnrollments", columns: new[] { "TenantId", "StudentId" }, unique: true, filter: "[IsDeleted] = 0 AND [IsActive] = 1 AND [IsCurrent] = 1");
        migrationBuilder.CreateIndex(name: "UX_StudentEnrollments_Tenant_Batch_Roll", table: "StudentEnrollments", columns: new[] { "TenantId", "AcademicBatchId", "RollNo" }, unique: true, filter: "[IsDeleted] = 0 AND [IsActive] = 1 AND [IsCurrent] = 1");

        migrationBuilder.CreateIndex(name: "IX_StudentSubjectRegistrations_AcademicBatchId", table: "StudentSubjectRegistrations", column: "AcademicBatchId");
        migrationBuilder.CreateIndex(name: "IX_StudentSubjectRegistrations_AcademicCurriculumId", table: "StudentSubjectRegistrations", column: "AcademicCurriculumId");
        migrationBuilder.CreateIndex(name: "IX_StudentSubjectRegistrations_AcademicTermId", table: "StudentSubjectRegistrations", column: "AcademicTermId");
        migrationBuilder.CreateIndex(name: "IX_StudentSubjectRegistrations_AcademicYearId", table: "StudentSubjectRegistrations", column: "AcademicYearId");
        migrationBuilder.CreateIndex(name: "IX_StudentSubjectRegistrations_CurriculumSubjectId", table: "StudentSubjectRegistrations", column: "CurriculumSubjectId");
        migrationBuilder.CreateIndex(name: "IX_StudentSubjectRegistrations_StudentEnrollmentId", table: "StudentSubjectRegistrations", column: "StudentEnrollmentId");
        migrationBuilder.CreateIndex(name: "IX_StudentSubjectRegistrations_StudentId", table: "StudentSubjectRegistrations", column: "StudentId");
        migrationBuilder.CreateIndex(name: "IX_StudentSubjectRegistrations_SubjectId", table: "StudentSubjectRegistrations", column: "SubjectId");
        migrationBuilder.CreateIndex(name: "IX_StudentSubjectRegistrations_TenantId", table: "StudentSubjectRegistrations", column: "TenantId");
        migrationBuilder.CreateIndex(name: "IX_StudentSubjectRegistrations_TenantId_StudentId_Status", table: "StudentSubjectRegistrations", columns: new[] { "TenantId", "StudentId", "Status" });
        migrationBuilder.CreateIndex(name: "UX_StudentSubjectRegistrations_Tenant_Request", table: "StudentSubjectRegistrations", columns: new[] { "TenantId", "ClientRequestId" }, unique: true, filter: "[IsDeleted] = 0 AND [ClientRequestId] IS NOT NULL");
        migrationBuilder.CreateIndex(name: "UX_StudentSubjectRegistrations_Tenant_Enrollment_Subject", table: "StudentSubjectRegistrations", columns: new[] { "TenantId", "StudentEnrollmentId", "SubjectId" }, unique: true, filter: "[IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "StudentSubjectRegistrations");
        migrationBuilder.DropTable(name: "StudentEnrollments");
    }
}
