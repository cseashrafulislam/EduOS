using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>Tenant-scoped active custom-field values must be unique per definition and entity.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261014024000_TenantCustomFieldValueUniqueness")]
public sealed class TenantCustomFieldValueUniqueness : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Do not silently discard legacy data. An operator must reconcile duplicates before deployment.
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM dbo.CustomFieldValues WITH (UPDLOCK, HOLDLOCK)
                WHERE IsDeleted = 0
                GROUP BY TenantId, CustomFieldDefinitionId, EntityId
                HAVING COUNT_BIG(*) > 1
            ) THROW 51132, 'Duplicate active custom-field values exist; reconcile them before migration.', 1;
            """);
        migrationBuilder.CreateIndex(
            name: "IX_CustomFieldValues_TenantId_CustomFieldDefinitionId_EntityId",
            table: "CustomFieldValues",
            columns: new[] { "TenantId", "CustomFieldDefinitionId", "EntityId" },
            unique: true,
            filter: "[IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_CustomFieldValues_TenantId_CustomFieldDefinitionId_EntityId",
            table: "CustomFieldValues");
    }
}
