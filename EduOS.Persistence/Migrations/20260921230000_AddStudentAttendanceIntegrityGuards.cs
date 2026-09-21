using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260921230000_AddStudentAttendanceIntegrityGuards")]
public partial class AddStudentAttendanceIntegrityGuards : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [dbo].[StudentAttendances] WHERE [Status] NOT IN ('Present','Absent','Late','Leave'))
    THROW 51000, 'Cannot enforce attendance status integrity because invalid statuses exist.', 1;
IF EXISTS (SELECT 1 FROM [dbo].[StudentAttendances] WHERE [OutTime] IS NOT NULL AND [InTime] IS NOT NULL AND [OutTime] < [InTime])
    THROW 51001, 'Cannot enforce attendance time integrity because invalid time ranges exist.', 1;
IF EXISTS (
    SELECT 1 FROM [dbo].[StudentAttendances]
    WHERE [IsDeleted] = 0
    GROUP BY [TenantId], [StudentId], CONVERT(date, [Date])
    HAVING COUNT_BIG(*) > 1)
    THROW 51002, 'Cannot enforce daily attendance uniqueness because duplicate active rows exist.', 1;");

        migrationBuilder.AlterColumn<DateTime>(name: "Date", table: "StudentAttendances", type: "date", nullable: false, oldClrType: typeof(DateTime), oldType: "datetime2");
        migrationBuilder.AlterColumn<string>(name: "Status", table: "StudentAttendances", type: "nvarchar(20)", maxLength: 20, nullable: false, oldClrType: typeof(string), oldType: "nvarchar(500)", oldMaxLength: 500);

        migrationBuilder.CreateIndex(name: "IX_StudentAttendances_Tenant_Roster_Date", table: "StudentAttendances", columns: new[] { "TenantId", "ClassId", "SectionId", "Date" });
        migrationBuilder.CreateIndex(name: "UX_StudentAttendances_Tenant_Student_Date", table: "StudentAttendances", columns: new[] { "TenantId", "StudentId", "Date" }, unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.AddCheckConstraint(name: "CK_StudentAttendances_Status", table: "StudentAttendances", sql: "[Status] IN ('Present','Absent','Late','Leave')");
        migrationBuilder.AddCheckConstraint(name: "CK_StudentAttendances_TimeRange", table: "StudentAttendances", sql: "[OutTime] IS NULL OR [InTime] IS NULL OR [OutTime] >= [InTime]");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "CK_StudentAttendances_Status", table: "StudentAttendances");
        migrationBuilder.DropCheckConstraint(name: "CK_StudentAttendances_TimeRange", table: "StudentAttendances");
        migrationBuilder.DropIndex(name: "IX_StudentAttendances_Tenant_Roster_Date", table: "StudentAttendances");
        migrationBuilder.DropIndex(name: "UX_StudentAttendances_Tenant_Student_Date", table: "StudentAttendances");
        migrationBuilder.AlterColumn<DateTime>(name: "Date", table: "StudentAttendances", type: "datetime2", nullable: false, oldClrType: typeof(DateTime), oldType: "date");
        migrationBuilder.AlterColumn<string>(name: "Status", table: "StudentAttendances", type: "nvarchar(500)", maxLength: 500, nullable: false, oldClrType: typeof(string), oldType: "nvarchar(20)", oldMaxLength: 20);
    }
}
