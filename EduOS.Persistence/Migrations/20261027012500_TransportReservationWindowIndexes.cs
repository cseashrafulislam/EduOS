using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>
/// Additive filtered date-window indexes for canonical Transport aggregates.
/// No identity, status, date or financial data is rewritten.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261027012500_TransportReservationWindowIndexes")]
public sealed class TransportReservationWindowIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_StudentTransports_TenantId_StudentId_StartDate_EndDate",
            table: "StudentTransports",
            columns: new[] { "TenantId", "StudentId", "StartDate", "EndDate" },
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_StudentTransports_TenantId_VehicleId_StartDate_EndDate",
            table: "StudentTransports",
            columns: new[] { "TenantId", "VehicleId", "StartDate", "EndDate" },
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_RouteVehicleAssignments_TenantId_VehicleId_EffectiveFrom_EffectiveTo",
            table: "RouteVehicleAssignments",
            columns: new[] { "TenantId", "VehicleId", "EffectiveFrom", "EffectiveTo" },
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_VehicleDriverAssignments_TenantId_TransportDriverId_EffectiveFrom_EffectiveTo",
            table: "VehicleDriverAssignments",
            columns: new[] { "TenantId", "TransportDriverId", "EffectiveFrom", "EffectiveTo" },
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_VehicleDriverAssignments_TenantId_VehicleId_EffectiveFrom_EffectiveTo",
            table: "VehicleDriverAssignments",
            columns: new[] { "TenantId", "VehicleId", "EffectiveFrom", "EffectiveTo" },
            filter: "[IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_StudentTransports_TenantId_StudentId_StartDate_EndDate", "StudentTransports");
        migrationBuilder.DropIndex("IX_StudentTransports_TenantId_VehicleId_StartDate_EndDate", "StudentTransports");
        migrationBuilder.DropIndex("IX_RouteVehicleAssignments_TenantId_VehicleId_EffectiveFrom_EffectiveTo", "RouteVehicleAssignments");
        migrationBuilder.DropIndex("IX_VehicleDriverAssignments_TenantId_TransportDriverId_EffectiveFrom_EffectiveTo", "VehicleDriverAssignments");
        migrationBuilder.DropIndex("IX_VehicleDriverAssignments_TenantId_VehicleId_EffectiveFrom_EffectiveTo", "VehicleDriverAssignments");
    }
}
