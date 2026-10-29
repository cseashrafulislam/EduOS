using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Additive immutable correction evidence. Down drops historical corrections; never run
/// on production without an approved retention and rollback procedure.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261030042000_TransportAttendanceCorrections")]
public sealed class TransportAttendanceCorrections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long?>("CorrectsEventId", "TransportTripAttendanceEvents", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<string>("CorrectionReason", "TransportTripAttendanceEvents", type: "nvarchar(500)", maxLength: 500, nullable: true);
        migrationBuilder.DropCheckConstraint("CK_TransportTripAttendanceEvents_Sequence", "TransportTripAttendanceEvents");
        migrationBuilder.AddCheckConstraint("CK_TransportTripAttendanceEvents_Sequence", "TransportTripAttendanceEvents", "([Sequence] = 1 AND [EventType] IN ('Boarded','NoShow') AND [CorrectsEventId] IS NULL AND [CorrectionReason] IS NULL) OR ([Sequence] = 2 AND [EventType] = 'Dropped' AND [CorrectsEventId] IS NULL AND [CorrectionReason] IS NULL) OR ([Sequence] BETWEEN 3 AND 12 AND [EventType] IN ('Dropped','NoShow') AND [CorrectsEventId] IS NOT NULL AND [CorrectionReason] IS NOT NULL AND LEN(LTRIM(RTRIM([CorrectionReason]))) BETWEEN 15 AND 500)");
        migrationBuilder.CreateIndex("IX_TransportTripAttendanceEvents_TenantId_CorrectsEventId",
            "TransportTripAttendanceEvents", new[] { "TenantId", "CorrectsEventId" }, filter: "[CorrectsEventId] IS NOT NULL");
        migrationBuilder.AddForeignKey("FK_TransportTripAttendanceEvents_TransportTripAttendanceEvents_TenantId_CorrectsEventId",
            "TransportTripAttendanceEvents", new[] { "TenantId", "CorrectsEventId" },
            "TransportTripAttendanceEvents", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_TransportTripAttendanceEvents_TransportTripAttendanceEvents_TenantId_CorrectsEventId", "TransportTripAttendanceEvents");
        migrationBuilder.DropIndex("IX_TransportTripAttendanceEvents_TenantId_CorrectsEventId", "TransportTripAttendanceEvents");
        migrationBuilder.DropCheckConstraint("CK_TransportTripAttendanceEvents_Sequence", "TransportTripAttendanceEvents");
        migrationBuilder.AddCheckConstraint("CK_TransportTripAttendanceEvents_Sequence", "TransportTripAttendanceEvents",
            "([Sequence] = 1 AND [EventType] IN ('Boarded','NoShow')) OR ([Sequence] = 2 AND [EventType] = 'Dropped')");
        migrationBuilder.DropColumn("CorrectsEventId", "TransportTripAttendanceEvents");
        migrationBuilder.DropColumn("CorrectionReason", "TransportTripAttendanceEvents");
    }
}
