using System;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260908130000_AddStudentPromotionWorkflow")]
public partial class AddStudentPromotionWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "StudentPromotionRecords",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StudentId = table.Column<long>(type: "bigint", nullable: false),
                FromEnrollmentId = table.Column<long>(type: "bigint", nullable: false),
                ToEnrollmentId = table.Column<long>(type: "bigint", nullable: false),
                FromAcademicYearId = table.Column<long>(type: "bigint", nullable: false),
                ToAcademicYearId = table.Column<long>(type: "bigint", nullable: false),
                FromClassId = table.Column<long>(type: "bigint", nullable: false),
                ToClassId = table.Column<long>(type: "bigint", nullable: false),
                FromSectionId = table.Column<long>(type: "bigint", nullable: false),
                ToSectionId = table.Column<long>(type: "bigint", nullable: false),
                FromGroupId = table.Column<long>(type: "bigint", nullable: true),
                ToGroupId = table.Column<long>(type: "bigint", nullable: true),
                FromRoll = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                ToRoll = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Decision = table.Column<int>(type: "int", nullable: false),
                ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                ProcessedByUserId = table.Column<long>(type: "bigint", nullable: false),
                Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StudentPromotionRecords", x => x.Id);
                table.ForeignKey("FK_StudentPromotionRecords_AcademicYears_FromAcademicYearId",
                    x => x.FromAcademicYearId, "AcademicYears", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentPromotionRecords_AcademicYears_ToAcademicYearId",
                    x => x.ToAcademicYearId, "AcademicYears", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentPromotionRecords_Classes_FromClassId",
                    x => x.FromClassId, "Classes", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentPromotionRecords_Classes_ToClassId",
                    x => x.ToClassId, "Classes", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentPromotionRecords_Enrollments_FromEnrollmentId",
                    x => x.FromEnrollmentId, "Enrollments", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentPromotionRecords_Enrollments_ToEnrollmentId",
                    x => x.ToEnrollmentId, "Enrollments", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentPromotionRecords_Groups_FromGroupId",
                    x => x.FromGroupId, "Groups", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentPromotionRecords_Groups_ToGroupId",
                    x => x.ToGroupId, "Groups", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentPromotionRecords_Sections_FromSectionId",
                    x => x.FromSectionId, "Sections", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentPromotionRecords_Sections_ToSectionId",
                    x => x.ToSectionId, "Sections", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentPromotionRecords_Students_StudentId",
                    x => x.StudentId, "Students", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StudentPromotionRecords_Tenants_TenantId",
                                       x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_StudentPromotionRecords_FromAcademicYearId", "StudentPromotionRecords", "FromAcademicYearId");
        migrationBuilder.CreateIndex("IX_StudentPromotionRecords_FromClassId", "StudentPromotionRecords", "FromClassId");
        migrationBuilder.CreateIndex("IX_StudentPromotionRecords_FromEnrollmentId", "StudentPromotionRecords", "FromEnrollmentId");
        migrationBuilder.CreateIndex("IX_StudentPromotionRecords_FromGroupId", "StudentPromotionRecords", "FromGroupId");
        migrationBuilder.CreateIndex("IX_StudentPromotionRecords_FromSectionId", "StudentPromotionRecords", "FromSectionId");
        migrationBuilder.CreateIndex("IX_StudentPromotionRecords_PublicId", "StudentPromotionRecords", "PublicId",
            unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex("IX_StudentPromotionRecords_StudentId", "StudentPromotionRecords", "StudentId");
        migrationBuilder.CreateIndex("IX_StudentPromotionRecords_TenantId_ClientRequestId", "StudentPromotionRecords",
            new[] { "TenantId", "ClientRequestId" }, unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex("IX_StudentPromotionRecords_TenantId_FromEnrollmentId", "StudentPromotionRecords",
            new[] { "TenantId", "FromEnrollmentId" }, unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex("IX_StudentPromotionRecords_TenantId_StudentId_ProcessedAt", "StudentPromotionRecords",
            new[] { "TenantId", "StudentId", "ProcessedAt" });
        migrationBuilder.CreateIndex("IX_StudentPromotionRecords_ToAcademicYearId", "StudentPromotionRecords", "ToAcademicYearId");
        migrationBuilder.CreateIndex("IX_StudentPromotionRecords_ToClassId", "StudentPromotionRecords", "ToClassId");
        migrationBuilder.CreateIndex("IX_StudentPromotionRecords_ToEnrollmentId", "StudentPromotionRecords", "ToEnrollmentId");
        migrationBuilder.CreateIndex("IX_StudentPromotionRecords_ToGroupId", "StudentPromotionRecords", "ToGroupId");
        migrationBuilder.CreateIndex("IX_StudentPromotionRecords_ToSectionId", "StudentPromotionRecords", "ToSectionId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "StudentPromotionRecords");
    }
}
