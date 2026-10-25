using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>Persists form-specific document policies; legacy forms have no required documents.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261026004500_AdmissionIntakeDocumentRequirements")]
public sealed class AdmissionIntakeDocumentRequirements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DocumentRequirementsJson",
            table: "AdmissionIntakeForms",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "[]");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DocumentRequirementsJson", table: "AdmissionIntakeForms");
    }
}
