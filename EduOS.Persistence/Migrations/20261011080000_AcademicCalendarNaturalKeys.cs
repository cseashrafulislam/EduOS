using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

public partial class AcademicCalendarNaturalKeys : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Report existing conflicts before changing anything, without selecting,
        // mutating or deleting any of the affected educational records.
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM dbo.AcademicCalendarPolicies WHERE IsDeleted = 0
                GROUP BY TenantId, CampusId HAVING COUNT_BIG(*) > 1
            )
                THROW 51071, 'Duplicate calendar policies exist for one institution/campus. Reconcile before migration.', 1;
            IF EXISTS (
                SELECT 1 FROM dbo.AcademicCalendarEvents WHERE IsDeleted = 0
                GROUP BY TenantId, CampusId, AcademicYearId, AcademicTermId, EventType, Title, StartDate, EndDate
                HAVING COUNT_BIG(*) > 1
            )
                THROW 51072, 'Duplicate calendar events exist for the same natural key. Reconcile before migration.', 1;
            """);

        migrationBuilder.DropIndex(
            name: "IX_AcademicCalendarPolicies_TenantId_CampusId",
            table: "AcademicCalendarPolicies");
        migrationBuilder.DropIndex(
            name: "IX_AcademicCalendarEvents_TenantId_CampusId",
            table: "AcademicCalendarEvents");
        migrationBuilder.CreateIndex(
            name: "UX_AcademicCalendarPolicies_Tenant_Campus",
            table: "AcademicCalendarPolicies",
            columns: new[] { "TenantId", "CampusId" },
            unique: true,
            filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex(
            name: "UX_AcademicCalendarEvents_Tenant_NaturalKey",
            table: "AcademicCalendarEvents",
            columns: new[] { "TenantId", "CampusId", "AcademicYearId", "AcademicTermId", "EventType", "Title", "StartDate", "EndDate" },
            unique: true,
            filter: "[IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_AcademicCalendarEvents_Tenant_NaturalKey",
            table: "AcademicCalendarEvents");
        migrationBuilder.DropIndex(
            name: "UX_AcademicCalendarPolicies_Tenant_Campus",
            table: "AcademicCalendarPolicies");
        migrationBuilder.CreateIndex(
            name: "IX_AcademicCalendarEvents_TenantId_CampusId",
            table: "AcademicCalendarEvents",
            columns: new[] { "TenantId", "CampusId" });
        migrationBuilder.CreateIndex(
            name: "IX_AcademicCalendarPolicies_TenantId_CampusId",
            table: "AcademicCalendarPolicies",
            columns: new[] { "TenantId", "CampusId" });
    }
}
