using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260921170000_EnableCanonicalAcademicSetup")]
public partial class EnableCanonicalAcademicSetup : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ValidateTenantReference(migrationBuilder, "AcademicPrograms", "CampusId", "Campuses", true);
        ValidateTenantReference(migrationBuilder, "AcademicCurriculums", "EffectiveFromAcademicYearId", "AcademicYears", true);
        ValidateTenantReference(migrationBuilder, "AcademicCurriculums", "EffectiveToAcademicYearId", "AcademicYears", true);
        ValidateTenantReference(migrationBuilder, "AcademicBatches", "CampusId", "Campuses", false);
        ValidateTenantReference(migrationBuilder, "Rooms", "CampusId", "Campuses", true);
        ValidateTenantReference(migrationBuilder, "ProgramCampuses", "CampusId", "Campuses", false);

        migrationBuilder.AlterColumn<long>(
            name: "ClassId",
            table: "Subjects",
            type: "bigint",
            nullable: true,
            oldClrType: typeof(long),
            oldType: "bigint");

        migrationBuilder.CreateIndex(name: "IX_AcademicPrograms_CampusId", table: "AcademicPrograms", column: "CampusId");
        migrationBuilder.CreateIndex(name: "UX_AcademicPrograms_Tenant_Code", table: "AcademicPrograms", columns: new[] { "TenantId", "Code" }, unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex(name: "UX_AcademicLevels_Tenant_Program_Code", table: "AcademicLevels", columns: new[] { "TenantId", "AcademicProgramId", "Code" }, unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex(name: "UX_Subjects_Tenant_CanonicalCode", table: "Subjects", columns: new[] { "TenantId", "Code" }, unique: true, filter: "[IsDeleted] = 0 AND [ClassId] IS NULL");
        migrationBuilder.CreateIndex(name: "IX_AcademicCurriculums_EffectiveFromAcademicYearId", table: "AcademicCurriculums", column: "EffectiveFromAcademicYearId");
        migrationBuilder.CreateIndex(name: "IX_AcademicCurriculums_EffectiveToAcademicYearId", table: "AcademicCurriculums", column: "EffectiveToAcademicYearId");
        migrationBuilder.CreateIndex(name: "UX_AcademicCurriculums_Tenant_Code", table: "AcademicCurriculums", columns: new[] { "TenantId", "Code" }, unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex(name: "UX_AcademicCurriculums_Tenant_CurrentProgram", table: "AcademicCurriculums", columns: new[] { "TenantId", "AcademicProgramId" }, unique: true, filter: "[IsDeleted] = 0 AND [IsActive] = 1 AND [IsCurrent] = 1");
        migrationBuilder.CreateIndex(name: "UX_CurriculumSubjects_Tenant_Scope", table: "CurriculumSubjects", columns: new[] { "TenantId", "AcademicCurriculumId", "AcademicLevelId", "SubjectId", "AcademicTrackId", "MediumId" }, unique: true, filter: "[IsDeleted] = 0 AND [IsActive] = 1");
        migrationBuilder.CreateIndex(name: "IX_AcademicBatches_CampusId", table: "AcademicBatches", column: "CampusId");
        migrationBuilder.CreateIndex(name: "UX_AcademicBatches_Tenant_Year_Code", table: "AcademicBatches", columns: new[] { "TenantId", "AcademicYearId", "Code" }, unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex(name: "IX_Rooms_CampusId", table: "Rooms", column: "CampusId");
        migrationBuilder.CreateIndex(name: "UX_Rooms_Tenant_Code", table: "Rooms", columns: new[] { "TenantId", "Code" }, unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex(name: "IX_ProgramCampuses_CampusId", table: "ProgramCampuses", column: "CampusId");
        migrationBuilder.CreateIndex(name: "UX_ProgramCampuses_Tenant_Program_Campus", table: "ProgramCampuses", columns: new[] { "TenantId", "AcademicProgramId", "CampusId" }, unique: true, filter: "[IsDeleted] = 0");

        migrationBuilder.AddForeignKey(name: "FK_AcademicPrograms_Campuses_CampusId", table: "AcademicPrograms", column: "CampusId", principalTable: "Campuses", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_AcademicCurriculums_AcademicYears_EffectiveFromAcademicYearId", table: "AcademicCurriculums", column: "EffectiveFromAcademicYearId", principalTable: "AcademicYears", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_AcademicCurriculums_AcademicYears_EffectiveToAcademicYearId", table: "AcademicCurriculums", column: "EffectiveToAcademicYearId", principalTable: "AcademicYears", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_AcademicBatches_Campuses_CampusId", table: "AcademicBatches", column: "CampusId", principalTable: "Campuses", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_Rooms_Campuses_CampusId", table: "Rooms", column: "CampusId", principalTable: "Campuses", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_ProgramCampuses_Campuses_CampusId", table: "ProgramCampuses", column: "CampusId", principalTable: "Campuses", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_AcademicPrograms_Campuses_CampusId", table: "AcademicPrograms");
        migrationBuilder.DropForeignKey(name: "FK_AcademicCurriculums_AcademicYears_EffectiveFromAcademicYearId", table: "AcademicCurriculums");
        migrationBuilder.DropForeignKey(name: "FK_AcademicCurriculums_AcademicYears_EffectiveToAcademicYearId", table: "AcademicCurriculums");
        migrationBuilder.DropForeignKey(name: "FK_AcademicBatches_Campuses_CampusId", table: "AcademicBatches");
        migrationBuilder.DropForeignKey(name: "FK_Rooms_Campuses_CampusId", table: "Rooms");
        migrationBuilder.DropForeignKey(name: "FK_ProgramCampuses_Campuses_CampusId", table: "ProgramCampuses");

        migrationBuilder.DropIndex(name: "IX_AcademicPrograms_CampusId", table: "AcademicPrograms");
        migrationBuilder.DropIndex(name: "UX_AcademicPrograms_Tenant_Code", table: "AcademicPrograms");
        migrationBuilder.DropIndex(name: "UX_AcademicLevels_Tenant_Program_Code", table: "AcademicLevels");
        migrationBuilder.DropIndex(name: "UX_Subjects_Tenant_CanonicalCode", table: "Subjects");
        migrationBuilder.DropIndex(name: "IX_AcademicCurriculums_EffectiveFromAcademicYearId", table: "AcademicCurriculums");
        migrationBuilder.DropIndex(name: "IX_AcademicCurriculums_EffectiveToAcademicYearId", table: "AcademicCurriculums");
        migrationBuilder.DropIndex(name: "UX_AcademicCurriculums_Tenant_Code", table: "AcademicCurriculums");
        migrationBuilder.DropIndex(name: "UX_AcademicCurriculums_Tenant_CurrentProgram", table: "AcademicCurriculums");
        migrationBuilder.DropIndex(name: "UX_CurriculumSubjects_Tenant_Scope", table: "CurriculumSubjects");
        migrationBuilder.DropIndex(name: "IX_AcademicBatches_CampusId", table: "AcademicBatches");
        migrationBuilder.DropIndex(name: "UX_AcademicBatches_Tenant_Year_Code", table: "AcademicBatches");
        migrationBuilder.DropIndex(name: "IX_Rooms_CampusId", table: "Rooms");
        migrationBuilder.DropIndex(name: "UX_Rooms_Tenant_Code", table: "Rooms");
        migrationBuilder.DropIndex(name: "IX_ProgramCampuses_CampusId", table: "ProgramCampuses");
        migrationBuilder.DropIndex(name: "UX_ProgramCampuses_Tenant_Program_Campus", table: "ProgramCampuses");

        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [dbo].[Subjects] WHERE [ClassId] IS NULL) THROW 51000, 'Cannot restore required Subjects.ClassId while canonical subjects exist.', 1;");
        migrationBuilder.AlterColumn<long>(name: "ClassId", table: "Subjects", type: "bigint", nullable: false, oldClrType: typeof(long), oldType: "bigint", oldNullable: true);
    }

    private static void ValidateTenantReference(MigrationBuilder migrationBuilder, string table, string column, string principalTable, bool nullable)
    {
        var invalidReference = nullable
            ? $"r.[{column}] IS NOT NULL AND (p.[Id] IS NULL OR p.[TenantId] <> r.[TenantId])"
            : $"r.[{column}] IS NULL OR p.[Id] IS NULL OR p.[TenantId] <> r.[TenantId]";
        migrationBuilder.Sql($@"
IF EXISTS (
    SELECT 1
    FROM [dbo].[{table}] r
    LEFT JOIN [dbo].[{principalTable}] p ON p.[Id] = r.[{column}]
    WHERE {invalidReference}
)
    THROW 51000, 'Cannot add {table}.{column} integrity because orphaned or cross-tenant references exist.', 1;");
    }
}
