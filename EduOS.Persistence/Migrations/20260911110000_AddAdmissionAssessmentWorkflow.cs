using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260911110000_AddAdmissionAssessmentWorkflow")]
public partial class AddAdmissionAssessmentWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AdmissionTests",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                AcademicYearId = table.Column<long>(type: "bigint", nullable: false),
                CampusId = table.Column<long>(type: "bigint", nullable: false),
                AcademicUnitId = table.Column<long>(type: "bigint", nullable: false),
                TestDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                TotalMarks = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                PassMarks = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                Venue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                DurationMinutes = table.Column<int>(type: "int", nullable: false),
                IsPublished = table.Column<bool>(type: "bit", nullable: false),
                PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                PublishedByUserId = table.Column<long>(type: "bigint", nullable: true),
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
                table.PrimaryKey("PK_AdmissionTests", x => x.Id);
                table.ForeignKey("FK_AdmissionTests_AcademicYears_AcademicYearId", x => x.AcademicYearId, "AcademicYears", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AdmissionTests_Campuses_CampusId", x => x.CampusId, "Campuses", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AdmissionTests_Classes_AcademicUnitId", x => x.AcademicUnitId, "Classes", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AdmissionTests_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "AdmissionResults",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                AdmissionTestId = table.Column<long>(type: "bigint", nullable: false),
                ApplicantId = table.Column<long>(type: "bigint", nullable: false),
                ObtainedMarks = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                Percentage = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: false),
                IsPassed = table.Column<bool>(type: "bit", nullable: false),
                MeritPosition = table.Column<int>(type: "int", nullable: true),
                ResultStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Grade = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
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
                table.PrimaryKey("PK_AdmissionResults", x => x.Id);
                table.ForeignKey("FK_AdmissionResults_AdmissionApplicants_ApplicantId", x => x.ApplicantId, "AdmissionApplicants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AdmissionResults_AdmissionTests_AdmissionTestId", x => x.AdmissionTestId, "AdmissionTests", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AdmissionResults_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_AdmissionTests_AcademicUnitId", "AdmissionTests", "AcademicUnitId");
        migrationBuilder.CreateIndex("IX_AdmissionTests_AcademicYearId", "AdmissionTests", "AcademicYearId");
        migrationBuilder.CreateIndex("IX_AdmissionTests_CampusId", "AdmissionTests", "CampusId");
        migrationBuilder.CreateIndex("IX_AdmissionTests_TenantId_AcademicYearId_CampusId_AcademicUnitId_TestDate", "AdmissionTests", new[] { "TenantId", "AcademicYearId", "CampusId", "AcademicUnitId", "TestDate" });

        migrationBuilder.CreateIndex("IX_AdmissionResults_AdmissionTestId", "AdmissionResults", "AdmissionTestId");
        migrationBuilder.CreateIndex("IX_AdmissionResults_ApplicantId", "AdmissionResults", "ApplicantId");
        migrationBuilder.CreateIndex("IX_AdmissionResults_TenantId_AdmissionTestId_MeritPosition", "AdmissionResults", new[] { "TenantId", "AdmissionTestId", "MeritPosition" });
        migrationBuilder.CreateIndex(
            name: "IX_AdmissionResults_TenantId_AdmissionTestId_ApplicantId",
            table: "AdmissionResults",
            columns: new[] { "TenantId", "AdmissionTestId", "ApplicantId" },
            unique: true,
            filter: "[IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AdmissionResults");
        migrationBuilder.DropTable(name: "AdmissionTests");
    }
}
