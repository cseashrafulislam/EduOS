using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Independent tenant/campus facility space registry, not a duplicate academic/hostel/inventory location.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261101010000_V3FacilitySpaceRegistry")]
public sealed class V3FacilitySpaceRegistry : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "FacilitySpaces",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CampusId = table.Column<long>(type: "bigint", nullable: false),
                Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FacilitySpaces", x => x.Id);
                table.UniqueConstraint("AK_FacilitySpaces_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey("FK_FacilitySpaces_Tenants_TenantId", x => x.TenantId,
                    "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_FacilitySpaces_Campuses_TenantId_CampusId",
                    x => new { x.TenantId, x.CampusId }, "Campuses", new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_FacilitySpaces_Code", "LEN(LTRIM(RTRIM([Code]))) > 0 AND LEN(LTRIM(RTRIM([Name]))) > 0");
            });
        migrationBuilder.CreateIndex("IX_FacilitySpaces_TenantId_CampusId", "FacilitySpaces",
            new[] { "TenantId", "CampusId" });
        migrationBuilder.CreateIndex("IX_FacilitySpaces_TenantId_CampusId_IsActive_Name", "FacilitySpaces",
            new[] { "TenantId", "CampusId", "IsActive", "Name" });
        migrationBuilder.CreateIndex("IX_FacilitySpaces_TenantId_CampusId_Code", "FacilitySpaces",
            new[] { "TenantId", "CampusId", "Code" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(name: "FacilitySpaces");
}
