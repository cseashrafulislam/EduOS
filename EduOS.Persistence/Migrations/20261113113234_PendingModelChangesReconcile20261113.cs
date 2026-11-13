using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PendingModelChangesReconcile20261113 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AcademicSections_TenantId_CampusId",
                table: "AcademicSections");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AcademicSections_TenantId_CampusId",
                table: "AcademicSections",
                columns: new[] { "TenantId", "CampusId" });
        }
    }
}
