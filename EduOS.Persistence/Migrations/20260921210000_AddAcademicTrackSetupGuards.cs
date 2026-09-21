using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260921210000_AddAcademicTrackSetupGuards")]
public partial class AddAcademicTrackSetupGuards : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1 FROM [dbo].[AcademicTracks]
    WHERE [IsDeleted] = 0
    GROUP BY [TenantId], [Code]
    HAVING COUNT_BIG(*) > 1
)
    THROW 51000, 'Cannot enforce academic track code uniqueness because duplicate active records exist.', 1;

IF EXISTS (
    SELECT 1 FROM [dbo].[AcademicTracks]
    WHERE [IsDeleted] = 0 AND [IsActive] = 1 AND [IsDefault] = 1
    GROUP BY [TenantId], [AcademicProgramId]
    HAVING COUNT_BIG(*) > 1
)
    THROW 51001, 'Cannot enforce one default academic track because duplicate defaults exist.', 1;");

        migrationBuilder.CreateIndex(
            name: "UX_AcademicTracks_Tenant_Code",
            table: "AcademicTracks",
            columns: new[] { "TenantId", "Code" },
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "UX_AcademicTracks_Tenant_DefaultScope",
            table: "AcademicTracks",
            columns: new[] { "TenantId", "AcademicProgramId" },
            unique: true,
            filter: "[IsDeleted] = 0 AND [IsActive] = 1 AND [IsDefault] = 1");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "UX_AcademicTracks_Tenant_Code", table: "AcademicTracks");
        migrationBuilder.DropIndex(name: "UX_AcademicTracks_Tenant_DefaultScope", table: "AcademicTracks");
    }
}
