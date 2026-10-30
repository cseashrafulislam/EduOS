using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>
/// Tenant-leading active overdue candidate and same-loan/day notification lookups.
/// Additive indexes only; existing circulation and notification history is unchanged.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261030074500_LibraryOverdueNoticeLookupIndexes")]
public partial class LibraryOverdueNoticeLookupIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_BookIssues_LibraryOverdueCandidates",
            table: "BookIssues",
            columns: new[] { "TenantId", "State", "DueDate", "Id" },
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_Notifications_LibraryOverdueSourceDay",
            table: "Notifications",
            columns: new[] { "TenantId", "SourceType", "SourceId", "TypeCode", "CreatedAt" },
            filter: "[IsDeleted] = 0 AND [SourceId] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Notifications_LibraryOverdueSourceDay", table: "Notifications");
        migrationBuilder.DropIndex(
            name: "IX_BookIssues_LibraryOverdueCandidates", table: "BookIssues");
    }
}
