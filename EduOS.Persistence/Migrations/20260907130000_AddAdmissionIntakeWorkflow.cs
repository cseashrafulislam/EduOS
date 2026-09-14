using System;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260907130000_AddAdmissionIntakeWorkflow")]
public partial class AddAdmissionIntakeWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AdmissionApplicants",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ApplicationNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                AcademicYearId = table.Column<long>(type: "bigint", nullable: false),
                AcademicTermId = table.Column<long>(type: "bigint", nullable: true),
                CampusId = table.Column<long>(type: "bigint", nullable: false),
                AcademicUnitId = table.Column<long>(type: "bigint", nullable: false),
                ApplicantName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                ApplicantNameBangla = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                DateOfBirth = table.Column<DateTime>(type: "date", nullable: false),
                Gender = table.Column<int>(type: "int", nullable: false),
                PrimaryMobile = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                GuardianName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                GuardianRelation = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                GuardianMobile = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                PresentAddress = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                PermanentAddress = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                PreviousInstitution = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                PreferredLanguage = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                ReviewedByUserId = table.Column<long>(type: "bigint", nullable: true),
                DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                table.PrimaryKey("PK_AdmissionApplicants", x => x.Id);
                table.ForeignKey("FK_AdmissionApplicants_AcademicTerms_AcademicTermId", x => x.AcademicTermId, "AcademicTerms", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AdmissionApplicants_AcademicYears_AcademicYearId", x => x.AcademicYearId, "AcademicYears", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AdmissionApplicants_Campuses_CampusId", x => x.CampusId, "Campuses", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AdmissionApplicants_Classes_AcademicUnitId", x => x.AcademicUnitId, "Classes", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AdmissionApplicants_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_AdmissionApplicants_AcademicTermId", "AdmissionApplicants", "AcademicTermId");
        migrationBuilder.CreateIndex("IX_AdmissionApplicants_AcademicUnitId", "AdmissionApplicants", "AcademicUnitId");
        migrationBuilder.CreateIndex("IX_AdmissionApplicants_AcademicYearId", "AdmissionApplicants", "AcademicYearId");
        migrationBuilder.CreateIndex("IX_AdmissionApplicants_CampusId", "AdmissionApplicants", "CampusId");
        migrationBuilder.CreateIndex("IX_AdmissionApplicants_PublicId", "AdmissionApplicants", "PublicId", unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex("IX_AdmissionApplicants_TenantId_AcademicYearId_AcademicUnitId", "AdmissionApplicants", new[] { "TenantId", "AcademicYearId", "AcademicUnitId" });
        migrationBuilder.CreateIndex("IX_AdmissionApplicants_TenantId_ApplicationNumber", "AdmissionApplicants", new[] { "TenantId", "ApplicationNumber" }, unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex("IX_AdmissionApplicants_TenantId_ClientRequestId", "AdmissionApplicants", new[] { "TenantId", "ClientRequestId" }, unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex("IX_AdmissionApplicants_TenantId_Status_SubmittedAtUtc", "AdmissionApplicants", new[] { "TenantId", "Status", "SubmittedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AdmissionApplicants");
    }
}
