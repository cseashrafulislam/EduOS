using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// Independent invitation consent. Default OFF for every historical graduate;
/// existing directory consent never silently enables Email/SMS marketing.
/// The Alumni tables are SQL-owned and intentionally absent from the EF snapshot.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261101150000_AlumniInvitationChannelConsent")]
public sealed class AlumniInvitationChannelConsent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("InvitationEmailOptIn", "AlumniProfiles", type: "bit",
            nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>("InvitationSmsOptIn", "AlumniProfiles", type: "bit",
            nullable: false, defaultValue: false);
        migrationBuilder.Sql(@"
            ALTER TABLE dbo.AlumniProfiles DROP CONSTRAINT CK_AlumniProfiles_PrivateContact;
            ALTER TABLE dbo.AlumniProfiles ADD CONSTRAINT CK_AlumniProfiles_PrivateContact CHECK (
                (DirectoryOptIn = 1 OR InvitationEmailOptIn = 1 OR ContactEmail IS NULL)
                AND (DirectoryOptIn = 1 OR InvitationSmsOptIn = 1 OR ContactPhone IS NULL)
                AND (InvitationEmailOptIn = 0 OR ContactEmail IS NOT NULL)
                AND (InvitationSmsOptIn = 0 OR ContactPhone IS NOT NULL)
            );");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A rollback must never silently erase separately consented private contact data.
        migrationBuilder.Sql(@"
            IF EXISTS (SELECT 1 FROM dbo.AlumniProfiles
                       WHERE InvitationEmailOptIn = 1 OR InvitationSmsOptIn = 1)
                THROW 51000, 'Revoke invitation-only consent and export permitted data before rollback.', 1;
            ALTER TABLE dbo.AlumniProfiles DROP CONSTRAINT CK_AlumniProfiles_PrivateContact;
            ALTER TABLE dbo.AlumniProfiles ADD CONSTRAINT CK_AlumniProfiles_PrivateContact
                CHECK (DirectoryOptIn = 1 OR (ContactEmail IS NULL AND ContactPhone IS NULL));");
        migrationBuilder.DropColumn("InvitationEmailOptIn", "AlumniProfiles");
        migrationBuilder.DropColumn("InvitationSmsOptIn", "AlumniProfiles");
    }
}
