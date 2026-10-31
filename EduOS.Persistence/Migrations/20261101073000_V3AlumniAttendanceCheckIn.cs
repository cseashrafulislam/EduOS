using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20261101073000_V3AlumniAttendanceCheckIn")]
public sealed class V3AlumniAttendanceCheckIn : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>("CheckedInAtUtc", "AlumniEventRsvps",
            type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<long>("CheckedInByUserId", "AlumniEventRsvps",
            type: "bigint", nullable: true);
        migrationBuilder.AddCheckConstraint("CK_AlumniRsvp_CheckIn", "AlumniEventRsvps",
            "([CheckedInAtUtc] IS NULL AND [CheckedInByUserId] IS NULL) OR " +
            "([CheckedInAtUtc] IS NOT NULL AND [CheckedInByUserId] IS NOT NULL AND [Response] = 1)");
        migrationBuilder.CreateIndex("IX_AlumniRsvp_Attendance", "AlumniEventRsvps",
            new[] { "TenantId", "EventId", "Response", "CheckedInAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_AlumniRsvp_Attendance", "AlumniEventRsvps");
        migrationBuilder.DropCheckConstraint("CK_AlumniRsvp_CheckIn", "AlumniEventRsvps");
        migrationBuilder.DropColumn("CheckedInByUserId", "AlumniEventRsvps");
        migrationBuilder.DropColumn("CheckedInAtUtc", "AlumniEventRsvps");
    }
}
