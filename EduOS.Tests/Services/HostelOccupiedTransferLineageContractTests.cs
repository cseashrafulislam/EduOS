using EduOS.Core.Entities.Hostel;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
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
        Assert.Contains(entity.GetCheckConstraints(), x =>
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
    }
}
