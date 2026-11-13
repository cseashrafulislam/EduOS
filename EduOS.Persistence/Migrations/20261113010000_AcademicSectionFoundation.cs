using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>Additive cohort/section split. Old batch foreign keys remain valid during the transition.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261113010000_AcademicSectionFoundation")]
public sealed class AcademicSectionFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "AcademicSections", columns: table => new
        {
            Id = table.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            TenantId = table.Column<long>("bigint", nullable: false),
            AcademicBatchId = table.Column<long>("bigint", nullable: false),
            ClientRequestId = table.Column<Guid>("uniqueidentifier", nullable: true),
            Name = table.Column<string>("nvarchar(150)", maxLength: 150, nullable: false),
            Code = table.Column<string>("nvarchar(50)", maxLength: 50, nullable: false),
            Capacity = table.Column<int>("int", nullable: false),
            IsLegacyDefault = table.Column<bool>("bit", nullable: false),
            IsActive = table.Column<bool>("bit", nullable: false),
            DisplayOrder = table.Column<int>("int", nullable: false),
            CreatedAt = table.Column<DateTime>("datetime2", nullable: false),
            UpdatedAt = table.Column<DateTime>("datetime2", nullable: true),
            CreatedBy = table.Column<long>("bigint", nullable: true),
            UpdatedBy = table.Column<long>("bigint", nullable: true),
            IsDeleted = table.Column<bool>("bit", nullable: false),
            DeletedAt = table.Column<DateTime>("datetime2", nullable: true),
            DeletedBy = table.Column<long>("bigint", nullable: true),
            RowVersion = table.Column<byte[]>("rowversion", rowVersion: true, nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_AcademicSections", x => x.Id);
            table.UniqueConstraint("AK_AcademicSections_TenantId_Id", x => new { x.TenantId, x.Id });
            table.CheckConstraint("CK_AcademicSections_Capacity", "[Capacity] BETWEEN 0 AND 100000");
            table.ForeignKey("FK_AcademicSections_Tenants_TenantId", x => x.TenantId,
                "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_AcademicSections_AcademicBatches_TenantId_AcademicBatchId",
                x => new { x.TenantId, x.AcademicBatchId }, "AcademicBatches",
                new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_AcademicSections_TenantId_AcademicBatchId_Code",
            "AcademicSections", new[] { "TenantId", "AcademicBatchId", "Code" }, unique: true,
            filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex("IX_AcademicSections_TenantId_ClientRequestId",
            "AcademicSections", new[] { "TenantId", "ClientRequestId" }, unique: true,
            filter: "[ClientRequestId] IS NOT NULL");
        migrationBuilder.CreateIndex("UX_AcademicSections_LegacyDefault",
            "AcademicSections", new[] { "TenantId", "AcademicBatchId" }, unique: true,
            filter: "[IsLegacyDefault] = 1 AND [IsDeleted] = 0");

        foreach (var table in new[] { "StudentEnrollments", "SubjectOfferings", "AttendanceSessions",
            "ResultPublications", "FeeStructures" })
        {
            migrationBuilder.AddColumn<long>("AcademicSectionId", table, type: "bigint", nullable: true);
            migrationBuilder.CreateIndex("IX_" + table + "_TenantId_AcademicSectionId", table,
                new[] { "TenantId", "AcademicSectionId" });
            migrationBuilder.AddForeignKey(
                name: "FK_" + table + "_AcademicSections_TenantId_AcademicSectionId",
                table: table,
                columns: new[] { "TenantId", "AcademicSectionId" },
                principalTable: "AcademicSections",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        // Preserve every old batch-as-section as one immutable compatibility section.
        // No dependent row is rewritten by this schema migration; the resumable backfill
        // script runs after deployment with batches small enough for production load.
        migrationBuilder.Sql(@"
INSERT INTO dbo.AcademicSections
    (TenantId, AcademicBatchId, ClientRequestId, Name, Code, Capacity, IsLegacyDefault,
     IsActive, DisplayOrder, CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, IsDeleted, DeletedAt, DeletedBy)
SELECT b.TenantId, b.Id, NULL, b.Name, b.Code, b.Capacity, 1,
       b.IsActive, b.DisplayOrder, b.CreatedAt, b.UpdatedAt, b.CreatedBy, b.UpdatedBy,
       b.IsDeleted, b.DeletedAt, b.DeletedBy
FROM dbo.AcademicBatches b
WHERE NOT EXISTS (SELECT 1 FROM dbo.AcademicSections s
    WHERE s.TenantId = b.TenantId AND s.AcademicBatchId = b.Id AND s.IsLegacyDefault = 1);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in new[] { "StudentEnrollments", "SubjectOfferings", "AttendanceSessions",
            "ResultPublications", "FeeStructures" })
        {
            migrationBuilder.DropForeignKey("FK_" + table + "_AcademicSections_TenantId_AcademicSectionId", table);
            migrationBuilder.DropIndex("IX_" + table + "_TenantId_AcademicSectionId", table);
            migrationBuilder.DropColumn("AcademicSectionId", table);
        }
        migrationBuilder.DropTable("AcademicSections");
    }
}
