using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Optional institution overrides of reviewed external AI capabilities.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261121120000_TenantAiAgentControls")]
public sealed class TenantAiAgentControls : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("TenantAiAgentControls", columns: table => new
        {
            TenantId = table.Column<long>(type: "bigint", nullable: false),
            AgentCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
            IsEnabled = table.Column<bool>(type: "bit", nullable: false),
            UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
            UpdatedByUserId = table.Column<long>(type: "bigint", nullable: false),
            RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_TenantAiAgentControls", x => new { x.TenantId, x.AgentCode });
            table.ForeignKey("FK_TenantAiAgentControls_Tenants", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            table.CheckConstraint("CK_TenantAiAgentControls_Code", "LEN([AgentCode]) BETWEEN 2 AND 40");
        });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM dbo.TenantAiAgentControls) " +
            "THROW 51095, 'Retain tenant AI agent preferences before rollback.', 1;");
        migrationBuilder.DropTable("TenantAiAgentControls");
    }
}