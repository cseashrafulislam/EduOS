using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>Final class-owned Section model. No BatchId or synthetic cohort defaults.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261113050000_DecoupleSectionsFromBatches")]
public sealed class DecoupleSectionsFromBatches : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>("CampusId", "AcademicSections", "bigint", nullable: true);
        migrationBuilder.AddColumn<long>("AcademicLevelId", "AcademicSections", "bigint", nullable: true);

        // Compatible with an existing empty DB or a previously generated placeholder:
        // preserve referenced sections and recover their class/campus only from real FK.
        migrationBuilder.Sql(@"
UPDATE s SET CampusId = b.CampusId, AcademicLevelId = b.AcademicLevelId
FROM dbo.AcademicSections s
JOIN dbo.AcademicBatches b ON b.TenantId = s.TenantId AND b.Id = s.AcademicBatchId
WHERE s.CampusId IS NULL OR s.AcademicLevelId IS NULL;
IF EXISTS (SELECT 1 FROM dbo.AcademicSections WHERE CampusId IS NULL OR AcademicLevelId IS NULL)
    THROW 51045, 'Existing Section has no valid Campus/Class; reconcile before migration.', 1;
IF EXISTS (
    SELECT 1 FROM dbo.AcademicSections
    WHERE IsDeleted = 0
    GROUP BY TenantId, CampusId, AcademicLevelId, Code HAVING COUNT_BIG(*) > 1
)
    THROW 51046, 'Duplicate Section code for the same Campus/Class; reconcile before migration.', 1;
");

        migrationBuilder.DropForeignKey(
            "FK_AcademicSections_AcademicBatches_TenantId_AcademicBatchId", "AcademicSections");
        migrationBuilder.DropIndex("IX_AcademicSections_TenantId_AcademicBatchId_Code", "AcademicSections");

        migrationBuilder.AlterColumn<long>("CampusId", "AcademicSections", "bigint",
            nullable: false, oldClrType: typeof(long), oldType: "bigint", oldNullable: true);
        migrationBuilder.AlterColumn<long>("AcademicLevelId", "AcademicSections", "bigint",
            nullable: false, oldClrType: typeof(long), oldType: "bigint", oldNullable: true);

        migrationBuilder.CreateIndex("UX_AcademicSections_Campus_Level_Code", "AcademicSections",
            new[] { "TenantId", "CampusId", "AcademicLevelId", "Code" },
            unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex("IX_AcademicSections_TenantId_CampusId", "AcademicSections",
            new[] { "TenantId", "CampusId" });
        migrationBuilder.CreateIndex("IX_AcademicSections_TenantId_AcademicLevelId", "AcademicSections",
            new[] { "TenantId", "AcademicLevelId" });

        migrationBuilder.AddForeignKey(
            name: "FK_AcademicSections_Campuses_TenantId_CampusId", table: "AcademicSections",
            columns: new[] { "TenantId", "CampusId" }, principalTable: "Campuses",
            principalColumns: new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_AcademicSections_AcademicLevels_TenantId_AcademicLevelId",
            table: "AcademicSections", columns: new[] { "TenantId", "AcademicLevelId" },
            principalTable: "AcademicLevels", principalColumns: new[] { "TenantId", "Id" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.DropColumn("AcademicBatchId", "AcademicSections");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM dbo.AcademicSections)
    THROW 51047, 'Cannot revert independent Academic Sections while Section rows exist.', 1;
");
        migrationBuilder.DropForeignKey("FK_AcademicSections_Campuses_TenantId_CampusId", "AcademicSections");
        migrationBuilder.DropForeignKey("FK_AcademicSections_AcademicLevels_TenantId_AcademicLevelId", "AcademicSections");
        migrationBuilder.DropIndex("UX_AcademicSections_Campus_Level_Code", "AcademicSections");
        migrationBuilder.DropIndex("IX_AcademicSections_TenantId_CampusId", "AcademicSections");
        migrationBuilder.DropIndex("IX_AcademicSections_TenantId_AcademicLevelId", "AcademicSections");
        migrationBuilder.AddColumn<long>("AcademicBatchId", "AcademicSections", "bigint",
            nullable: false, defaultValue: 0L);
        migrationBuilder.CreateIndex("IX_AcademicSections_TenantId_AcademicBatchId_Code",
            "AcademicSections", new[] { "TenantId", "AcademicBatchId", "Code" },
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
