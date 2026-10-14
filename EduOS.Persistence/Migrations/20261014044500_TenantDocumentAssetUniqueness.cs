using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>One active document metadata owner per tenant-private file asset.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261014044500_TenantDocumentAssetUniqueness")]
public sealed class TenantDocumentAssetUniqueness : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Reject legacy duplicates instead of deleting or merging user records.
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM dbo.Documents WITH (UPDLOCK, HOLDLOCK)
                WHERE IsDeleted = 0
                GROUP BY TenantId, FileAssetId HAVING COUNT_BIG(*) > 1
            ) THROW 51131, 'Duplicate active document attachments; reconcile before migration.', 1;
            """);
        migrationBuilder.DropIndex("IX_Documents_TenantId_FileAssetId", "Documents");
        migrationBuilder.CreateIndex("IX_Documents_TenantId_FileAssetId", "Documents",
            new[] { "TenantId", "FileAssetId" }, unique: true, filter: "[IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_Documents_TenantId_FileAssetId", "Documents");
        migrationBuilder.CreateIndex("IX_Documents_TenantId_FileAssetId", "Documents",
            new[] { "TenantId", "FileAssetId" });
    }
}
