using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>
/// Nullable destination-side confirmation evidence; legacy transfers need
/// explicit reconfirmation. Rollback drops evidence but preserves bindings.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261025044500_DestinationTransferEnrollmentConfirmation")]
public sealed class DestinationTransferEnrollmentConfirmation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "DestinationEnrollmentConfirmedAt", table: "TransferRequests",
            type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<long>(
            name: "DestinationEnrollmentConfirmedByUserId", table: "TransferRequests",
            type: "bigint", nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "DestinationEnrollmentVersionAtConfirmation", table: "TransferRequests",
            type: "nvarchar(100)", maxLength: 100, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DestinationEnrollmentVersionAtConfirmation", table: "TransferRequests");
        migrationBuilder.DropColumn(name: "DestinationEnrollmentConfirmedByUserId", table: "TransferRequests");
        migrationBuilder.DropColumn(name: "DestinationEnrollmentConfirmedAt", table: "TransferRequests");
    }
}
