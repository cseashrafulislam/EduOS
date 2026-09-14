using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace EduOS.Tests.Persistence;

public class MigrationSnapshotContractTests
{
    [Fact]
    public void Committed_snapshot_matches_runtime_property_and_relationship_contracts()
    {
        using var context = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=EduOSSnapshotContract;Trusted_Connection=True;")
            .Options);

        var snapshotType = typeof(EduOSDbContext).Assembly.GetType(
            "EduOS.Persistence.Migrations.EduOSDbContextModelSnapshot",
            throwOnError: true)!;
        var snapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!;
        var runtimeModel = context.Model;
        var snapshotModel = snapshot.Model;
        var differences = new List<string>();

        foreach (var runtimeEntity in runtimeModel.GetEntityTypes().OrderBy(x => x.Name))
        {
            var snapshotEntity = snapshotModel.FindEntityType(runtimeEntity.Name);
            if (snapshotEntity == null)
            {
                differences.Add($"Missing entity in snapshot: {runtimeEntity.Name}");
                continue;
            }

            CompareProperties(runtimeEntity, snapshotEntity, differences);
            CompareForeignKeys(runtimeEntity, snapshotEntity, differences);
        }

        foreach (var snapshotEntity in snapshotModel.GetEntityTypes().OrderBy(x => x.Name))
        {
            if (runtimeModel.FindEntityType(snapshotEntity.Name) == null)
                differences.Add($"Snapshot-only entity: {snapshotEntity.Name}");
        }

        Assert.True(differences.Count == 0,
            "EF model snapshot drift detected:\n" + string.Join("\n", differences));
    }

    private static void CompareProperties(
        IEntityType runtimeEntity,
        IEntityType snapshotEntity,
        ICollection<string> differences)
    {
        var runtimeProperties = runtimeEntity.GetProperties().ToDictionary(x => x.Name);
        var snapshotProperties = snapshotEntity.GetProperties().ToDictionary(x => x.Name);

        foreach (var (name, runtimeProperty) in runtimeProperties)
        {
            if (!snapshotProperties.TryGetValue(name, out var snapshotProperty))
            {
                differences.Add($"{runtimeEntity.Name}: snapshot missing property {name}");
                continue;
            }

            if (runtimeProperty.ClrType != snapshotProperty.ClrType ||
                runtimeProperty.IsNullable != snapshotProperty.IsNullable ||
                runtimeProperty.IsConcurrencyToken != snapshotProperty.IsConcurrencyToken)
            {
                differences.Add(
                    $"{runtimeEntity.Name}.{name}: runtime={Describe(runtimeProperty)}, snapshot={Describe(snapshotProperty)}");
            }
        }

        foreach (var name in snapshotProperties.Keys.Except(runtimeProperties.Keys).OrderBy(x => x))
            differences.Add($"{runtimeEntity.Name}: snapshot-only property {name}");
    }

    private static void CompareForeignKeys(
        IEntityType runtimeEntity,
        IEntityType snapshotEntity,
        ICollection<string> differences)
    {
        var runtime = runtimeEntity.GetForeignKeys()
            .Select(ForeignKeySignature)
            .OrderBy(x => x)
            .ToList();
        var snapshot = snapshotEntity.GetForeignKeys()
            .Select(ForeignKeySignature)
            .OrderBy(x => x)
            .ToList();

        foreach (var signature in runtime.Except(snapshot))
            differences.Add($"{runtimeEntity.Name}: snapshot missing FK {signature}");
        foreach (var signature in snapshot.Except(runtime))
            differences.Add($"{runtimeEntity.Name}: snapshot-only FK {signature}");
    }

    private static string Describe(IProperty property) =>
        $"{property.ClrType.Name}, nullable={property.IsNullable}, concurrency={property.IsConcurrencyToken}";

    private static string ForeignKeySignature(IForeignKey foreignKey) =>
        $"[{string.Join(',', foreignKey.Properties.Select(x => x.Name))}]=>{foreignKey.PrincipalEntityType.Name}";
}
