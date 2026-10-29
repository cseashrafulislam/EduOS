using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Nullable historical keys preserve legacy fine and discount rows without backfilling invented request identifiers.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261029230000_StudentAdjustmentRequestIdempotency")]
public sealed class StudentAdjustmentRequestIdempotency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "ClientRequestId", table: "StudentDiscounts",
            type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "ClientRequestId", table: "StudentFines",
            type: "uniqueidentifier", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_StudentDiscounts_TenantId_ClientRequestId",
            table: "StudentDiscounts", columns: new[] { "TenantId", "ClientRequestId" },
            unique: true, filter: "[ClientRequestId] IS NOT NULL");
        migrationBuilder.CreateIndex(name: "IX_StudentFines_TenantId_ClientRequestId",
            table: "StudentFines", columns: new[] { "TenantId", "ClientRequestId" },
            unique: true, filter: "[ClientRequestId] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_StudentDiscounts_TenantId_ClientRequestId", table: "StudentDiscounts");
        migrationBuilder.DropIndex(name: "IX_StudentFines_TenantId_ClientRequestId", table: "StudentFines");
        migrationBuilder.DropColumn(name: "ClientRequestId", table: "StudentDiscounts");
        migrationBuilder.DropColumn(name: "ClientRequestId", table: "StudentFines");
    }
}
