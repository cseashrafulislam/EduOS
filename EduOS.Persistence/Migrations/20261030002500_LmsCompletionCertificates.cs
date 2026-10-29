using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// Additive LMS-only issue-time certificate history. No existing V1 certificate or LMS
/// enrollment is rewritten. Rollback drops issued history: export/reconcile before rollback.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261030002500_LmsCompletionCertificates")]
public sealed class LmsCompletionCertificates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LmsCompletionCertificates",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CourseEnrollmentId = table.Column<long>(type: "bigint", nullable: false),
                CourseId = table.Column<long>(type: "bigint", nullable: false),
                StudentId = table.Column<long>(type: "bigint", nullable: false),
                Revision = table.Column<int>(type: "int", nullable: false),
                CourseTitleSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                CourseCodeSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                StudentNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                StudentCodeSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                CompletedAtSnapshot = table.Column<DateTime>(type: "datetime2", nullable: false),
                RequiredLessonsSnapshot = table.Column<int>(type: "int", nullable: false),
                CompletedLessonsSnapshot = table.Column<int>(type: "int", nullable: false),
                RequiredAssignmentsSnapshot = table.Column<int>(type: "int", nullable: false),
                GradedAssignmentsSnapshot = table.Column<int>(type: "int", nullable: false),
                RequiredQuizzesSnapshot = table.Column<int>(type: "int", nullable: false),
                PassedQuizzesSnapshot = table.Column<int>(type: "int", nullable: false),
                IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                IssuedByUserId = table.Column<long>(type: "bigint", nullable: false),
                IsRevoked = table.Column<bool>(type: "bit", nullable: false),
                RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                RevokedByUserId = table.Column<long>(type: "bigint", nullable: true),
                RevocationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                table.PrimaryKey("PK_LmsCompletionCertificates", x => x.Id);
                table.UniqueConstraint("AK_LmsCompletionCertificates_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_LmsCompletionCertificates_Revision", "[Revision] >= 1");
                table.CheckConstraint("CK_LmsCompletionCertificates_Revocation",
                    "([IsRevoked] = 0 AND [RevokedAt] IS NULL AND [RevokedByUserId] IS NULL AND [RevocationReason] IS NULL) OR ([IsRevoked] = 1 AND [RevokedAt] IS NOT NULL AND [RevokedByUserId] IS NOT NULL AND [RevocationReason] IS NOT NULL)");
                table.ForeignKey("FK_LmsCompletionCertificates_Tenants_TenantId", x => x.TenantId,
                    "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_LmsCompletionCertificates_CourseEnrollments_TenantId_CourseEnrollmentId",
                    x => new { x.TenantId, x.CourseEnrollmentId }, "CourseEnrollments",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_LmsCompletionCertificates_Courses_TenantId_CourseId",
                    x => new { x.TenantId, x.CourseId }, "Courses",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_LmsCompletionCertificates_Students_TenantId_StudentId",
                    x => new { x.TenantId, x.StudentId }, "Students",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_LmsCompletionCertificates_PublicId", "LmsCompletionCertificates", "PublicId", unique: true);
        migrationBuilder.CreateIndex("IX_LmsCompletionCertificates_TenantId_ClientRequestId", "LmsCompletionCertificates",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_LmsCompletionCertificates_TenantId_CourseEnrollmentId_Revision",
            "LmsCompletionCertificates", new[] { "TenantId", "CourseEnrollmentId", "Revision" }, unique: true);
        migrationBuilder.CreateIndex("IX_LmsCompletionCertificates_TenantId_CourseEnrollmentId",
            "LmsCompletionCertificates", new[] { "TenantId", "CourseEnrollmentId" },
            unique: true, filter: "[IsRevoked] = 0 AND [IsDeleted] = 0");
        migrationBuilder.CreateIndex("IX_LmsCompletionCertificates_TenantId_CourseId_IssuedAt",
            "LmsCompletionCertificates", new[] { "TenantId", "CourseId", "IssuedAt" });
        migrationBuilder.CreateIndex("IX_LmsCompletionCertificates_TenantId_StudentId_IssuedAt",
            "LmsCompletionCertificates", new[] { "TenantId", "StudentId", "IssuedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("LmsCompletionCertificates");
}
