using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>
/// Drop the obsolete default-section marker after the previous cleanup migration.
/// Referenced generated sections require explicit reconciliation, never silent deletion.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261113040000_RemoveAcademicSectionLegacyDefault")]
public sealed class RemoveAcademicSectionLegacyDefault : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1 FROM dbo.AcademicSections AS s
    WHERE s.IsLegacyDefault = 1
)
    THROW 51042, 'Cannot remove legacy section marker while generated sections remain. Reconcile their linked enrollments, offerings, attendance, results and fees before retrying.', 1;
");
        migrationBuilder.DropIndex(
            name: "UX_AcademicSections_LegacyDefault",
            table: "AcademicSections");
        migrationBuilder.DropColumn(
            name: "IsLegacyDefault",
            table: "AcademicSections");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsLegacyDefault",
            table: "AcademicSections",
            type: "bit",
            nullable: false,
            defaultValue: false);
        migrationBuilder.CreateIndex(
            name: "UX_AcademicSections_LegacyDefault",
            table: "AcademicSections",
            columns: new[] { "TenantId", "AcademicBatchId" },
            unique: true,
            filter: "[IsLegacyDefault] = 1 AND [IsDeleted] = 0");
    }
}
