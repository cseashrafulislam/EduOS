using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// Additive LMS learner-consent history. Existing certificates and guardian links remain
/// unchanged. No grants are backfilled: disclosure defaults to denied.
/// Rollback removes consent history and requires prior export/retention review.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261030030000_LmsCertificateGuardianGrants")]
public sealed class LmsCertificateGuardianGrants : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LmsCertificateGuardianGrants",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CertificateId = table.Column<long>(type: "bigint", nullable: false),
                StudentGuardianId = table.Column<long>(type: "bigint", nullable: false),
                GrantedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                GrantedByUserId = table.Column<long>(type: "bigint", nullable: false),
                RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                RevokedByUserId = table.Column<long>(type: "bigint", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LmsCertificateGuardianGrants", x => x.Id);
                table.UniqueConstraint("AK_LmsCertificateGuardianGrants_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_LmsCertificateGuardianGrants_Revocation",
                    "([RevokedAt] IS NULL AND [RevokedByUserId] IS NULL) OR ([RevokedAt] IS NOT NULL AND [RevokedByUserId] IS NOT NULL)");
                table.ForeignKey("FK_LmsCertificateGuardianGrants_Tenants_TenantId", x => x.TenantId,
                    "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_LmsCertificateGuardianGrants_LmsCompletionCertificates_TenantId_CertificateId",
                    x => new { x.TenantId, x.CertificateId }, "LmsCompletionCertificates",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_LmsCertificateGuardianGrants_StudentGuardians_TenantId_StudentGuardianId",
                    x => new { x.TenantId, x.StudentGuardianId }, "StudentGuardians",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_LmsCertificateGuardianGrants_PublicId",
            "LmsCertificateGuardianGrants", "PublicId", unique: true);
        migrationBuilder.CreateIndex("IX_LmsCertificateGuardianGrants_TenantId_ClientRequestId",
            "LmsCertificateGuardianGrants", new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_LmsCertificateGuardianGrants_TenantId_CertificateId_StudentGuardianId",
            "LmsCertificateGuardianGrants", new[] { "TenantId", "CertificateId", "StudentGuardianId" },
            unique: true, filter: "[RevokedAt] IS NULL AND [IsDeleted] = 0");
        migrationBuilder.CreateIndex("IX_LmsCertificateGuardianGrants_TenantId_StudentGuardianId_RevokedAt",
            "LmsCertificateGuardianGrants", new[] { "TenantId", "StudentGuardianId", "RevokedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("LmsCertificateGuardianGrants");
}
