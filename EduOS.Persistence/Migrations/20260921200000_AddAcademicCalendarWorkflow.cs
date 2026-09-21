using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260921200000_AddAcademicCalendarWorkflow")]
public partial class AddAcademicCalendarWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ClientRequestId",
            table: "AcademicCalendarEvents",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<byte[]>(
            name: "RowVersion",
            table: "AcademicCalendarEvents",
            type: "rowversion",
            rowVersion: true,
            nullable: false);

        migrationBuilder.CreateTable(
            name: "AcademicCalendarPolicies",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AcademicYearId = table.Column<long>(type: "bigint", nullable: false),
                CampusId = table.Column<long>(type: "bigint", nullable: true),
                WeekendDaysMask = table.Column<int>(type: "int", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AcademicCalendarPolicies", x => x.Id);
                table.ForeignKey("FK_AcademicCalendarPolicies_AcademicYears_AcademicYearId", x => x.AcademicYearId, "AcademicYears", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AcademicCalendarPolicies_Campuses_CampusId", x => x.CampusId, "Campuses", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AcademicCalendarPolicies_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1
    FROM [dbo].[AcademicCalendarEvents] e
    LEFT JOIN [dbo].[Campuses] c ON c.[Id] = e.[CampusId]
    WHERE e.[CampusId] IS NOT NULL AND (c.[Id] IS NULL OR c.[TenantId] <> e.[TenantId])
)
    THROW 51000, 'Cannot add calendar campus integrity because orphaned or cross-tenant references exist.', 1;");

        migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1
    FROM [dbo].[AcademicCalendarEvents]
    WHERE [IsDeleted] = 0 AND [IsActive] = 1 AND [AcademicYearId] IS NOT NULL
    GROUP BY [TenantId], [AcademicYearId], [AcademicTermId], [CampusId], [Title], [StartDate], [EndDate]
    HAVING COUNT_BIG(*) > 1
)
    THROW 51001, 'Cannot enforce calendar event uniqueness because duplicate active scoped events exist.', 1;");

        migrationBuilder.CreateIndex(name: "IX_AcademicCalendarEvents_CampusId", table: "AcademicCalendarEvents", column: "CampusId");
        migrationBuilder.CreateIndex(name: "UX_AcademicCalendarEvents_Tenant_Request", table: "AcademicCalendarEvents", columns: new[] { "TenantId", "ClientRequestId" }, unique: true, filter: "[ClientRequestId] IS NOT NULL");
        migrationBuilder.CreateIndex(name: "IX_AcademicCalendarEvents_TenantId_AcademicYearId_CampusId_StartDate_EndDate", table: "AcademicCalendarEvents", columns: new[] { "TenantId", "AcademicYearId", "CampusId", "StartDate", "EndDate" });
        migrationBuilder.CreateIndex(name: "UX_AcademicCalendarEvents_Tenant_Scope_Title_Dates", table: "AcademicCalendarEvents", columns: new[] { "TenantId", "AcademicYearId", "AcademicTermId", "CampusId", "Title", "StartDate", "EndDate" }, unique: true, filter: "[IsDeleted] = 0 AND [IsActive] = 1 AND [AcademicYearId] IS NOT NULL");

        migrationBuilder.CreateIndex(name: "IX_AcademicCalendarPolicies_AcademicYearId", table: "AcademicCalendarPolicies", column: "AcademicYearId");
        migrationBuilder.CreateIndex(name: "IX_AcademicCalendarPolicies_CampusId", table: "AcademicCalendarPolicies", column: "CampusId");
        migrationBuilder.CreateIndex(name: "IX_AcademicCalendarPolicies_TenantId", table: "AcademicCalendarPolicies", column: "TenantId");
        migrationBuilder.CreateIndex(name: "UX_AcademicCalendarPolicies_Tenant_Request", table: "AcademicCalendarPolicies", columns: new[] { "TenantId", "ClientRequestId" }, unique: true);
        migrationBuilder.CreateIndex(name: "UX_AcademicCalendarPolicies_Tenant_Scope", table: "AcademicCalendarPolicies", columns: new[] { "TenantId", "AcademicYearId", "CampusId" }, unique: true, filter: "[IsDeleted] = 0 AND [IsActive] = 1");

        migrationBuilder.AddForeignKey(
            name: "FK_AcademicCalendarEvents_Campuses_CampusId",
            table: "AcademicCalendarEvents",
            column: "CampusId",
            principalTable: "Campuses",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_AcademicCalendarEvents_Campuses_CampusId", table: "AcademicCalendarEvents");
        migrationBuilder.DropTable(name: "AcademicCalendarPolicies");
        migrationBuilder.DropIndex(name: "IX_AcademicCalendarEvents_CampusId", table: "AcademicCalendarEvents");
        migrationBuilder.DropIndex(name: "UX_AcademicCalendarEvents_Tenant_Request", table: "AcademicCalendarEvents");
        migrationBuilder.DropIndex(name: "IX_AcademicCalendarEvents_TenantId_AcademicYearId_CampusId_StartDate_EndDate", table: "AcademicCalendarEvents");
        migrationBuilder.DropIndex(name: "UX_AcademicCalendarEvents_Tenant_Scope_Title_Dates", table: "AcademicCalendarEvents");
        migrationBuilder.DropColumn(name: "ClientRequestId", table: "AcademicCalendarEvents");
        migrationBuilder.DropColumn(name: "RowVersion", table: "AcademicCalendarEvents");
    }
}
