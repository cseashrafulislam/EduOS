using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20261011181000_TenantRegionalDateFormat")]
public sealed class TenantRegionalDateFormat : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DateFormat",
            table: "Tenants",
            type: "nvarchar(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "dd-MM-yyyy");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DateFormat", table: "Tenants");
    }
}
