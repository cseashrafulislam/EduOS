using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20261028055100_EmployeeContractTenantForeignKey")]
public sealed class EmployeeContractTenantForeignKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Additional ownership safeguard; no existing contract row is rewritten.
        migrationBuilder.AddForeignKey(
            name: "FK_EmployeeContracts_Tenants_TenantId",
            table: "EmployeeContracts",
            column: "TenantId",
            principalTable: "Tenants",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_EmployeeContracts_Tenants_TenantId",
            table: "EmployeeContracts");
    }
}
