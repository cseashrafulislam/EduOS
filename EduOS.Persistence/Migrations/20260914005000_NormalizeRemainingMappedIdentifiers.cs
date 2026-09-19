using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations
{
    public partial class NormalizeRemainingMappedIdentifiers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [Accounts] WHERE [ParentAccountId1] IS NOT NULL AND [ParentAccountId] IS NOT NULL AND [ParentAccountId] > 0 AND CONVERT(bigint,[ParentAccountId]) <> [ParentAccountId1])
    THROW 51000, 'Accounts contains conflicting ParentAccountId and ParentAccountId1 values.', 1;
IF EXISTS (SELECT 1 FROM [Expenses] WHERE [BankAccountId1] IS NOT NULL AND [BankAccountId] > 0 AND CONVERT(bigint,[BankAccountId]) <> [BankAccountId1])
    THROW 51000, 'Expenses contains conflicting BankAccountId values.', 1;
IF EXISTS (SELECT 1 FROM [Expenses] WHERE [CategoryId1] IS NOT NULL AND [CategoryId] > 0 AND CONVERT(bigint,[CategoryId]) <> [CategoryId1])
    THROW 51000, 'Expenses contains conflicting CategoryId values.', 1;
IF EXISTS (SELECT 1 FROM [Incomes] WHERE [BankAccountId1] IS NOT NULL AND [BankAccountId] > 0 AND CONVERT(bigint,[BankAccountId]) <> [BankAccountId1])
    THROW 51000, 'Incomes contains conflicting BankAccountId values.', 1;
IF EXISTS (SELECT 1 FROM [Incomes] WHERE [CategoryId1] IS NOT NULL AND [CategoryId] > 0 AND CONVERT(bigint,[CategoryId]) <> [CategoryId1])
    THROW 51000, 'Incomes contains conflicting CategoryId values.', 1;
IF EXISTS (SELECT 1 FROM [VoucherDetails] WHERE [AccountId1] IS NOT NULL AND [AccountId] > 0 AND CONVERT(bigint,[AccountId]) <> [AccountId1])
    THROW 51000, 'VoucherDetails contains conflicting AccountId values.', 1;
IF EXISTS (SELECT 1 FROM [VoucherDetails] WHERE [VoucherId1] IS NOT NULL AND [VoucherId] > 0 AND CONVERT(bigint,[VoucherId]) <> [VoucherId1])
    THROW 51000, 'VoucherDetails contains conflicting VoucherId values.', 1;
IF EXISTS (SELECT 1 FROM [AssetMaintenances] WHERE [AssetId1] IS NOT NULL AND [AssetId] > 0 AND CONVERT(bigint,[AssetId]) <> [AssetId1])
    THROW 51000, 'AssetMaintenances contains conflicting AssetId values.', 1;

IF EXISTS (SELECT 1 FROM [Accounts] a WHERE COALESCE(NULLIF(CONVERT(bigint,a.[ParentAccountId]),0),a.[ParentAccountId1]) IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [Accounts] p WHERE p.[Id]=COALESCE(NULLIF(CONVERT(bigint,a.[ParentAccountId]),0),a.[ParentAccountId1])))
    THROW 51000, 'Accounts contains an orphan parent account reference.', 1;
IF EXISTS (SELECT 1 FROM [Expenses] e WHERE COALESCE(NULLIF(CONVERT(bigint,e.[BankAccountId]),0),e.[BankAccountId1]) IS NULL OR NOT EXISTS (SELECT 1 FROM [BankAccounts] b WHERE b.[Id]=COALESCE(NULLIF(CONVERT(bigint,e.[BankAccountId]),0),e.[BankAccountId1])))
    THROW 51000, 'Expenses contains a missing or orphan bank account reference.', 1;
IF EXISTS (SELECT 1 FROM [Expenses] e WHERE COALESCE(NULLIF(CONVERT(bigint,e.[CategoryId]),0),e.[CategoryId1]) IS NULL OR NOT EXISTS (SELECT 1 FROM [ExpenseCategories] c WHERE c.[Id]=COALESCE(NULLIF(CONVERT(bigint,e.[CategoryId]),0),e.[CategoryId1])))
    THROW 51000, 'Expenses contains a missing or orphan category reference.', 1;
