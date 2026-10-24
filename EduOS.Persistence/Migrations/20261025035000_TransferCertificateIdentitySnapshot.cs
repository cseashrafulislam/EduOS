using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>
/// Preserve certificate identity as issued. Existing records remain null: backfilling
/// from the current student directory would incorrectly claim historical accuracy.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261025035000_TransferCertificateIdentitySnapshot")]
public sealed class TransferCertificateIdentitySnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "StudentNameAtIssue", table: "TransferCertificates",
            type: "nvarchar(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "StudentCodeAtIssue", table: "TransferCertificates",
            type: "nvarchar(50)", maxLength: 50, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "StudentCodeAtIssue", table: "TransferCertificates");
        migrationBuilder.DropColumn(name: "StudentNameAtIssue", table: "TransferCertificates");
    }
}
