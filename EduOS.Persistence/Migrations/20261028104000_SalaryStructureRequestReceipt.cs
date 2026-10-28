using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>New salary requests retain a unique replay receipt; old history remains untouched.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261028104000_SalaryStructureRequestReceipt")]
public sealed class SalaryStructureRequestReceipt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ClientRequestId", table: "SalaryStructures",
            type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "RequestFingerprint", table: "SalaryStructures",
            type: "nvarchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_SalaryStructures_TenantId_ClientRequestId",
            table: "SalaryStructures",
            columns: new[] { "TenantId", "ClientRequestId" },
            unique: true, filter: "[ClientRequestId] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_SalaryStructures_TenantId_ClientRequestId", "SalaryStructures");
        migrationBuilder.DropColumn("RequestFingerprint", "SalaryStructures");
        migrationBuilder.DropColumn("ClientRequestId", "SalaryStructures");
    }
}
