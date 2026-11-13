using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>
/// Retain an efficient tenant-scoped lookup for the optional enrollment batch FK.
/// This is additive and does not rewrite enrollment rows or roll assignments.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261113200000_AcademicBatchEnrollmentLookupIndex")]
public sealed class AcademicBatchEnrollmentLookupIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_StudentEnrollments_TenantId_AcademicBatchId",
            table: "StudentEnrollments",
            columns: new[] { "TenantId", "AcademicBatchId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_StudentEnrollments_TenantId_AcademicBatchId",
            table: "StudentEnrollments");
    }
}
