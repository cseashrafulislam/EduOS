using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>Additive C1 explanation migration, safe after earlier adaptive migrations.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261109083000_AdaptiveQuizExplanations")]
public sealed class AdaptiveQuizExplanations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // A short-lived earlier C1 deployment may already contain this column.
        // Preserve that data and register this separate migration in either case.
        migrationBuilder.Sql("IF COL_LENGTH(N'dbo.QuizQuestions', N'Explanation') IS NULL ALTER TABLE dbo.QuizQuestions ADD [Explanation] nvarchar(2000) NULL;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM dbo.QuizQuestions WHERE Explanation IS NOT NULL) THROW 51063, 'Quiz explanation evidence must be retained before rollback.', 1;");
        migrationBuilder.DropColumn("Explanation", "QuizQuestions");
    }
}
