using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

public partial class AdmissionIntegrityGuards : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Fail without changing data if pre-existing active records violate the new rules.
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM dbo.AdmissionIntakeForms
                WHERE IsDeleted = 0
                GROUP BY TenantId, Code
                HAVING COUNT_BIG(*) > 1
            )
                THROW 51041, 'Duplicate admission intake codes exist. Reconcile the affected tenants before migration.', 1;
            IF EXISTS (
                SELECT 1 FROM dbo.AdmissionApplicantDocuments
                WHERE IsDeleted = 0
                GROUP BY TenantId, AdmissionApplicantId, DocumentTypeCode, VersionNo
                HAVING COUNT_BIG(*) > 1
            )
                THROW 51042, 'Duplicate admission document versions exist. Reconcile the affected applicants before migration.', 1;
            IF EXISTS (
                SELECT 1 FROM dbo.AdmissionIntakeForms
                WHERE ClosesAt IS NOT NULL AND OpensAt IS NOT NULL AND ClosesAt < OpensAt
            )
                THROW 51043, 'Admission intake date ranges are invalid. Correct them before migration.', 1;
            IF EXISTS (
                SELECT 1 FROM dbo.AdmissionApplicantDocuments
                WHERE VersionNo < 1
            )
                THROW 51044, 'Invalid admission document version numbers exist. Correct them before migration.', 1;
            """);

        migrationBuilder.CreateIndex(
            name: "UX_AdmissionIntakeForms_Tenant_Code",
            table: "AdmissionIntakeForms",
            columns: new[] { "TenantId", "Code" },
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "UX_AdmissionApplicantDocuments_Tenant_Applicant_Type_Version",
            table: "AdmissionApplicantDocuments",
            columns: new[] { "TenantId", "AdmissionApplicantId", "DocumentTypeCode", "VersionNo" },
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_AdmissionIntakeForms_DateRange",
            table: "AdmissionIntakeForms",
            sql: "[ClosesAt] IS NULL OR [OpensAt] IS NULL OR [ClosesAt] >= [OpensAt]");

        migrationBuilder.AddCheckConstraint(
            name: "CK_AdmissionApplicantDocuments_Version",
            table: "AdmissionApplicantDocuments",
            sql: "[VersionNo] >= 1");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_AdmissionApplicantDocuments_Version",
            table: "AdmissionApplicantDocuments");
        migrationBuilder.DropCheckConstraint(
            name: "CK_AdmissionIntakeForms_DateRange",
            table: "AdmissionIntakeForms");
        migrationBuilder.DropIndex(
            name: "UX_AdmissionApplicantDocuments_Tenant_Applicant_Type_Version",
            table: "AdmissionApplicantDocuments");
        migrationBuilder.DropIndex(
            name: "UX_AdmissionIntakeForms_Tenant_Code",
            table: "AdmissionIntakeForms");
    }
}
