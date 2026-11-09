using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20261109120000_GroundedStudyEvidence")]
public sealed class GroundedStudyEvidence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "StudyEvidenceExcerpts",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DocumentId = table.Column<long>(type: "bigint", nullable: false),
                SourceLocator = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                ExcerptText = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false),
                LanguageCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                StateCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                SubmittedByUserId = table.Column<long>(type: "bigint", nullable: false),
                ReviewedByUserId = table.Column<long>(type: "bigint", nullable: true),
                ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                ReviewReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StudyEvidenceExcerpts", x => x.Id);
                table.UniqueConstraint("AK_StudyEvidenceExcerpts_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_StudyEvidenceExcerpts_State",
                    "[StateCode] IN (N'Pending',N'Approved',N'Rejected',N'Revoked')");
                table.ForeignKey("FK_StudyEvidenceExcerpts_Tenants_TenantId", x => x.TenantId,
                    "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudyEvidenceExcerpts_Documents_TenantId_DocumentId",
                    x => new { x.TenantId, x.DocumentId }, "Documents",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_StudyEvidenceExcerpts_PublicId", "StudyEvidenceExcerpts", "PublicId", unique: true);
        migrationBuilder.CreateIndex("IX_StudyEvidenceExcerpts_TenantId_DocumentId_StateCode_LanguageCode_Id",
            "StudyEvidenceExcerpts", new[] { "TenantId", "DocumentId", "StateCode", "LanguageCode", "Id" });
        migrationBuilder.CreateIndex("IX_StudyEvidenceExcerpts_TenantId_DocumentId_SourceLocator",
            "StudyEvidenceExcerpts", new[] { "TenantId", "DocumentId", "SourceLocator" },
            unique: true, filter: "[IsDeleted] = 0 AND [StateCode] IN ('Pending','Approved')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM dbo.StudyEvidenceExcerpts) THROW 51072, 'Study evidence and review history must be retained before rollback.', 1;");
        migrationBuilder.DropTable("StudyEvidenceExcerpts");
    }
}
