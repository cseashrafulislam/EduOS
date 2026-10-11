using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20261011165000_SubscriptionPaymentPayerAccount")]
public sealed class SubscriptionPaymentPayerAccount : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PayerAccountNumber",
            table: "SubscriptionPayments",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_SubscriptionPayments_PlatformPending",
            table: "SubscriptionPayments",
            columns: new[] { "InitiatedAt", "Id" },
            filter: "[IsDeleted] = 0 AND [PaymentMethod] = 2 AND [State] = 2");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_SubscriptionPayments_PlatformPending", table: "SubscriptionPayments");
        migrationBuilder.DropColumn(
            name: "PayerAccountNumber",
            table: "SubscriptionPayments");
    }
}
