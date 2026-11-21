using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Tenant-owned provider secret. No provider key is shared between institutions.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261121100000_TenantAiProviderCredentials")]
public sealed class TenantAiProviderCredentials : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("TenantAiProviderSettings", columns: table => new
        {
            TenantId = table.Column<long>(type: "bigint", nullable: false),
            ProviderCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
            Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
            ProtectedApiKey = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
            IsEnabled = table.Column<bool>(type: "bit", nullable: false),
            StudentTutorEnabled = table.Column<bool>(type: "bit", nullable: false),
            UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
            UpdatedByUserId = table.Column<long>(type: "bigint", nullable: false),
            RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_TenantAiProviderSettings", x => x.TenantId);
            table.ForeignKey("FK_TenantAiProviderSettings_Tenants", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            table.CheckConstraint("CK_TenantAiProviderSettings_EnabledHasKey",
                "[IsEnabled] = 0 OR [ProtectedApiKey] IS NOT NULL");
            table.CheckConstraint("CK_TenantAiProviderSettings_Provider",
                "[ProviderCode] = N'OpenAI'");
            table.CheckConstraint("CK_TenantAiProviderSettings_StudentEnabled",
                "[StudentTutorEnabled] = 0 OR [IsEnabled] = 1");
        });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM dbo.TenantAiProviderSettings) THROW 51093, 'Export and securely remove tenant AI configuration before rolling back.', 1;");
        migrationBuilder.DropTable("TenantAiProviderSettings");
    }
}