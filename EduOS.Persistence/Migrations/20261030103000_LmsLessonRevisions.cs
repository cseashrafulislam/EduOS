using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Additive LMS revision evidence. Old lessons are baselined on first edit. Rollback destroys revision history and requires retention approval.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261030103000_LmsLessonRevisions")]
public sealed class LmsLessonRevisions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LmsLessonRevisions",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                LessonId = table.Column<long>(type: "bigint", nullable: false),
                CourseId = table.Column<long>(type: "bigint", nullable: false),
                RevisionNo = table.Column<int>(type: "int", nullable: false),
                ChangeKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Content = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                ContentUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CourseSectionId = table.Column<long>(type: "bigint", nullable: true),
                DisplayOrder = table.Column<int>(type: "int", nullable: false),
                IsPublished = table.Column<bool>(type: "bit", nullable: false),
                ResourceSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CapturedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CapturedByUserId = table.Column<long>(type: "bigint", nullable: false),
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
                table.PrimaryKey("PK_LmsLessonRevisions", x => x.Id);
                table.UniqueConstraint("AK_LmsLessonRevisions_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_LmsLessonRevisions_PositiveRevision", "[RevisionNo] > 0");
                table.ForeignKey("FK_LmsLessonRevisions_Tenants_TenantId", x => x.TenantId,
                    "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_LmsLessonRevisions_Lessons_TenantId_LessonId",
                    x => new { x.TenantId, x.LessonId }, "Lessons",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_LmsLessonRevisions_TenantId_LessonId_RevisionNo",
            "LmsLessonRevisions", new[] { "TenantId", "LessonId", "RevisionNo" }, unique: true);
        migrationBuilder.CreateIndex("IX_LmsLessonRevisions_TenantId_LessonId",
            "LmsLessonRevisions", new[] { "TenantId", "LessonId" });
        migrationBuilder.CreateIndex("IX_LmsLessonRevisions_TenantId_CourseId_CapturedAt",
            "LmsLessonRevisions", new[] { "TenantId", "CourseId", "CapturedAt" });
        migrationBuilder.Sql("""
            CREATE TRIGGER [dbo].[TR_LmsLessonRevisions_Immutable]
            ON [dbo].[LmsLessonRevisions] AFTER UPDATE, DELETE
            AS BEGIN
                SET NOCOUNT ON;
                THROW 51010, 'LMS lesson revisions are immutable.', 1;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS [dbo].[TR_LmsLessonRevisions_Immutable]");
        migrationBuilder.DropTable("LmsLessonRevisions");
    }
}
