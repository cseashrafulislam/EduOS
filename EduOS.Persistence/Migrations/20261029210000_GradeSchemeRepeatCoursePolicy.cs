using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>
/// No silent historic grade changes. Existing schemes reject duplicate courses;
/// new immutable credit schemes may opt into latest or highest grade selection.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261029210000_GradeSchemeRepeatCoursePolicy")]
public sealed class GradeSchemeRepeatCoursePolicy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("RepeatCourseGradePolicy", "GradeSchemes",
            type: "int", nullable: false, defaultValue: 0);

        migrationBuilder.AddCheckConstraint("CK_GradeSchemes_RepeatCourseGradePolicy",
            "GradeSchemes", "[RepeatCourseGradePolicy] IN (0,1,2)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_GradeSchemes_RepeatCourseGradePolicy", "GradeSchemes");
        migrationBuilder.DropColumn("RepeatCourseGradePolicy", "GradeSchemes");
    }
}
