using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Additive trip planning and immutable dispatch-time rider snapshot. No existing reservations change.
/// Down removes dispatch history; never run in production without approved retention/export.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261030012000_TransportTripDispatch")]
public sealed class TransportTripDispatch : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TransportTrips",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DispatchRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CampusId = table.Column<long>(type: "bigint", nullable: false),
                RouteId = table.Column<long>(type: "bigint", nullable: false),
                VehicleId = table.Column<long>(type: "bigint", nullable: false),
                TransportDriverId = table.Column<long>(type: "bigint", nullable: true),
                OperatingDate = table.Column<DateOnly>(type: "date", nullable: false),
                Direction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                State = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                RiderCount = table.Column<int>(type: "int", nullable: false),
                DispatchedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DispatchedBy = table.Column<long>(type: "bigint", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TransportTrips", x => x.Id);
                table.UniqueConstraint("AK_TransportTrips_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_TransportTrips_Direction", "[Direction] IN ('Pickup','Drop')");
                table.CheckConstraint("CK_TransportTrips_State", "[State] IN ('Planned','Dispatched')");
                table.CheckConstraint("CK_TransportTrips_Dispatch",
                    "([State] = 'Planned' AND [TransportDriverId] IS NULL AND [DispatchRequestId] IS NULL AND [DispatchedAt] IS NULL AND [DispatchedBy] IS NULL AND [RiderCount] = 0) OR ([State] = 'Dispatched' AND [TransportDriverId] IS NOT NULL AND [DispatchRequestId] IS NOT NULL AND [DispatchedAt] IS NOT NULL AND [DispatchedBy] IS NOT NULL AND [RiderCount] > 0)");
                table.ForeignKey("FK_TransportTrips_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TransportTrips_Campuses_TenantId_CampusId", x => new { x.TenantId, x.CampusId },
                    "Campuses", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TransportTrips_Routes_TenantId_RouteId", x => new { x.TenantId, x.RouteId },
                    "Routes", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TransportTrips_Vehicles_TenantId_VehicleId", x => new { x.TenantId, x.VehicleId },
                    "Vehicles", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TransportTrips_TransportDrivers_TenantId_TransportDriverId",
                    x => new { x.TenantId, x.TransportDriverId }, "TransportDrivers",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_TransportTrips_PublicId", "TransportTrips", "PublicId", unique: true);
        migrationBuilder.CreateIndex("IX_TransportTrips_TenantId_ClientRequestId", "TransportTrips",
            new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_TransportTrips_TenantId_DispatchRequestId", "TransportTrips",
            new[] { "TenantId", "DispatchRequestId" }, unique: true, filter: "[DispatchRequestId] IS NOT NULL");
        migrationBuilder.CreateIndex("IX_TransportTrips_TenantId_VehicleId_OperatingDate_Direction", "TransportTrips",
            new[] { "TenantId", "VehicleId", "OperatingDate", "Direction" }, unique: true);
        migrationBuilder.CreateIndex("IX_TransportTrips_TenantId_CampusId_OperatingDate", "TransportTrips",
            new[] { "TenantId", "CampusId", "OperatingDate" });
        migrationBuilder.CreateIndex("IX_TransportTrips_TenantId_TransportDriverId_OperatingDate_Direction", "TransportTrips",
            new[] { "TenantId", "TransportDriverId", "OperatingDate", "Direction" },
            unique: true, filter: "[TransportDriverId] IS NOT NULL");
        migrationBuilder.CreateIndex("IX_TransportTrips_TenantId_RouteId", "TransportTrips", new[] { "TenantId", "RouteId" });

        migrationBuilder.CreateTable(
            name: "TransportTripRiders",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                TransportTripId = table.Column<long>(type: "bigint", nullable: false),
                StudentTransportId = table.Column<long>(type: "bigint", nullable: false),
                StudentId = table.Column<long>(type: "bigint", nullable: false),
                StudentEnrollmentId = table.Column<long>(type: "bigint", nullable: false),
                StudentCodeSnapshot = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                PickupStopId = table.Column<long>(type: "bigint", nullable: false),
                DropStopId = table.Column<long>(type: "bigint", nullable: false),
                PickupStopSnapshot = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                DropStopSnapshot = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TransportTripRiders", x => x.Id);
                table.UniqueConstraint("AK_TransportTripRiders_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey("FK_TransportTripRiders_Tenants_TenantId", x => x.TenantId,
                    "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TransportTripRiders_TransportTrips_TenantId_TransportTripId",
                    x => new { x.TenantId, x.TransportTripId }, "TransportTrips",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TransportTripRiders_StudentTransports_TenantId_StudentTransportId",
                    x => new { x.TenantId, x.StudentTransportId }, "StudentTransports",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TransportTripRiders_Students_TenantId_StudentId",
                    x => new { x.TenantId, x.StudentId }, "Students",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TransportTripRiders_StudentEnrollments_TenantId_StudentEnrollmentId",
                    x => new { x.TenantId, x.StudentEnrollmentId }, "StudentEnrollments",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_TransportTripRiders_TenantId_TransportTripId_StudentTransportId",
            "TransportTripRiders", new[] { "TenantId", "TransportTripId", "StudentTransportId" }, unique: true);
        migrationBuilder.CreateIndex("IX_TransportTripRiders_TenantId_StudentId_TransportTripId",
            "TransportTripRiders", new[] { "TenantId", "StudentId", "TransportTripId" });
        migrationBuilder.CreateIndex("IX_TransportTripRiders_TenantId_StudentTransportId",
            "TransportTripRiders", new[] { "TenantId", "StudentTransportId" });
        migrationBuilder.CreateIndex("IX_TransportTripRiders_TenantId_StudentEnrollmentId",
            "TransportTripRiders", new[] { "TenantId", "StudentEnrollmentId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("TransportTripRiders");
        migrationBuilder.DropTable("TransportTrips");
    }
}
