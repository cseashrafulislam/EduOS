using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations
{
    /// <summary>Adds a nullable, tenant-linked source key for independently posted fine invoices.</summary>
    public partial class V1SeparateFineInvoicePosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "StudentFineId",
                table: "StudentInvoiceLines",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentInvoiceLines_TenantId_StudentFineId",
                table: "StudentInvoiceLines",
                columns: new[] { "TenantId", "StudentFineId" },
                unique: true,
                filter: "[StudentFineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StudentFines_TenantId_FineDate_Id",
                table: "StudentFines",
                columns: new[] { "TenantId", "FineDate", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_StudentInvoiceLines_StudentFines_TenantId_StudentFineId",
                table: "StudentInvoiceLines",
                columns: new[] { "TenantId", "StudentFineId" },
                principalTable: "StudentFines",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StudentInvoiceLines_StudentFines_TenantId_StudentFineId",
                table: "StudentInvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_StudentInvoiceLines_TenantId_StudentFineId",
                table: "StudentInvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_StudentFines_TenantId_FineDate_Id",
                table: "StudentFines");

            migrationBuilder.DropColumn(
                name: "StudentFineId",
                table: "StudentInvoiceLines");
        }
    }
}
