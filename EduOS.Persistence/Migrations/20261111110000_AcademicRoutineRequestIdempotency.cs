using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>
/// Legacy assignment and timetable rows retain NULL request IDs. New keyed writes
/// are tenant-unique; historical routine entries and their rowversions are untouched.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261111110000_AcademicRoutineRequestIdempotency")]
public sealed class AcademicRoutineRequestIdempotency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "ClientRequestId", table: "InstructorAssignments",
            type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "ClientRequestId", table: "RoutineEntries",
            type: "uniqueidentifier", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_InstructorAssignments_TenantId_ClientRequestId",
            table: "InstructorAssignments", columns: new[] { "TenantId", "ClientRequestId" },
            unique: true, filter: "[ClientRequestId] IS NOT NULL");
        migrationBuilder.CreateIndex(name: "IX_RoutineEntries_TenantId_ClientRequestId",
            table: "RoutineEntries", columns: new[] { "TenantId", "ClientRequestId" },
            unique: true, filter: "[ClientRequestId] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A rollback must not erase request receipts that are needed to prevent replay.
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM dbo.InstructorAssignments WHERE ClientRequestId IS NOT NULL) OR EXISTS (SELECT 1 FROM dbo.RoutineEntries WHERE ClientRequestId IS NOT NULL) THROW 51065, 'Academic routine request receipts must be retained before rollback.', 1;");
        migrationBuilder.DropIndex(name: "IX_InstructorAssignments_TenantId_ClientRequestId", table: "InstructorAssignments");
        migrationBuilder.DropIndex(name: "IX_RoutineEntries_TenantId_ClientRequestId", table: "RoutineEntries");
        migrationBuilder.DropColumn(name: "ClientRequestId", table: "InstructorAssignments");
        migrationBuilder.DropColumn(name: "ClientRequestId", table: "RoutineEntries");
    }
}