IF EXISTS (SELECT 1 FROM [Incomes] i WHERE COALESCE(NULLIF(CONVERT(bigint,i.[BankAccountId]),0),i.[BankAccountId1]) IS NULL OR NOT EXISTS (SELECT 1 FROM [BankAccounts] b WHERE b.[Id]=COALESCE(NULLIF(CONVERT(bigint,i.[BankAccountId]),0),i.[BankAccountId1])))
    THROW 51000, 'Incomes contains a missing or orphan bank account reference.', 1;
IF EXISTS (SELECT 1 FROM [Incomes] i WHERE COALESCE(NULLIF(CONVERT(bigint,i.[CategoryId]),0),i.[CategoryId1]) IS NULL OR NOT EXISTS (SELECT 1 FROM [IncomeCategories] c WHERE c.[Id]=COALESCE(NULLIF(CONVERT(bigint,i.[CategoryId]),0),i.[CategoryId1])))
    THROW 51000, 'Incomes contains a missing or orphan category reference.', 1;
IF EXISTS (SELECT 1 FROM [VoucherDetails] d WHERE COALESCE(NULLIF(CONVERT(bigint,d.[AccountId]),0),d.[AccountId1]) IS NULL OR NOT EXISTS (SELECT 1 FROM [Accounts] a WHERE a.[Id]=COALESCE(NULLIF(CONVERT(bigint,d.[AccountId]),0),d.[AccountId1])))
    THROW 51000, 'VoucherDetails contains a missing or orphan account reference.', 1;
IF EXISTS (SELECT 1 FROM [VoucherDetails] d WHERE COALESCE(NULLIF(CONVERT(bigint,d.[VoucherId]),0),d.[VoucherId1]) IS NULL OR NOT EXISTS (SELECT 1 FROM [Vouchers] v WHERE v.[Id]=COALESCE(NULLIF(CONVERT(bigint,d.[VoucherId]),0),d.[VoucherId1])))
    THROW 51000, 'VoucherDetails contains a missing or orphan voucher reference.', 1;
IF EXISTS (SELECT 1 FROM [AssetMaintenances] m WHERE COALESCE(NULLIF(CONVERT(bigint,m.[AssetId]),0),m.[AssetId1]) IS NULL OR NOT EXISTS (SELECT 1 FROM [Assets] a WHERE a.[Id]=COALESCE(NULLIF(CONVERT(bigint,m.[AssetId]),0),m.[AssetId1])))
    THROW 51000, 'AssetMaintenances contains a missing or orphan asset reference.', 1;

