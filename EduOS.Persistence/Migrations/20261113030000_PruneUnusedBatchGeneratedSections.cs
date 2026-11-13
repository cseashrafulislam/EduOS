using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>
/// Do not show automatically generated section placeholders when a batch has never
/// been used. Preserve every default section referenced by existing academic,
/// attendance, exam or financial records. User-created sections are untouched.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261113030000_PruneUnusedBatchGeneratedSections")]
public sealed class PruneUnusedBatchGeneratedSections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
DELETE s
FROM dbo.AcademicSections AS s
WHERE s.IsLegacyDefault = 1
  AND NOT EXISTS (
      SELECT 1 FROM dbo.StudentEnrollments AS x
      WHERE x.TenantId = s.TenantId
        AND (x.AcademicBatchId = s.AcademicBatchId OR x.AcademicSectionId = s.Id))
  AND NOT EXISTS (
      SELECT 1 FROM dbo.SubjectOfferings AS x
      WHERE x.TenantId = s.TenantId
        AND (x.AcademicBatchId = s.AcademicBatchId OR x.AcademicSectionId = s.Id))
  AND NOT EXISTS (
      SELECT 1 FROM dbo.AttendanceSessions AS x
      WHERE x.TenantId = s.TenantId
        AND (x.AcademicBatchId = s.AcademicBatchId OR x.AcademicSectionId = s.Id))
  AND NOT EXISTS (
      SELECT 1 FROM dbo.ResultPublications AS x
      WHERE x.TenantId = s.TenantId
        AND (x.AcademicBatchId = s.AcademicBatchId OR x.AcademicSectionId = s.Id))
  AND NOT EXISTS (
      SELECT 1 FROM dbo.FeeStructures AS x
      WHERE x.TenantId = s.TenantId
        AND (x.AcademicBatchId = s.AcademicBatchId OR x.AcademicSectionId = s.Id));");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // No destructive schema operation to reverse. This migration only
        // removes unreferenced placeholders derived from existing batch data.
        // Never recreate artificial sections during rollback.
    }
}
