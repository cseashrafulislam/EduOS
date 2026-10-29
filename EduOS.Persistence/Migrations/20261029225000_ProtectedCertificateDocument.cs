using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Legacy register-only certificates remain nullable and cannot be retroactively forged as issue-time documents.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261029225000_ProtectedCertificateDocument")]
public sealed class ProtectedCertificateDocument : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "ProtectedDocument", table: "CertificateIssues",
            type: "nvarchar(max)", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ProtectedDocument", table: "CertificateIssues");
    }
}
