using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260903170000_AddActiveBillingGuards")]
public partial class AddActiveBillingGuards : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<byte[]>(
            name: "RowVersion",
            table: "SubscriptionPayments",
            type: "rowversion",
            rowVersion: true,
            nullable: false);

        migrationBuilder.DropIndex(
            name: "IX_SubscriptionPayments_SubscriptionInvoiceId",
            table: "SubscriptionPayments");

        migrationBuilder.CreateIndex(
            name: "IX_SubscriptionPayments_SubscriptionInvoiceId",
            table: "SubscriptionPayments",
            column: "SubscriptionInvoiceId",
            unique: true,
            filter: "[IsDeleted] = 0 AND [Status] IN (2, 7)");

        migrationBuilder.CreateIndex(
            name: "IX_TenantSubscriptions_TenantId",
            table: "TenantSubscriptions",
            column: "TenantId",
            unique: true,
            filter: "[IsDeleted] = 0 AND [Status] IN (1, 2, 3, 6)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_SubscriptionPayments_SubscriptionInvoiceId",
            table: "SubscriptionPayments");

        migrationBuilder.CreateIndex(
            name: "IX_SubscriptionPayments_SubscriptionInvoiceId",
            table: "SubscriptionPayments",
            column: "SubscriptionInvoiceId");

        migrationBuilder.DropIndex(
            name: "IX_TenantSubscriptions_TenantId",
            table: "TenantSubscriptions");

        migrationBuilder.DropColumn(
            name: "RowVersion",
            table: "SubscriptionPayments");
    }
}
