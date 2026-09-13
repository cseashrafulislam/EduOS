using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260908010000_AddAdmissionEnrollmentWorkflow")]
public partial class AddAdmissionEnrollmentWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "AcademicTermId", table: "Enrollments", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<long>(name: "CampusId", table: "Enrollments", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<byte[]>(name: "RowVersion", table: "Enrollments", type: "rowversion", rowVersion: true, nullable: false);

        migrationBuilder.AddColumn<Guid>(name: "PublicId", table: "Guardians", type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()");
        migrationBuilder.AddColumn<string>(name: "NameBangla", table: "Guardians", type: "nvarchar(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<byte[]>(name: "RowVersion", table: "Guardians", type: "rowversion", rowVersion: true, nullable: false);

        migrationBuilder.AddColumn<Guid>(name: "PublicId", table: "Students", type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()");
        migrationBuilder.AddColumn<long>(name: "AdmissionApplicationId", table: "Students", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<string>(name: "FullNameBangla", table: "Students", type: "nvarchar(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<string>(name: "PreferredLanguage", table: "Students", type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "bn-BD");
        migrationBuilder.AddColumn<byte[]>(name: "RowVersion", table: "Students", type: "rowversion", rowVersion: true, nullable: false);

        migrationBuilder.CreateIndex(name: "IX_Enrollments_AcademicTermId", table: "Enrollments", column: "AcademicTermId");
        migrationBuilder.CreateIndex(name: "IX_Enrollments_CampusId", table: "Enrollments", column: "CampusId");
        migrationBuilder.CreateIndex(name: "IX_Enrollments_TenantId_AcademicYearId_ClassId_SectionId_Roll", table: "Enrollments",
            columns: new[] { "TenantId", "AcademicYearId", "ClassId", "SectionId", "Roll" }, unique: true,
            filter: "[IsDeleted] = 0 AND [IsActive] = 1");
        migrationBuilder.CreateIndex(name: "IX_Enrollments_TenantId_StudentId_AcademicYearId", table: "Enrollments",
            columns: new[] { "TenantId", "StudentId", "AcademicYearId" }, unique: true, filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(name: "IX_Guardians_PublicId", table: "Guardians", column: "PublicId", unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex(name: "IX_Guardians_TenantId_Phone", table: "Guardians", columns: new[] { "TenantId", "Phone" });
        migrationBuilder.CreateIndex(name: "IX_Guardians_TenantId_StudentId_IsPrimary", table: "Guardians", columns: new[] { "TenantId", "StudentId", "IsPrimary" }, filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(name: "IX_Students_PublicId", table: "Students", column: "PublicId", unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex(name: "IX_Students_AdmissionApplicationId", table: "Students", column: "AdmissionApplicationId");
        migrationBuilder.CreateIndex(name: "IX_Students_TenantId_AdmissionApplicationId", table: "Students",
            columns: new[] { "TenantId", "AdmissionApplicationId" }, unique: true,
            filter: "[IsDeleted] = 0 AND [AdmissionApplicationId] IS NOT NULL");
        migrationBuilder.CreateIndex(name: "IX_Students_TenantId_AcademicYearId_ClassId_SectionId_Roll", table: "Students",
            columns: new[] { "TenantId", "AcademicYearId", "ClassId", "SectionId", "Roll" }, unique: true,
            filter: "[IsDeleted] = 0 AND [IsActive] = 1");
        migrationBuilder.CreateIndex(name: "IX_Students_TenantId_StudentCode", table: "Students",
            columns: new[] { "TenantId", "StudentCode" }, unique: true, filter: "[IsDeleted] = 0");

        migrationBuilder.AddForeignKey(name: "FK_Enrollments_AcademicTerms_AcademicTermId", table: "Enrollments", column: "AcademicTermId",
            principalTable: "AcademicTerms", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_Enrollments_Campuses_CampusId", table: "Enrollments", column: "CampusId",
            principalTable: "Campuses", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_Students_AdmissionApplicants_AdmissionApplicationId", table: "Students", column: "AdmissionApplicationId",
            principalTable: "AdmissionApplicants", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_Enrollments_AcademicTerms_AcademicTermId", table: "Enrollments");
        migrationBuilder.DropForeignKey(name: "FK_Enrollments_Campuses_CampusId", table: "Enrollments");
        migrationBuilder.DropForeignKey(name: "FK_Students_AdmissionApplicants_AdmissionApplicationId", table: "Students");
        migrationBuilder.DropIndex(name: "IX_Enrollments_AcademicTermId", table: "Enrollments");
        migrationBuilder.DropIndex(name: "IX_Enrollments_CampusId", table: "Enrollments");
        migrationBuilder.DropIndex(name: "IX_Enrollments_TenantId_AcademicYearId_ClassId_SectionId_Roll", table: "Enrollments");
        migrationBuilder.DropIndex(name: "IX_Enrollments_TenantId_StudentId_AcademicYearId", table: "Enrollments");
        migrationBuilder.DropIndex(name: "IX_Guardians_PublicId", table: "Guardians");
        migrationBuilder.DropIndex(name: "IX_Guardians_TenantId_Phone", table: "Guardians");
        migrationBuilder.DropIndex(name: "IX_Guardians_TenantId_StudentId_IsPrimary", table: "Guardians");
        migrationBuilder.DropIndex(name: "IX_Students_PublicId", table: "Students");
        migrationBuilder.DropIndex(name: "IX_Students_AdmissionApplicationId", table: "Students");
        migrationBuilder.DropIndex(name: "IX_Students_TenantId_AdmissionApplicationId", table: "Students");
        migrationBuilder.DropIndex(name: "IX_Students_TenantId_AcademicYearId_ClassId_SectionId_Roll", table: "Students");
        migrationBuilder.DropIndex(name: "IX_Students_TenantId_StudentCode", table: "Students");
        migrationBuilder.DropColumn(name: "AcademicTermId", table: "Enrollments");
        migrationBuilder.DropColumn(name: "CampusId", table: "Enrollments");
        migrationBuilder.DropColumn(name: "RowVersion", table: "Enrollments");
        migrationBuilder.DropColumn(name: "PublicId", table: "Guardians");
        migrationBuilder.DropColumn(name: "NameBangla", table: "Guardians");
        migrationBuilder.DropColumn(name: "RowVersion", table: "Guardians");
        migrationBuilder.DropColumn(name: "PublicId", table: "Students");
        migrationBuilder.DropColumn(name: "AdmissionApplicationId", table: "Students");
        migrationBuilder.DropColumn(name: "FullNameBangla", table: "Students");
        migrationBuilder.DropColumn(name: "PreferredLanguage", table: "Students");
        migrationBuilder.DropColumn(name: "RowVersion", table: "Students");
    }
}
