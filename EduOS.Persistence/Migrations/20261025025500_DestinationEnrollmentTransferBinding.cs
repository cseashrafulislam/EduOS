using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>
/// Records the exact destination enrollment selected by the receiving institution.
/// Existing transfer records remain nullable and require destination re-linking before completion.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261025025500_DestinationEnrollmentTransferBinding")]
public sealed class DestinationEnrollmentTransferBinding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "DestinationEnrollmentId", table: "TransferRequests",
            type: "bigint", nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_TransferRequests_DestinationTenantId_DestinationEnrollmentId",
            table: "TransferRequests",
            columns: new[] { "DestinationTenantId", "DestinationEnrollmentId" },
            unique: true,
            filter: "[DestinationEnrollmentId] IS NOT NULL AND [IsDeleted] = 0");

        migrationBuilder.AddForeignKey(
            name: "FK_TransferRequests_StudentEnrollments_DestinationTenantId_DestinationEnrollmentId",
            table: "TransferRequests",
            columns: new[] { "DestinationTenantId", "DestinationEnrollmentId" },
            principalTable: "StudentEnrollments",
            principalColumns: new[] { "TenantId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_TransferRequests_StudentEnrollments_DestinationTenantId_DestinationEnrollmentId",
            table: "TransferRequests");
        migrationBuilder.DropIndex(
            name: "IX_TransferRequests_DestinationTenantId_DestinationEnrollmentId",
            table: "TransferRequests");
        migrationBuilder.DropColumn(
            name: "DestinationEnrollmentId", table: "TransferRequests");
    }
}
