using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>Snapshot each run's explicitly selected calculation policy; no old totals are changed.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261028052500_PayrollCalendarDayProration")]
public sealed class PayrollCalendarDayProration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ProrationModeCode", table: "PayrollRuns",
            type: "nvarchar(30)", maxLength: 30, nullable: false,
            defaultValue: "FullMonthOnly");
        migrationBuilder.AddCheckConstraint(
            name: "CK_PayrollRuns_ProrationModeCode",
            table: "PayrollRuns",
            sql: "[ProrationModeCode] IN ('FullMonthOnly', 'CalendarDays')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_PayrollRuns_ProrationModeCode", "PayrollRuns");
        migrationBuilder.DropColumn("ProrationModeCode", "PayrollRuns");
    }
}
