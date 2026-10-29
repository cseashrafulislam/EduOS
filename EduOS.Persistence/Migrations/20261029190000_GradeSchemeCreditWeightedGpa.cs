using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using EduOS.Persistence.Context;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20261029190000_GradeSchemeCreditWeightedGpa")]
public partial class GradeSchemeCreditWeightedGpa : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "UseCreditWeightedGpa",
            table: "GradeSchemes",
            type: "bit",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "UseCreditWeightedGpa",
            table: "GradeSchemes");
    }
}