IF OBJECT_ID(N'[FK_Accounts_Accounts_ParentAccountId1]', 'F') IS NOT NULL ALTER TABLE [Accounts] DROP CONSTRAINT [FK_Accounts_Accounts_ParentAccountId1];
IF OBJECT_ID(N'[FK_Expenses_BankAccounts_BankAccountId1]', 'F') IS NOT NULL ALTER TABLE [Expenses] DROP CONSTRAINT [FK_Expenses_BankAccounts_BankAccountId1];
IF OBJECT_ID(N'[FK_Expenses_ExpenseCategories_CategoryId1]', 'F') IS NOT NULL ALTER TABLE [Expenses] DROP CONSTRAINT [FK_Expenses_ExpenseCategories_CategoryId1];
IF OBJECT_ID(N'[FK_Incomes_BankAccounts_BankAccountId1]', 'F') IS NOT NULL ALTER TABLE [Incomes] DROP CONSTRAINT [FK_Incomes_BankAccounts_BankAccountId1];
IF OBJECT_ID(N'[FK_Incomes_IncomeCategories_CategoryId1]', 'F') IS NOT NULL ALTER TABLE [Incomes] DROP CONSTRAINT [FK_Incomes_IncomeCategories_CategoryId1];
IF OBJECT_ID(N'[FK_VoucherDetails_Accounts_AccountId1]', 'F') IS NOT NULL ALTER TABLE [VoucherDetails] DROP CONSTRAINT [FK_VoucherDetails_Accounts_AccountId1];
IF OBJECT_ID(N'[FK_VoucherDetails_Vouchers_VoucherId1]', 'F') IS NOT NULL ALTER TABLE [VoucherDetails] DROP CONSTRAINT [FK_VoucherDetails_Vouchers_VoucherId1];
IF OBJECT_ID(N'[FK_AssetMaintenances_Assets_AssetId1]', 'F') IS NOT NULL ALTER TABLE [AssetMaintenances] DROP CONSTRAINT [FK_AssetMaintenances_Assets_AssetId1];

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_Accounts_ParentAccountId1' AND object_id=OBJECT_ID(N'[Accounts]')) DROP INDEX [IX_Accounts_ParentAccountId1] ON [Accounts];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_Expenses_BankAccountId1' AND object_id=OBJECT_ID(N'[Expenses]')) DROP INDEX [IX_Expenses_BankAccountId1] ON [Expenses];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_Expenses_CategoryId1' AND object_id=OBJECT_ID(N'[Expenses]')) DROP INDEX [IX_Expenses_CategoryId1] ON [Expenses];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_Incomes_BankAccountId1' AND object_id=OBJECT_ID(N'[Incomes]')) DROP INDEX [IX_Incomes_BankAccountId1] ON [Incomes];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_Incomes_CategoryId1' AND object_id=OBJECT_ID(N'[Incomes]')) DROP INDEX [IX_Incomes_CategoryId1] ON [Incomes];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_VoucherDetails_AccountId1' AND object_id=OBJECT_ID(N'[VoucherDetails]')) DROP INDEX [IX_VoucherDetails_AccountId1] ON [VoucherDetails];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_VoucherDetails_VoucherId1' AND object_id=OBJECT_ID(N'[VoucherDetails]')) DROP INDEX [IX_VoucherDetails_VoucherId1] ON [VoucherDetails];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_AssetMaintenances_AssetId1' AND object_id=OBJECT_ID(N'[AssetMaintenances]')) DROP INDEX [IX_AssetMaintenances_AssetId1] ON [AssetMaintenances];
");

            migrationBuilder.AlterColumn<long?>(name: "ChapterId", table: "Questions", type: "bigint", nullable: true, oldClrType: typeof(int), oldType: "int", oldNullable: true);
            migrationBuilder.AlterColumn<long?>(name: "ParentAccountId", table: "Accounts", type: "bigint", nullable: true, oldClrType: typeof(int), oldType: "int", oldNullable: true);
            migrationBuilder.AlterColumn<long>(name: "BankAccountId", table: "Expenses", type: "bigint", nullable: false, oldClrType: typeof(int), oldType: "int");
            migrationBuilder.AlterColumn<long>(name: "CategoryId", table: "Expenses", type: "bigint", nullable: false, oldClrType: typeof(int), oldType: "int");
            migrationBuilder.AlterColumn<long>(name: "AddedBy", table: "Expenses", type: "bigint", nullable: false, oldClrType: typeof(int), oldType: "int");
            migrationBuilder.AlterColumn<long>(name: "BankAccountId", table: "Incomes", type: "bigint", nullable: false, oldClrType: typeof(int), oldType: "int");
            migrationBuilder.AlterColumn<long>(name: "CategoryId", table: "Incomes", type: "bigint", nullable: false, oldClrType: typeof(int), oldType: "int");
            migrationBuilder.AlterColumn<long>(name: "AddedBy", table: "Incomes", type: "bigint", nullable: false, oldClrType: typeof(int), oldType: "int");
            migrationBuilder.AlterColumn<long>(name: "AccountId", table: "VoucherDetails", type: "bigint", nullable: false, oldClrType: typeof(int), oldType: "int");
            migrationBuilder.AlterColumn<long>(name: "VoucherId", table: "VoucherDetails", type: "bigint", nullable: false, oldClrType: typeof(int), oldType: "int");
            migrationBuilder.AlterColumn<long>(name: "AssetId", table: "AssetMaintenances", type: "bigint", nullable: false, oldClrType: typeof(int), oldType: "int");

            migrationBuilder.Sql(@"
UPDATE [Accounts] SET [ParentAccountId]=[ParentAccountId1] WHERE ([ParentAccountId] IS NULL OR [ParentAccountId] <= 0) AND [ParentAccountId1] IS NOT NULL;
UPDATE [Accounts] SET [ParentAccountId]=NULL WHERE [ParentAccountId] <= 0 AND [ParentAccountId1] IS NULL;
UPDATE [Expenses] SET [BankAccountId]=[BankAccountId1] WHERE [BankAccountId] <= 0 AND [BankAccountId1] IS NOT NULL;
UPDATE [Expenses] SET [CategoryId]=[CategoryId1] WHERE [CategoryId] <= 0 AND [CategoryId1] IS NOT NULL;
UPDATE [Incomes] SET [BankAccountId]=[BankAccountId1] WHERE [BankAccountId] <= 0 AND [BankAccountId1] IS NOT NULL;
UPDATE [Incomes] SET [CategoryId]=[CategoryId1] WHERE [CategoryId] <= 0 AND [CategoryId1] IS NOT NULL;
UPDATE [VoucherDetails] SET [AccountId]=[AccountId1] WHERE [AccountId] <= 0 AND [AccountId1] IS NOT NULL;
UPDATE [VoucherDetails] SET [VoucherId]=[VoucherId1] WHERE [VoucherId] <= 0 AND [VoucherId1] IS NOT NULL;
UPDATE [AssetMaintenances] SET [AssetId]=[AssetId1] WHERE [AssetId] <= 0 AND [AssetId1] IS NOT NULL;
");

            migrationBuilder.DropColumn(name: "ParentAccountId1", table: "Accounts");
            migrationBuilder.DropColumn(name: "BankAccountId1", table: "Expenses");
            migrationBuilder.DropColumn(name: "CategoryId1", table: "Expenses");
            migrationBuilder.DropColumn(name: "BankAccountId1", table: "Incomes");
            migrationBuilder.DropColumn(name: "CategoryId1", table: "Incomes");
            migrationBuilder.DropColumn(name: "AccountId1", table: "VoucherDetails");
            migrationBuilder.DropColumn(name: "VoucherId1", table: "VoucherDetails");
            migrationBuilder.DropColumn(name: "AssetId1", table: "AssetMaintenances");

            migrationBuilder.CreateIndex(name: "IX_Accounts_ParentAccountId", table: "Accounts", column: "ParentAccountId");
            migrationBuilder.CreateIndex(name: "IX_Expenses_BankAccountId", table: "Expenses", column: "BankAccountId");
            migrationBuilder.CreateIndex(name: "IX_Expenses_CategoryId", table: "Expenses", column: "CategoryId");
            migrationBuilder.CreateIndex(name: "IX_Incomes_BankAccountId", table: "Incomes", column: "BankAccountId");
            migrationBuilder.CreateIndex(name: "IX_Incomes_CategoryId", table: "Incomes", column: "CategoryId");
            migrationBuilder.CreateIndex(name: "IX_VoucherDetails_AccountId", table: "VoucherDetails", column: "AccountId");
            migrationBuilder.CreateIndex(name: "IX_VoucherDetails_VoucherId", table: "VoucherDetails", column: "VoucherId");
            migrationBuilder.CreateIndex(name: "IX_AssetMaintenances_AssetId", table: "AssetMaintenances", column: "AssetId");

            migrationBuilder.AddForeignKey(name: "FK_Accounts_Accounts_ParentAccountId", table: "Accounts", column: "ParentAccountId", principalTable: "Accounts", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_Expenses_BankAccounts_BankAccountId", table: "Expenses", column: "BankAccountId", principalTable: "BankAccounts", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_Expenses_ExpenseCategories_CategoryId", table: "Expenses", column: "CategoryId", principalTable: "ExpenseCategories", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_Incomes_BankAccounts_BankAccountId", table: "Incomes", column: "BankAccountId", principalTable: "BankAccounts", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_Incomes_IncomeCategories_CategoryId", table: "Incomes", column: "CategoryId", principalTable: "IncomeCategories", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_VoucherDetails_Accounts_AccountId", table: "VoucherDetails", column: "AccountId", principalTable: "Accounts", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_VoucherDetails_Vouchers_VoucherId", table: "VoucherDetails", column: "VoucherId", principalTable: "Vouchers", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_AssetMaintenances_Assets_AssetId", table: "AssetMaintenances", column: "AssetId", principalTable: "Assets", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Normalized bigint identifiers cannot be safely narrowed back to int.");
        }
    }
}
