using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>Preserves review outcomes independently from the legacy IsVerified flag.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261025224500_AdmissionApplicantDocumentDecisions")]
public sealed class AdmissionApplicantDocumentDecisions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "VerificationStatus",
            table: "AdmissionApplicantDocuments",
            type: "int",
            nullable: false,
            defaultValue: 1);
        // Historical verified records remain verified. Unverified records have no
        // trustworthy rejection history, so retain Pending rather than inventing it.
        migrationBuilder.Sql("UPDATE [AdmissionApplicantDocuments] SET [VerificationStatus] = 2 WHERE [IsVerified] = 1;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "VerificationStatus", table: "AdmissionApplicantDocuments");
    }
}
