using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>
/// Preserve old loan registrations while ensuring that new command retries can never
/// create duplicate loans for a tenant. Historical rows have NULL request keys.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261028032000_LoanAdvanceRegistrationIdempotency")]
public sealed class LoanAdvanceRegistrationIdempotency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ClientRequestId",
            table: "LoanAdvances",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_LoanAdvances_TenantId_ClientRequestId",
            table: "LoanAdvances",
            columns: new[] { "TenantId", "ClientRequestId" },
            unique: true,
            filter: "[ClientRequestId] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_LoanAdvances_TenantId_ClientRequestId",
            table: "LoanAdvances");
        migrationBuilder.DropColumn(
            name: "ClientRequestId",
            table: "LoanAdvances");
    }
}
