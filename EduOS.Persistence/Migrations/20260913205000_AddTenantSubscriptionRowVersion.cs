using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260913205000_AddTenantSubscriptionRowVersion")]
public partial class AddTenantSubscriptionRowVersion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[TenantSubscriptions]', N'U') IS NULL
    THROW 51000, 'Cannot add TenantSubscription concurrency protection because TenantSubscriptions does not exist.', 1;
IF COL_LENGTH('dbo.TenantSubscriptions', 'RowVersion') IS NULL
    ALTER TABLE [dbo].[TenantSubscriptions] ADD [RowVersion] rowversion NOT NULL;");
            return;
        }

        migrationBuilder.AddColumn<byte[]>(
            name: "RowVersion",
            table: "TenantSubscriptions",
            rowVersion: true,
            nullable: false,
            defaultValue: Array.Empty<byte>());
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RowVersion",
            table: "TenantSubscriptions");
    }
}
