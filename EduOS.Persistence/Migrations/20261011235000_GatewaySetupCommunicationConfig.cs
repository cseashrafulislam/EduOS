using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

public partial class GatewaySetupCommunicationConfig : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM dbo.CommunicationGateways WHERE IsDeleted = 0
                GROUP BY TenantId, Channel, ProviderCode HAVING COUNT_BIG(*) > 1
            )
                THROW 51091, 'Duplicate communication providers exist per tenant/channel. Reconcile before applying gateway migration.', 1;
            IF EXISTS (
                SELECT 1 FROM dbo.CommunicationGateways WHERE IsDeleted = 0 AND IsDefault = 1
                GROUP BY TenantId, Channel HAVING COUNT_BIG(*) > 1
            )
                THROW 51092, 'Multiple default communication gateways exist per tenant/channel. Reconcile before applying gateway migration.', 1;
            """);
        migrationBuilder.AddColumn<string>(name: "SmsSenderId", table: "CommunicationGateways",
            type: "nvarchar(20)", maxLength: 20, nullable: true);
        migrationBuilder.AddColumn<string>(name: "SmtpHost", table: "CommunicationGateways",
            type: "nvarchar(253)", maxLength: 253, nullable: true);
        migrationBuilder.AddColumn<int>(name: "SmtpPort", table: "CommunicationGateways",
            type: "int", nullable: true);
        migrationBuilder.AddColumn<string>(name: "SmtpUsername", table: "CommunicationGateways",
            type: "nvarchar(320)", maxLength: 320, nullable: true);
        migrationBuilder.AddColumn<string>(name: "FromEmail", table: "CommunicationGateways",
            type: "nvarchar(320)", maxLength: 320, nullable: true);
        migrationBuilder.AddColumn<string>(name: "FromName", table: "CommunicationGateways",
            type: "nvarchar(150)", maxLength: 150, nullable: true);
        migrationBuilder.AddColumn<bool>(name: "UseSsl", table: "CommunicationGateways",
            type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.CreateIndex(name: "UX_CommunicationGateways_Tenant_Channel_Provider",
            table: "CommunicationGateways", columns: new[] { "TenantId", "Channel", "ProviderCode" },
            unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex(name: "UX_CommunicationGateways_Tenant_Channel_Default",
            table: "CommunicationGateways", columns: new[] { "TenantId", "Channel" },
            unique: true, filter: "[IsDeleted] = 0 AND [IsDefault] = 1");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "UX_CommunicationGateways_Tenant_Channel_Default",
            table: "CommunicationGateways");
        migrationBuilder.DropIndex(name: "UX_CommunicationGateways_Tenant_Channel_Provider",
            table: "CommunicationGateways");
        migrationBuilder.DropColumn(name: "SmsSenderId", table: "CommunicationGateways");
        migrationBuilder.DropColumn(name: "SmtpHost", table: "CommunicationGateways");
        migrationBuilder.DropColumn(name: "SmtpPort", table: "CommunicationGateways");
        migrationBuilder.DropColumn(name: "SmtpUsername", table: "CommunicationGateways");
        migrationBuilder.DropColumn(name: "FromEmail", table: "CommunicationGateways");
        migrationBuilder.DropColumn(name: "FromName", table: "CommunicationGateways");
        migrationBuilder.DropColumn(name: "UseSsl", table: "CommunicationGateways");
    }
}
