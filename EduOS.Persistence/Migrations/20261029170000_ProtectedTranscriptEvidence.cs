using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// Additive only. Existing metadata-only transcripts remain nullable/legacy;
/// never manufacture an issue-time academic record from later editable data.
/// Before rollback, archive issued encrypted documents and keep their key ring.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261029170000_ProtectedTranscriptEvidence")]
public sealed class ProtectedTranscriptEvidence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ProtectedAcademicRecord", table: "TranscriptIssues",
            type: "nvarchar(max)", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ProtectedAcademicRecord", table: "TranscriptIssues");
    }
}
