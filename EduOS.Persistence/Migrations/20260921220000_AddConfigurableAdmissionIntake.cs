using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260921220000_AddConfigurableAdmissionIntake")]
public partial class AddConfigurableAdmissionIntake : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AdmissionIntakeForms",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                AcademicYearId = table.Column<long>(type: "bigint", nullable: false),
                AcademicTermId = table.Column<long>(type: "bigint", nullable: true),
                CampusId = table.Column<long>(type: "bigint", nullable: false),
                AcademicUnitId = table.Column<long>(type: "bigint", nullable: false),
                OpensAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                ClosesAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                ApplicationFee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                FieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                DocumentRequirementsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                PublishedByUserId = table.Column<long>(type: "bigint", nullable: true),
                ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                ClosedByUserId = table.Column<long>(type: "bigint", nullable: true),
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
                table.PrimaryKey("PK_AdmissionIntakeForms", x => x.Id);
                table.CheckConstraint("CK_AdmissionIntakeForms_DateRange", "[ClosesAtUtc] > [OpensAtUtc]");
                table.CheckConstraint("CK_AdmissionIntakeForms_Fee", "[ApplicationFee] >= 0");
                table.CheckConstraint("CK_AdmissionIntakeForms_Status", "[Status] >= 1 AND [Status] <= 4");
                table.ForeignKey("FK_AdmissionIntakeForms_AcademicTerms_AcademicTermId", x => x.AcademicTermId, "AcademicTerms", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AdmissionIntakeForms_Classes_AcademicUnitId", x => x.AcademicUnitId, "Classes", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AdmissionIntakeForms_AcademicYears_AcademicYearId", x => x.AcademicYearId, "AcademicYears", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AdmissionIntakeForms_Campuses_CampusId", x => x.CampusId, "Campuses", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AdmissionIntakeForms_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddColumn<long>(name: "AdmissionIntakeFormId", table: "AdmissionApplicants", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<string>(name: "CustomResponsesJson", table: "AdmissionApplicants", type: "nvarchar(max)", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_AdmissionApplicants_AdmissionIntakeFormId", table: "AdmissionApplicants", column: "AdmissionIntakeFormId");
        migrationBuilder.AddForeignKey(name: "FK_AdmissionApplicants_AdmissionIntakeForms_AdmissionIntakeFormId", table: "AdmissionApplicants", column: "AdmissionIntakeFormId", principalTable: "AdmissionIntakeForms", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

        migrationBuilder.CreateTable(
            name: "AdmissionApplicantDocuments",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ApplicantId = table.Column<long>(type: "bigint", nullable: false),
                AdmissionIntakeFormId = table.Column<long>(type: "bigint", nullable: false),
                DocumentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                Sha256 = table.Column<string>(type: "nvarchar(44)", maxLength: 44, nullable: false),
                VerificationStatus = table.Column<int>(type: "int", nullable: false),
                IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                ReviewedByUserId = table.Column<long>(type: "bigint", nullable: true),
                ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                table.PrimaryKey("PK_AdmissionApplicantDocuments", x => x.Id);
                table.CheckConstraint("CK_AdmissionApplicantDocuments_FileSize", "[FileSizeBytes] > 0");
                table.CheckConstraint("CK_AdmissionApplicantDocuments_Status", "[VerificationStatus] >= 1 AND [VerificationStatus] <= 3");
                table.ForeignKey("FK_AdmissionApplicantDocuments_AdmissionApplicants_ApplicantId", x => x.ApplicantId, "AdmissionApplicants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AdmissionApplicantDocuments_AdmissionIntakeForms_AdmissionIntakeFormId", x => x.AdmissionIntakeFormId, "AdmissionIntakeForms", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AdmissionApplicantDocuments_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "IX_AdmissionIntakeForms_AcademicTermId", table: "AdmissionIntakeForms", column: "AcademicTermId");
        migrationBuilder.CreateIndex(name: "IX_AdmissionIntakeForms_AcademicUnitId", table: "AdmissionIntakeForms", column: "AcademicUnitId");
        migrationBuilder.CreateIndex(name: "IX_AdmissionIntakeForms_AcademicYearId", table: "AdmissionIntakeForms", column: "AcademicYearId");
        migrationBuilder.CreateIndex(name: "IX_AdmissionIntakeForms_CampusId", table: "AdmissionIntakeForms", column: "CampusId");
        migrationBuilder.CreateIndex(name: "IX_AdmissionIntakeForms_PublicId", table: "AdmissionIntakeForms", column: "PublicId", unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex(name: "IX_AdmissionIntakeForms_TenantId_ClientRequestId", table: "AdmissionIntakeForms", columns: new[] { "TenantId", "ClientRequestId" }, unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex(name: "IX_AdmissionIntakeForms_TenantId_Code", table: "AdmissionIntakeForms", columns: new[] { "TenantId", "Code" }, unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex(name: "IX_AdmissionIntakeForms_TenantId_Status_OpensAtUtc_ClosesAtUtc", table: "AdmissionIntakeForms", columns: new[] { "TenantId", "Status", "OpensAtUtc", "ClosesAtUtc" });

        migrationBuilder.CreateIndex(name: "IX_AdmissionApplicantDocuments_AdmissionIntakeFormId", table: "AdmissionApplicantDocuments", column: "AdmissionIntakeFormId");
        migrationBuilder.CreateIndex(name: "IX_AdmissionApplicantDocuments_ApplicantId", table: "AdmissionApplicantDocuments", column: "ApplicantId");
        migrationBuilder.CreateIndex(name: "IX_AdmissionApplicantDocuments_PublicId", table: "AdmissionApplicantDocuments", column: "PublicId", unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex(name: "IX_AdmissionApplicantDocuments_TenantId_ClientRequestId", table: "AdmissionApplicantDocuments", columns: new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex(name: "UX_AdmissionApplicantDocuments_CurrentType", table: "AdmissionApplicantDocuments", columns: new[] { "TenantId", "ApplicantId", "DocumentType" }, unique: true, filter: "[IsDeleted] = 0 AND [IsCurrent] = 1");
        migrationBuilder.CreateIndex(name: "IX_AdmissionApplicantDocuments_TenantId_ApplicantId_VerificationStatus_IsCurrent", table: "AdmissionApplicantDocuments", columns: new[] { "TenantId", "ApplicantId", "VerificationStatus", "IsCurrent" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AdmissionApplicantDocuments");
        migrationBuilder.DropForeignKey(name: "FK_AdmissionApplicants_AdmissionIntakeForms_AdmissionIntakeFormId", table: "AdmissionApplicants");
        migrationBuilder.DropIndex(name: "IX_AdmissionApplicants_AdmissionIntakeFormId", table: "AdmissionApplicants");
        migrationBuilder.DropColumn(name: "AdmissionIntakeFormId", table: "AdmissionApplicants");
        migrationBuilder.DropColumn(name: "CustomResponsesJson", table: "AdmissionApplicants");
        migrationBuilder.DropTable(name: "AdmissionIntakeForms");
    }
}
