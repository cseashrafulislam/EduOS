using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>Extend canonical LMS participation to employees without fabricating student identities.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261109050000_TeacherCpdCanonicalEnrollment")]
public sealed class TeacherCpdCanonicalEnrollment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("IsTeacherCpd", "Courses", type: "bit", nullable: false, defaultValue: false);
        migrationBuilder.DropForeignKey("FK_CourseEnrollments_Students_TenantId_StudentId", "CourseEnrollments");
        migrationBuilder.DropForeignKey("FK_LmsCompletionCertificates_Students_TenantId_StudentId", "LmsCompletionCertificates");
        migrationBuilder.DropIndex("IX_CourseEnrollments_TenantId_CourseId_StudentId", "CourseEnrollments");
        migrationBuilder.DropIndex("IX_CourseEnrollments_TenantId_StudentId", "CourseEnrollments");
        migrationBuilder.DropIndex("IX_LmsCompletionCertificates_TenantId_StudentId_IssuedAt", "LmsCompletionCertificates");
        migrationBuilder.AlterColumn<long>("StudentId", "CourseEnrollments", type: "bigint", nullable: true, oldClrType: typeof(long), oldType: "bigint");
        migrationBuilder.AlterColumn<long>("StudentId", "LmsCompletionCertificates", type: "bigint", nullable: true, oldClrType: typeof(long), oldType: "bigint");
        migrationBuilder.AddColumn<long>("EmployeeId", "CourseEnrollments", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<long>("EmployeeId", "LmsCompletionCertificates", type: "bigint", nullable: true);
        migrationBuilder.AddForeignKey("FK_CourseEnrollments_Students_TenantId_StudentId", "CourseEnrollments",
            new[] { "TenantId", "StudentId" }, "Students", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("FK_LmsCompletionCertificates_Students_TenantId_StudentId", "LmsCompletionCertificates",
            new[] { "TenantId", "StudentId" }, "Students", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        migrationBuilder.CreateIndex("IX_CourseEnrollments_TenantId_EmployeeId", "CourseEnrollments", new[] { "TenantId", "EmployeeId" });
        migrationBuilder.CreateIndex("IX_CourseEnrollments_TenantId_StudentId", "CourseEnrollments", new[] { "TenantId", "StudentId" });
        migrationBuilder.CreateIndex("IX_LmsCompletionCertificates_TenantId_StudentId_IssuedAt", "LmsCompletionCertificates", new[] { "TenantId", "StudentId", "IssuedAt" });
        migrationBuilder.CreateIndex("IX_LmsCompletionCertificates_TenantId_EmployeeId", "LmsCompletionCertificates", new[] { "TenantId", "EmployeeId" });
        migrationBuilder.AddForeignKey("FK_CourseEnrollments_Employees_TenantId_EmployeeId", "CourseEnrollments",
            new[] { "TenantId", "EmployeeId" }, "Employees", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("FK_LmsCompletionCertificates_Employees_TenantId_EmployeeId", "LmsCompletionCertificates",
            new[] { "TenantId", "EmployeeId" }, "Employees", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        migrationBuilder.CreateIndex("IX_CourseEnrollments_TenantId_CourseId_StudentId", "CourseEnrollments",
            new[] { "TenantId", "CourseId", "StudentId" }, unique: true,
            filter: "[StudentEnrollmentId] IS NULL AND [StudentId] IS NOT NULL AND [IsDeleted] = 0");
        migrationBuilder.CreateIndex("IX_CourseEnrollments_TenantId_CourseId_EmployeeId", "CourseEnrollments",
            new[] { "TenantId", "CourseId", "EmployeeId" }, unique: true,
            filter: "[EmployeeId] IS NOT NULL AND [IsDeleted] = 0");
        migrationBuilder.CreateIndex("IX_LmsCompletionCertificates_TenantId_EmployeeId_IssuedAt", "LmsCompletionCertificates",
            new[] { "TenantId", "EmployeeId", "IssuedAt" });
        migrationBuilder.AddCheckConstraint("CK_CourseEnrollments_Participant", "CourseEnrollments",
            "([StudentId] IS NOT NULL AND [EmployeeId] IS NULL) OR ([StudentId] IS NULL AND [EmployeeId] IS NOT NULL AND [StudentEnrollmentId] IS NULL)");
        migrationBuilder.AddCheckConstraint("CK_LmsCompletionCertificates_Participant", "LmsCompletionCertificates",
            "([StudentId] IS NOT NULL AND [EmployeeId] IS NULL) OR ([StudentId] IS NULL AND [EmployeeId] IS NOT NULL)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM dbo.CourseEnrollments WHERE EmployeeId IS NOT NULL) OR EXISTS (SELECT 1 FROM dbo.LmsCompletionCertificates WHERE EmployeeId IS NOT NULL) OR EXISTS (SELECT 1 FROM dbo.Courses WHERE IsTeacherCpd = 1) THROW 51042, 'Remove CPD evidence through an approved retention migration before rollback.', 1;");
        migrationBuilder.DropCheckConstraint("CK_LmsCompletionCertificates_Participant", "LmsCompletionCertificates");
        migrationBuilder.DropCheckConstraint("CK_CourseEnrollments_Participant", "CourseEnrollments");
        migrationBuilder.DropForeignKey("FK_CourseEnrollments_Employees_TenantId_EmployeeId", "CourseEnrollments");
        migrationBuilder.DropForeignKey("FK_LmsCompletionCertificates_Employees_TenantId_EmployeeId", "LmsCompletionCertificates");
        migrationBuilder.DropForeignKey("FK_CourseEnrollments_Students_TenantId_StudentId", "CourseEnrollments");
        migrationBuilder.DropForeignKey("FK_LmsCompletionCertificates_Students_TenantId_StudentId", "LmsCompletionCertificates");
        migrationBuilder.DropIndex("IX_CourseEnrollments_TenantId_CourseId_EmployeeId", "CourseEnrollments");
        migrationBuilder.DropIndex("IX_CourseEnrollments_TenantId_EmployeeId", "CourseEnrollments");
        migrationBuilder.DropIndex("IX_LmsCompletionCertificates_TenantId_EmployeeId", "LmsCompletionCertificates");
        migrationBuilder.DropIndex("IX_LmsCompletionCertificates_TenantId_EmployeeId_IssuedAt", "LmsCompletionCertificates");
        migrationBuilder.DropIndex("IX_CourseEnrollments_TenantId_CourseId_StudentId", "CourseEnrollments");
        migrationBuilder.DropIndex("IX_CourseEnrollments_TenantId_StudentId", "CourseEnrollments");
        migrationBuilder.DropIndex("IX_LmsCompletionCertificates_TenantId_StudentId_IssuedAt", "LmsCompletionCertificates");
        migrationBuilder.DropColumn("EmployeeId", "CourseEnrollments");
        migrationBuilder.DropColumn("EmployeeId", "LmsCompletionCertificates");
        migrationBuilder.DropColumn("IsTeacherCpd", "Courses");
        migrationBuilder.AlterColumn<long>("StudentId", "CourseEnrollments", type: "bigint", nullable: false, oldClrType: typeof(long), oldType: "bigint", oldNullable: true);
        migrationBuilder.AlterColumn<long>("StudentId", "LmsCompletionCertificates", type: "bigint", nullable: false, oldClrType: typeof(long), oldType: "bigint", oldNullable: true);
        migrationBuilder.AddForeignKey("FK_CourseEnrollments_Students_TenantId_StudentId", "CourseEnrollments",
            new[] { "TenantId", "StudentId" }, "Students", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("FK_LmsCompletionCertificates_Students_TenantId_StudentId", "LmsCompletionCertificates",
            new[] { "TenantId", "StudentId" }, "Students", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        migrationBuilder.CreateIndex("IX_CourseEnrollments_TenantId_CourseId_StudentId", "CourseEnrollments",
            new[] { "TenantId", "CourseId", "StudentId" }, unique: true,
            filter: "[StudentEnrollmentId] IS NULL AND [IsDeleted] = 0");
        migrationBuilder.CreateIndex("IX_CourseEnrollments_TenantId_StudentId", "CourseEnrollments", new[] { "TenantId", "StudentId" });
        migrationBuilder.CreateIndex("IX_LmsCompletionCertificates_TenantId_StudentId_IssuedAt", "LmsCompletionCertificates", new[] { "TenantId", "StudentId", "IssuedAt" });
    }
}
