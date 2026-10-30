using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>
/// Optional single-campus licence for scanned Library digital Documents.
/// Null preserves existing tenant-wide access; the composite FK rejects cross-tenant campuses.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261030095000_LibraryDigitalCampusLicensing")]
public partial class LibraryDigitalCampusLicensing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "LibraryCampusId", table: "Documents", type: "bigint", nullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_Documents_TenantId_LibraryCampusId",
            table: "Documents", columns: new[] { "TenantId", "LibraryCampusId" });
        migrationBuilder.AddForeignKey(
            name: "FK_Documents_Campuses_TenantId_LibraryCampusId",
            table: "Documents", columns: new[] { "TenantId", "LibraryCampusId" },
            principalTable: "Campuses", principalColumns: new[] { "TenantId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Documents_Campuses_TenantId_LibraryCampusId", table: "Documents");
        migrationBuilder.DropIndex(
            name: "IX_Documents_TenantId_LibraryCampusId", table: "Documents");
        migrationBuilder.DropColumn(name: "LibraryCampusId", table: "Documents");
    }
}
