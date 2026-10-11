using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

public partial class RoutineCollisionIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_RoutineEntries_Tenant_Day_Active_From",
            table: "RoutineEntries",
            columns: new[] { "TenantId", "DayOfWeek", "IsActive", "EffectiveFrom" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_RoutineEntries_Tenant_Day_Active_From",
            table: "RoutineEntries");
    }
}
