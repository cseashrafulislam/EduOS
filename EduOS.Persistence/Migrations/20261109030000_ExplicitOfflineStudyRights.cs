using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>Existing licences remain online-only; offline rights require explicit review.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261109030000_ExplicitOfflineStudyRights")]
public sealed class ExplicitOfflineStudyRights : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("ContentOfflineAllowed", "Documents", "bit",
            nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<int>("LastReadPage", "StudyBookmarks", "int",
            nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<Guid>("LastProgressCommandId", "StudyBookmarks", "uniqueidentifier",
            nullable: true);
        migrationBuilder.AddColumn<DateTime>("ProgressUpdatedAtUtc", "StudyBookmarks", "datetime2",
            nullable: true);
        migrationBuilder.AddCheckConstraint("CK_StudyBookmarks_LastReadPage", "StudyBookmarks",
            "[LastReadPage] >= 0 AND [LastReadPage] <= 100000");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("ContentOfflineAllowed", "Documents");
        migrationBuilder.DropCheckConstraint("CK_StudyBookmarks_LastReadPage", "StudyBookmarks");
        migrationBuilder.DropColumn("LastReadPage", "StudyBookmarks");
        migrationBuilder.DropColumn("LastProgressCommandId", "StudyBookmarks");
        migrationBuilder.DropColumn("ProgressUpdatedAtUtc", "StudyBookmarks");
    }
}
