using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Explicit cash-in-hand identification. Existing accounts default to unclassified.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261104173000_AccountCashLedgerDesignation")]
public sealed class AccountCashLedgerDesignation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsCashInHand", table: "Accounts", type: "bit", nullable: false, defaultValue: false);
        migrationBuilder.AddCheckConstraint(
            name: "CK_Accounts_CashInHandPostingAsset", table: "Accounts",
            sql: "[IsCashInHand] = 0 OR ([AccountType] = 1 AND [IsGroup] = 0 AND [AllowsPosting] = 1 AND [IsActive] = 1 AND [CurrencyCode] = N'BDT')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_Accounts_CashInHandPostingAsset", "Accounts");
        migrationBuilder.DropColumn("IsCashInHand", "Accounts");
    }
}
