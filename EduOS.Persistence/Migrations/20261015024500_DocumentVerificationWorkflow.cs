using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20261015024500_DocumentVerificationWorkflow")]
public sealed class DocumentVerificationWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("VerificationStatusCode", "Documents", type: "nvarchar(24)",
            maxLength: 24, nullable: false, defaultValue: "NotRequired");
        migrationBuilder.AddColumn<long>("VerifiedByUserId", "Documents", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<DateTime>("VerifiedAt", "Documents", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<string>("VerificationNote", "Documents", type: "nvarchar(500)",
            maxLength: 500, nullable: true);
        migrationBuilder.Sql("""
            UPDATE d SET d.VerificationStatusCode = N'Pending', d.IsActive = 0
            FROM dbo.Documents d
            INNER JOIN dbo.DocumentTypeDefinitions t
              ON t.TenantId = d.TenantId AND t.Code = d.DocumentTypeCode
            WHERE d.IsDeleted = 0 AND t.IsDeleted = 0
              AND (t.RequiresVerification = 1 OR t.IsSensitive = 1);
            """);
        migrationBuilder.AddCheckConstraint("CK_Documents_VerificationStatus", "Documents",
            "[VerificationStatusCode] IN (N'NotRequired', N'Pending', N'Approved', N'Rejected')");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_Documents_VerificationStatus", "Documents");
        migrationBuilder.DropColumn("VerificationNote", "Documents");
        migrationBuilder.DropColumn("VerifiedAt", "Documents");
        migrationBuilder.DropColumn("VerifiedByUserId", "Documents");
        migrationBuilder.DropColumn("VerificationStatusCode", "Documents");
    }
}