using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>Only curated public HTTPS metadata may cross institutional boundaries;
/// private LMS assets remain tenant-owned and are never publicly served.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261109040000_TeacherPublicSourceLinks")]
public sealed class TeacherPublicSourceLinks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_TeacherResources_Scope", "TeacherResources");
        migrationBuilder.AddCheckConstraint("CK_TeacherResources_Scope", "TeacherResources",
            "[ScopeCode] IN (N'Private', N'Campus', N'Institution', N'Public')");
        migrationBuilder.CreateIndex("IX_TeacherResources_ScopeCode_StateCode_PublishedAtUtc",
            "TeacherResources", new[] { "ScopeCode", "StateCode", "PublishedAtUtc" },
            filter: "[IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A rollback must not silently alter published resource visibility.
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM dbo.TeacherResources WHERE ScopeCode = N'Public') THROW 51041, 'Withdraw or migrate public resource links before rolling back.', 1;");
        migrationBuilder.DropIndex("IX_TeacherResources_ScopeCode_StateCode_PublishedAtUtc", "TeacherResources");
        migrationBuilder.DropCheckConstraint("CK_TeacherResources_Scope", "TeacherResources");
        migrationBuilder.AddCheckConstraint("CK_TeacherResources_Scope", "TeacherResources",
            "[ScopeCode] IN (N'Private', N'Campus', N'Institution')");
    }
}
