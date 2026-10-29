using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// Additive learner-consented public LMS certificate verification. The SHA-256 token
/// digest is the only stored bearer value. Existing certificates remain private.
/// Rolling back drops opt-in links; no historical completion evidence is changed.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261030053000_LmsPublicCertificateVerification")]
public sealed class LmsPublicCertificateVerification : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "VerificationTokenHash", table: "LmsCompletionCertificates",
            type: "nvarchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<DateTime>(
            name: "VerificationExpiresAt", table: "LmsCompletionCertificates",
            type: "datetime2", nullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_LmsCompletionCertificates_VerificationTokenHash",
            table: "LmsCompletionCertificates", column: "VerificationTokenHash",
            unique: true, filter: "[VerificationTokenHash] IS NOT NULL");
        migrationBuilder.AddCheckConstraint(
            name: "CK_LmsCompletionCertificates_VerificationPair",
            table: "LmsCompletionCertificates",
            sql: "([VerificationTokenHash] IS NULL AND [VerificationExpiresAt] IS NULL) OR ([VerificationTokenHash] IS NOT NULL AND [VerificationExpiresAt] IS NOT NULL AND [VerificationExpiresAt] > [IssuedAt])");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_LmsCompletionCertificates_VerificationPair",
            table: "LmsCompletionCertificates");
        migrationBuilder.DropIndex(
            name: "IX_LmsCompletionCertificates_VerificationTokenHash",
            table: "LmsCompletionCertificates");
        migrationBuilder.DropColumn(name: "VerificationTokenHash", table: "LmsCompletionCertificates");
        migrationBuilder.DropColumn(name: "VerificationExpiresAt", table: "LmsCompletionCertificates");
    }
}
