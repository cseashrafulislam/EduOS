using System;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260903120000_AddPlatformCatalog")]
public partial class AddPlatformCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "InstitutionTypeDefinitions",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                NameBangla = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                AcademicCycleType = table.Column<int>(type: "int", nullable: false),
                TerminologyJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                DefaultSettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                DisplayOrder = table.Column<int>(type: "int", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                IsPubliclyVisible = table.Column<bool>(type: "bit", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InstitutionTypeDefinitions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ProductModules",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                NameBangla = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                IconName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                RoutePrefix = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                DisplayOrder = table.Column<int>(type: "int", nullable: false),
                IsCore = table.Column<bool>(type: "bit", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProductModules", x => x.Id);
            });

        migrationBuilder.AddColumn<long>(
            name: "InstitutionTypeDefinitionId",
            table: "Tenants",
            type: "bigint",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "InstitutionTypeModules",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                InstitutionTypeDefinitionId = table.Column<long>(type: "bigint", nullable: false),
                ProductModuleId = table.Column<long>(type: "bigint", nullable: false),
                IsRequired = table.Column<bool>(type: "bit", nullable: false),
                IsEnabledByDefault = table.Column<bool>(type: "bit", nullable: false),
                DisplayOrder = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InstitutionTypeModules", x => x.Id);
                table.ForeignKey(
                    name: "FK_InstitutionTypeModules_InstitutionTypeDefinitions_InstitutionTypeDefinitionId",
                    column: x => x.InstitutionTypeDefinitionId,
                    principalTable: "InstitutionTypeDefinitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_InstitutionTypeModules_ProductModules_ProductModuleId",
                    column: x => x.ProductModuleId,
                    principalTable: "ProductModules",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Tenants_InstitutionTypeDefinitionId",
            table: "Tenants",
            column: "InstitutionTypeDefinitionId");

        migrationBuilder.CreateIndex(
            name: "IX_InstitutionTypeDefinitions_Code",
            table: "InstitutionTypeDefinitions",
            column: "Code",
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_InstitutionTypeDefinitions_IsActive_IsPubliclyVisible_DisplayOrder",
            table: "InstitutionTypeDefinitions",
            columns: new[] { "IsActive", "IsPubliclyVisible", "DisplayOrder" });

        migrationBuilder.CreateIndex(
            name: "IX_InstitutionTypeModules_InstitutionTypeDefinitionId_ProductModuleId",
            table: "InstitutionTypeModules",
            columns: new[] { "InstitutionTypeDefinitionId", "ProductModuleId" },
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_InstitutionTypeModules_ProductModuleId",
            table: "InstitutionTypeModules",
            column: "ProductModuleId");

        migrationBuilder.CreateIndex(
            name: "IX_ProductModules_Code",
            table: "ProductModules",
            column: "Code",
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_ProductModules_IsActive_Category_DisplayOrder",
            table: "ProductModules",
            columns: new[] { "IsActive", "Category", "DisplayOrder" });

        migrationBuilder.AddForeignKey(
            name: "FK_Tenants_InstitutionTypeDefinitions_InstitutionTypeDefinitionId",
            table: "Tenants",
            column: "InstitutionTypeDefinitionId",
            principalTable: "InstitutionTypeDefinitions",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Tenants_InstitutionTypeDefinitions_InstitutionTypeDefinitionId",
            table: "Tenants");

        migrationBuilder.DropTable(name: "InstitutionTypeModules");
        migrationBuilder.DropTable(name: "ProductModules");

        migrationBuilder.DropIndex(
            name: "IX_Tenants_InstitutionTypeDefinitionId",
            table: "Tenants");

        migrationBuilder.DropColumn(
            name: "InstitutionTypeDefinitionId",
            table: "Tenants");

        migrationBuilder.DropTable(name: "InstitutionTypeDefinitions");
    }
}
