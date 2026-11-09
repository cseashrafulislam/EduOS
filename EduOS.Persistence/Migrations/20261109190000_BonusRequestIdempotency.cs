using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>
/// Existing bonuses are intentionally nullable; new submissions use a tenant-scoped
/// unique receipt so timed-out or concurrent retries cannot create duplicate rows.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261109190000_BonusRequestIdempotency")]
public sealed class BonusRequestIdempotency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ClientRequestId", table: "Bonuses",
            type: "uniqueidentifier", nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Bonuses_TenantId_ClientRequestId", table: "Bonuses",
            columns: new[] { "TenantId", "ClientRequestId" },
            unique: true, filter: "[ClientRequestId] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Bonuses_TenantId_ClientRequestId", table: "Bonuses");
        migrationBuilder.DropColumn(name: "ClientRequestId", table: "Bonuses");
    }
}
