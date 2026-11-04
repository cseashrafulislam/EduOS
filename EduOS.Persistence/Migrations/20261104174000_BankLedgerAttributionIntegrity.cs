using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// One live bank mapping per institutional GL posting account. Migration fails
/// without modifying source records if legacy mappings need manual reconciliation.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261104174000_BankLedgerAttributionIntegrity")]
public sealed class BankLedgerAttributionIntegrity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1 FROM dbo.BankAccounts WHERE IsDeleted = 0
    GROUP BY TenantId, AccountId HAVING COUNT_BIG(*) > 1
)
    THROW 51041, 'Duplicate live bank-to-ledger mappings require audited reconciliation before migration.', 1;");
        migrationBuilder.DropIndex("IX_BankAccounts_TenantId_AccountId", "BankAccounts");
        migrationBuilder.CreateIndex(
            name: "IX_BankAccounts_TenantId_AccountId", table: "BankAccounts",
            columns: new[] { "TenantId", "AccountId" },
            unique: true, filter: "[IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_BankAccounts_TenantId_AccountId", "BankAccounts");
        migrationBuilder.CreateIndex(
            name: "IX_BankAccounts_TenantId_AccountId", table: "BankAccounts",
            columns: new[] { "TenantId", "AccountId" });
    }
}
