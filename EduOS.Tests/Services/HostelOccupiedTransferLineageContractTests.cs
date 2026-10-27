using EduOS.Core.Entities.Hostel;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelOccupiedTransferLineageContractTests
{
    [Fact]
    public void Canonical_allocation_has_tenant_bound_single_successor_and_rowversion()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer("Server=localhost;Database=EduOS_Model_Inspection;TrustServerCertificate=True;Integrated Security=True")
            .Options;
        using var db = new EduOSDbContext(options);
        var entity = Assert.IsAssignableFrom<Microsoft.EntityFrameworkCore.Metadata.IEntityType>(
            db.Model.FindEntityType(typeof(StudentHostelAllocation)));
        var property = entity.FindProperty(nameof(StudentHostelAllocation.PreviousAllocationId));
        Assert.NotNull(property);
        Assert.True(property.IsNullable);

        var fk = Assert.Single(entity.GetForeignKeys(), f =>
            f.PrincipalEntityType.ClrType == typeof(StudentHostelAllocation));
        Assert.Equal(new[] { "TenantId", "PreviousAllocationId" },
            fk.Properties.Select(p => p.Name).ToArray());
        Assert.Equal(new[] { "TenantId", "Id" },
            fk.PrincipalKey.Properties.Select(p => p.Name).ToArray());
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);

        var index = Assert.Single(entity.GetIndexes(), x =>
            x.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { "TenantId", "PreviousAllocationId" }));
        Assert.True(index.IsUnique);
        Assert.Equal("[PreviousAllocationId] IS NOT NULL", index.GetFilter());

        var version = entity.FindProperty(nameof(StudentHostelAllocation.RowVersion));
        Assert.NotNull(version);
        Assert.True(version.IsConcurrencyToken);
        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate,
            version.ValueGenerated);
        // EF Core 10 omits relational check-constraint metadata from the read-optimized
        // runtime model; use the design-time model for schema contract assertions.
        var designEntity = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>()
            .Model.FindEntityType(typeof(StudentHostelAllocation));
        Assert.NotNull(designEntity);
        Assert.Contains(designEntity!.GetCheckConstraints(), x =>
            x.Name == "CK_StudentHostelAllocation_Predecessor" &&
            x.Sql!.Contains("[PreviousAllocationId] <> [Id]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sql_server_migration_creates_tenant_fk_and_filtered_unique_predecessor_index()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer(connection).Options;
        await using var db = new EduOSDbContext(options);
        var fkCount = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE name = 'FK_StudentHostelAllocations_StudentHostelAllocations_TenantId_PreviousAllocationId'")
            .SingleAsync();
        var indexCount = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.StudentHostelAllocations') AND name = 'IX_StudentHostelAllocations_TenantId_PreviousAllocationId' AND is_unique = 1 AND has_filter = 1")
            .SingleAsync();
        Assert.Equal(1, fkCount);
        Assert.Equal(1, indexCount);

        // A matching FK name alone is insufficient: verify both tenant-bound columns,
        // trusted enforcement, and the noncomputed filter on the deployed database.
        var tenantBoundColumns = await db.Database.SqlQueryRaw<int>(@"
SELECT COUNT(*) AS [Value]
FROM sys.foreign_keys AS fk
JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.columns AS dependent
  ON dependent.object_id = fkc.parent_object_id AND dependent.column_id = fkc.parent_column_id
JOIN sys.columns AS principal
  ON principal.object_id = fkc.referenced_object_id AND principal.column_id = fkc.referenced_column_id
WHERE fk.name = N'FK_StudentHostelAllocations_StudentHostelAllocations_TenantId_PreviousAllocationId'
  AND fk.parent_object_id = OBJECT_ID(N'dbo.StudentHostelAllocations')
  AND fk.referenced_object_id = OBJECT_ID(N'dbo.StudentHostelAllocations')
  AND fk.is_disabled = 0 AND fk.is_not_trusted = 0
  AND ((fkc.constraint_column_id = 1 AND dependent.name = N'TenantId' AND principal.name = N'TenantId')
    OR (fkc.constraint_column_id = 2 AND dependent.name = N'PreviousAllocationId' AND principal.name = N'Id'))")
            .SingleAsync();
        Assert.Equal(2, tenantBoundColumns);

        var legalFilterCount = await db.Database.SqlQueryRaw<int>(@"
SELECT COUNT(*) AS [Value]
FROM sys.indexes AS i
WHERE i.object_id = OBJECT_ID(N'dbo.StudentHostelAllocations')
  AND i.name = N'IX_StudentHostelAllocations_TenantId_PreviousAllocationId'
  AND i.is_unique = 1 AND i.has_filter = 1
  AND CHARINDEX(N'[PreviousAllocationId]', i.filter_definition) > 0
  AND CHARINDEX(N'IS NOT NULL', i.filter_definition) > 0
  AND NOT EXISTS (
    SELECT 1 FROM sys.computed_columns AS c
    WHERE c.object_id = i.object_id
      AND CHARINDEX(N'[' + c.name + N']', i.filter_definition) > 0
  )").SingleAsync();
        Assert.Equal(1, legalFilterCount);
    }
}
