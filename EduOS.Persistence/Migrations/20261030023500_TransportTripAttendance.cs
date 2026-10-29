using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Additive, append-only operational attendance. Down drops historical evidence:
/// use only on a nonproduction restore with explicit retention approval.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261030023500_TransportTripAttendance")]
public sealed class TransportTripAttendance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TransportTripAttendanceEvents",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                TransportTripRiderId = table.Column<long>(type: "bigint", nullable: false),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Sequence = table.Column<int>(type: "int", nullable: false),
                EventType = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                table.PrimaryKey("PK_TransportTripAttendanceEvents", x => x.Id);
                table.UniqueConstraint("AK_TransportTripAttendanceEvents_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_TransportTripAttendanceEvents_Type", "[EventType] IN ('Boarded','Dropped','NoShow')");
                table.CheckConstraint("CK_TransportTripAttendanceEvents_Sequence",
                    "([Sequence] = 1 AND [EventType] IN ('Boarded','NoShow')) OR ([Sequence] = 2 AND [EventType] = 'Dropped')");
                table.ForeignKey("FK_TransportTripAttendanceEvents_Tenants_TenantId",
                    x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TransportTripAttendanceEvents_TransportTripRiders_TenantId_TransportTripRiderId",
                    x => new { x.TenantId, x.TransportTripRiderId }, "TransportTripRiders",
                    new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_TransportTripAttendanceEvents_TenantId_ClientRequestId",
            "TransportTripAttendanceEvents", new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex("IX_TransportTripAttendanceEvents_TenantId_TransportTripRiderId_Sequence",
            "TransportTripAttendanceEvents", new[] { "TenantId", "TransportTripRiderId", "Sequence" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("TransportTripAttendanceEvents");
    }
}
