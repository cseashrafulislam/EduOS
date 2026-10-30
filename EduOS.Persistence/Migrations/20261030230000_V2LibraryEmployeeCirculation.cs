using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V2LibraryEmployeeCirculation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "StudentId",
                table: "BookIssues",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "EmployeeId",
                table: "BookIssues",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookIssues_LibraryEmployeeLoans",
                table: "BookIssues",
                columns: new[] { "TenantId", "EmployeeId", "State", "Id" },
                filter: "[EmployeeId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BookIssue_OneBorrower",
                table: "BookIssues",
                sql: "([StudentId] IS NOT NULL AND [EmployeeId] IS NULL) OR ([StudentId] IS NULL AND [EmployeeId] IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_BookIssues_Employees_TenantId_EmployeeId",
                table: "BookIssues",
                columns: new[] { "TenantId", "EmployeeId" },
                principalTable: "Employees",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No schema downgrade may erase an employee borrower from posted history.
            // Require a reviewed forward fix or an approved database recovery plan.
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [BookIssues] WHERE [EmployeeId] IS NOT NULL OR [StudentId] IS NULL) THROW 51001, 'Library employee loans prevent schema rollback; preserve their history.', 1;");
            migrationBuilder.DropForeignKey(
                name: "FK_BookIssues_Employees_TenantId_EmployeeId",
                table: "BookIssues");

            migrationBuilder.DropIndex(
                name: "IX_BookIssues_LibraryEmployeeLoans",
                table: "BookIssues");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BookIssue_OneBorrower",
                table: "BookIssues");

            migrationBuilder.DropColumn(
                name: "EmployeeId",
                table: "BookIssues");

            migrationBuilder.AlterColumn<long>(
                name: "StudentId",
                table: "BookIssues",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}
