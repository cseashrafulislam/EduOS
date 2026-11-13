using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>
/// Expand/contract transition: independent campus/class Sections without breaking
/// historic Section identifiers or existing batch-based enrolment references.
/// </summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261113050000_DecoupleSectionsFromBatches")]
public sealed class DecoupleSectionsFromBatches : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "CampusId", table: "AcademicSections",
            type: "bigint", nullable: true);
        migrationBuilder.AddColumn<long>(name: "AcademicLevelId", table: "AcademicSections",
            type: "bigint", nullable: true);

        // Read old Batch facts only once to backfill the new independent Section scope.
        // The historical FK is temporarily retained as a nullable compatibility reference.
        migrationBuilder.Sql(@"
UPDATE s
SET CampusId = b.CampusId, AcademicLevelId = b.AcademicLevelId
FROM dbo.AcademicSections AS s
INNER JOIN dbo.AcademicBatches AS b ON b.TenantId = s.TenantId AND b.Id = s.AcademicBatchId
WHERE s.CampusId IS NULL OR s.AcademicLevelId IS NULL;
IF EXISTS (SELECT 1 FROM dbo.AcademicSections WHERE CampusId IS NULL OR AcademicLevelId IS NULL)
    THROW 51045, 'AcademicSection class/campus could not be resolved from existing batch. Reconcile before migrating.', 1;
");
        migrationBuilder.DropForeignKey(name: "FK_AcademicSections_AcademicBatches_TenantId_AcademicBatchId",
            table: "AcademicSections");
        migrationBuilder.DropIndex(name: "IX_AcademicSections_TenantId_AcademicBatchId_Code",
            table: "AcademicSections");

        migrationBuilder.AlterColumn<long>(name: "CampusId", table: "AcademicSections",
            type: "bigint", nullable: false, oldClrType: typeof(long),
            oldType: "bigint", oldNullable: true);
        migrationBuilder.AlterColumn<long>(name: "AcademicLevelId", table: "AcademicSections",
            type: "bigint", nullable: false, oldClrType: typeof(long),
            oldType: "bigint", oldNullable: true);
        migrationBuilder.AlterColumn<long>(name: "AcademicBatchId", table: "AcademicSections",
            type: "bigint", nullable: true, oldClrType: typeof(long),
            oldType: "bigint");

        migrationBuilder.CreateIndex(name: "IX_AcademicSections_TenantId_AcademicBatchId_Code",
            table: "AcademicSections", columns: new[] { "TenantId", "AcademicBatchId", "Code" },
            unique: true, filter: "[AcademicBatchId] IS NOT NULL AND [IsDeleted] = 0");
        migrationBuilder.CreateIndex(name: "UX_AcademicSections_Campus_Level_Code",
            table: "AcademicSections", columns: new[] { "TenantId", "CampusId", "AcademicLevelId", "Code" },
            unique: true, filter: "[AcademicBatchId] IS NULL AND [IsDeleted] = 0");
        migrationBuilder.CreateIndex(name: "IX_AcademicSections_TenantId_CampusId",
            table: "AcademicSections", columns: new[] { "TenantId", "CampusId" });
        migrationBuilder.CreateIndex(name: "IX_AcademicSections_TenantId_AcademicLevelId",
            table: "AcademicSections", columns: new[] { "TenantId", "AcademicLevelId" });
        migrationBuilder.AddForeignKey(
            name: "FK_AcademicSections_AcademicBatches_TenantId_AcademicBatchId",
            table: "AcademicSections", columns: new[] { "TenantId", "AcademicBatchId" },
            principalTable: "AcademicBatches", principalColumns: new[] { "TenantId", "Id" },
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_AcademicSections_Campuses_TenantId_CampusId",
            table: "AcademicSections", columns: new[] { "TenantId", "CampusId" },
            principalTable: "Campuses", principalColumns: new[] { "TenantId", "Id" },
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_AcademicSections_AcademicLevels_TenantId_AcademicLevelId",
            table: "AcademicSections", columns: new[] { "TenantId", "AcademicLevelId" },
            principalTable: "AcademicLevels", principalColumns: new[] { "TenantId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // It is unsafe to re-require a Batch after independently created Sections exist.
        migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM dbo.AcademicSections WHERE AcademicBatchId IS NULL)
    THROW 51046, 'Cannot revert Section independence while batch-free sections exist.', 1;
");
        migrationBuilder.DropForeignKey("FK_AcademicSections_Campuses_TenantId_CampusId", "AcademicSections");
        migrationBuilder.DropForeignKey("FK_AcademicSections_AcademicLevels_TenantId_AcademicLevelId", "AcademicSections");
        migrationBuilder.DropForeignKey("FK_AcademicSections_AcademicBatches_TenantId_AcademicBatchId", "AcademicSections");
        migrationBuilder.DropIndex("UX_AcademicSections_Campus_Level_Code", "AcademicSections");
        migrationBuilder.DropIndex("IX_AcademicSections_TenantId_CampusId", "AcademicSections");
        migrationBuilder.DropIndex("IX_AcademicSections_TenantId_AcademicLevelId", "AcademicSections");
        migrationBuilder.DropIndex("IX_AcademicSections_TenantId_AcademicBatchId_Code", "AcademicSections");
        migrationBuilder.AlterColumn<long>(name: "AcademicBatchId", table: "AcademicSections",
            type: "bigint", nullable: false, oldClrType: typeof(long),
            oldType: "bigint", oldNullable: true);
        migrationBuilder.CreateIndex(name: "IX_AcademicSections_TenantId_AcademicBatchId_Code",
            table: "AcademicSections", columns: new[] { "TenantId", "AcademicBatchId", "Code" },
            unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.AddForeignKey(
            name: "FK_AcademicSections_AcademicBatches_TenantId_AcademicBatchId",
            table: "AcademicSections", columns: new[] { "TenantId", "AcademicBatchId" },
            principalTable: "AcademicBatches", principalColumns: new[] { "TenantId", "Id" },
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.DropColumn("CampusId", "AcademicSections");
        migrationBuilder.DropColumn("AcademicLevelId", "AcademicSections");
    }
}
