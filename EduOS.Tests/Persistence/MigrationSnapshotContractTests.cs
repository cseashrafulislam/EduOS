using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Reflection;
using Xunit;

namespace EduOS.Tests.Persistence;

public class MigrationSnapshotContractTests
{
    [Fact]
    public void Every_committed_migration_is_discoverable_by_ef_core()
    {
        var contextType = typeof(EduOSDbContext);
        var migrationTypes = contextType.Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(Migration).IsAssignableFrom(type))
            .OrderBy(type => type.Name)
            .ToList();

        Assert.NotEmpty(migrationTypes);

        var missingMetadata = migrationTypes
            .Where(type => type.GetCustomAttribute<MigrationAttribute>() == null ||
                           type.GetCustomAttribute<DbContextAttribute>()?.ContextType != contextType)
            .Select(type => type.FullName)
            .ToList();

        Assert.True(missingMetadata.Count == 0,
            "EF migrations missing Migration/DbContext discovery metadata:\n" +
            string.Join("\n", missingMetadata));

        var duplicateIds = migrationTypes
            .Select(type => type.GetCustomAttribute<MigrationAttribute>()!.Id)
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(duplicateIds.Count == 0,
            "Duplicate EF migration IDs detected:\n" + string.Join("\n", duplicateIds));
    }
}
