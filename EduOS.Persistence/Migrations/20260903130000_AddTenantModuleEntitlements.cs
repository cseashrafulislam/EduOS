using System;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20260903130000_AddTenantModuleEntitlements")]
public partial class AddTenantModuleEntitlements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProductModuleFeatures",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ProductModuleId = table.Column<long>(type: "bigint", nullable: false),
                FeatureId = table.Column<long>(type: "bigint", nullable: false),
                IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                DisplayOrder = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProductModuleFeatures", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProductModuleFeatures_Features_FeatureId",
                    column: x => x.FeatureId,
                    principalTable: "Features",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ProductModuleFeatures_ProductModules_ProductModuleId",
                    column: x => x.ProductModuleId,
                    principalTable: "ProductModules",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "TenantModules",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<long>(type: "bigint", nullable: false),
                ProductModuleId = table.Column<long>(type: "bigint", nullable: false),
                IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                ActivationSource = table.Column<int>(type: "int", nullable: false),
                EnabledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DisabledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                EffectiveFromUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                EffectiveUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                DisabledReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                ConfigurationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ConfigurationVersion = table.Column<int>(type: "int", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TenantModules", x => x.Id);
                table.ForeignKey(
                    name: "FK_TenantModules_ProductModules_ProductModuleId",
                    column: x => x.ProductModuleId,
                    principalTable: "ProductModules",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_TenantModules_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProductModuleFeatures_FeatureId",
            table: "ProductModuleFeatures",
            column: "FeatureId");

        migrationBuilder.CreateIndex(
            name: "IX_ProductModuleFeatures_ProductModuleId_FeatureId",
            table: "ProductModuleFeatures",
            columns: new[] { "ProductModuleId", "FeatureId" },
            unique: true,
            filter: "[IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "IX_TenantModules_ProductModuleId",
            table: "TenantModules",
            column: "ProductModuleId");

        migrationBuilder.CreateIndex(
            name: "IX_TenantModules_TenantId_IsEnabled_EffectiveFromUtc_EffectiveUntilUtc",
            table: "TenantModules",
            columns: new[] { "TenantId", "IsEnabled", "EffectiveFromUtc", "EffectiveUntilUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_TenantModules_TenantId_ProductModuleId",
            table: "TenantModules",
            columns: new[] { "TenantId", "ProductModuleId" },
            unique: true,
            filter: "[IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ProductModuleFeatures");
        migrationBuilder.DropTable(name: "TenantModules");
    }
}
