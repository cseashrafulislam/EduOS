using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>Non-destructive study metadata and user bookmarks over canonical reviewed Documents.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261109010000_BangladeshStudyCollections")]
public partial class BangladeshStudyCollections : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<string>("ContentExamCode", "Documents", "nvarchar(24)", maxLength: 24, nullable: true);
        m.AddColumn<string>("ContentBoardCode", "Documents", "nvarchar(40)", maxLength: 40, nullable: true);
        m.AddColumn<int>("ContentSyllabusYear", "Documents", "int", nullable: true);
        m.AddColumn<string>("ContentSyllabusVersion", "Documents", "nvarchar(50)", maxLength: 50, nullable: true);
        m.AddColumn<string>("ContentSyllabusSourceUrl", "Documents", "nvarchar(1000)", maxLength: 1000, nullable: true);
        m.AddColumn<int>("ContentPreviousPaperYear", "Documents", "int", nullable: true);
        m.AddColumn<string>("ContentStudyExplanation", "Documents", "nvarchar(2000)", maxLength: 2000, nullable: true);
        m.CreateIndex("IX_Documents_TenantId_ContentExamCode_ContentBoardCode_ContentSyllabusYear_IsActive",
            "Documents", new[] { "TenantId", "ContentExamCode", "ContentBoardCode", "ContentSyllabusYear", "IsActive" });
        m.CreateTable("StudyBookmarks", columns: t => new
        {
            Id = t.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = t.Column<long>("bigint", nullable: false),
            UserId = t.Column<long>("bigint", nullable: false),
            DocumentId = t.Column<long>("bigint", nullable: false),
            CreatedAt = t.Column<DateTime>("datetime2", nullable: false),
            CreatedBy = t.Column<long>("bigint", nullable: true),
            UpdatedAt = t.Column<DateTime>("datetime2", nullable: true),
            UpdatedBy = t.Column<long>("bigint", nullable: true),
            IsDeleted = t.Column<bool>("bit", nullable: false),
            DeletedAt = t.Column<DateTime>("datetime2", nullable: true),
            DeletedBy = t.Column<long>("bigint", nullable: true),
            RowVersion = t.Column<byte[]>("rowversion", rowVersion: true, nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_StudyBookmarks", x => x.Id);
            t.UniqueConstraint("AK_StudyBookmarks_TenantId_Id", x => new { x.TenantId, x.Id });
            t.ForeignKey("FK_StudyBookmarks_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_StudyBookmarks_AspNetUsers_UserId", x => x.UserId,
                "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            t.ForeignKey("FK_StudyBookmarks_Documents_TenantId_DocumentId", x =>
                new { x.TenantId, x.DocumentId }, "Documents", new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_StudyBookmarks_UserId", "StudyBookmarks", "UserId");
        m.CreateIndex("IX_StudyBookmarks_TenantId_DocumentId", "StudyBookmarks",
            new[] { "TenantId", "DocumentId" });
        m.CreateIndex("IX_StudyBookmarks_TenantId_UserId_DocumentId", "StudyBookmarks",
            new[] { "TenantId", "UserId", "DocumentId" }, unique: true);
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("StudyBookmarks");
        m.DropIndex("IX_Documents_TenantId_ContentExamCode_ContentBoardCode_ContentSyllabusYear_IsActive", "Documents");
        foreach (var name in new[] { "ContentExamCode", "ContentBoardCode", "ContentSyllabusYear",
            "ContentSyllabusVersion", "ContentSyllabusSourceUrl", "ContentPreviousPaperYear",
            "ContentStudyExplanation" }) m.DropColumn(name, "Documents");
    }
}
