using EduOS.Core.Entities.Transport;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

/// <summary>Platform-owned index contract for Operations' tenant-scoped range scans.</summary>
public sealed class TransportReservationWindowIndexTests
{
    [Fact]
    public void Date_window_indexes_cover_canonical_transport_entities_without_replacing_current_uniqueness()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer("Server=localhost;Database=EduOS_Model_Inspection;TrustServerCertificate=True;Integrated Security=True")
            .Options;
        using var db = new EduOSDbContext(options);
        // The legacy tenant/vehicle lookup index is deployed and must remain in the
        // canonical model alongside the new date-window index (no EF snapshot drift).
        Assert.Contains(db.Model.FindEntityType(typeof(StudentTransport))!.GetIndexes(), i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "VehicleId" }) &&
            !i.IsUnique);
        AssertWindow(db, typeof(StudentTransport), "StudentId", "StartDate", "EndDate");
        AssertWindow(db, typeof(StudentTransport), "VehicleId", "StartDate", "EndDate");
        AssertWindow(db, typeof(RouteVehicleAssignment), "VehicleId", "EffectiveFrom", "EffectiveTo");
        AssertWindow(db, typeof(VehicleDriverAssignment), "VehicleId", "EffectiveFrom", "EffectiveTo");
        AssertWindow(db, typeof(VehicleDriverAssignment), "TransportDriverId", "EffectiveFrom", "EffectiveTo");

        foreach (var entity in new[] { typeof(StudentTransport), typeof(RouteVehicleAssignment),
            typeof(VehicleDriverAssignment), typeof(Vehicle), typeof(TransportDriver) })
        {
            var version = db.Model.FindEntityType(entity)!.FindProperty("RowVersion")!;
            Assert.True(version.IsConcurrencyToken);
            Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
        }

        foreach (var entity in new[] { typeof(RouteVehicleAssignment), typeof(VehicleDriverAssignment) })
        {
            Assert.Contains(db.Model.FindEntityType(entity)!.GetIndexes(), i =>
                i.IsUnique && i.Properties.Select(p => p.Name)
                    .SequenceEqual(new[] { "TenantId", "VehicleId" }) &&
                i.GetFilter() == "[IsCurrent] = 1 AND [IsDeleted] = 0");
        }
    }

    private static void AssertWindow(EduOSDbContext db, Type type, string resource,
        string start, string end)
    {
        var entity = db.Model.FindEntityType(type)!;
        var index = Assert.Single(entity.GetIndexes(), i =>
            i.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { "TenantId", resource, start, end }));
        Assert.False(index.IsUnique);
        Assert.Equal("[IsDeleted] = 0", index.GetFilter());
    }

    [Fact]
    public async Task Sql_server_migration_installs_all_five_filtered_window_indexes()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        await using var db = new EduOSDbContext(options);
        var count = await db.Database.SqlQueryRaw<int>(@"
SELECT COUNT(*) AS [Value]
FROM sys.indexes i
WHERE i.has_filter = 1 AND i.filter_definition LIKE N'%IsDeleted%'
AND (
    (i.object_id = OBJECT_ID(N'dbo.StudentTransports') AND i.name IN (
        N'IX_StudentTransports_TenantId_StudentId_StartDate_EndDate',
        N'IX_StudentTransports_TenantId_VehicleId_StartDate_EndDate'))
 OR (i.object_id = OBJECT_ID(N'dbo.RouteVehicleAssignments') AND i.name =
        N'IX_RouteVehicleAssignments_TenantId_VehicleId_EffectiveFrom_EffectiveTo')
 OR (i.object_id = OBJECT_ID(N'dbo.VehicleDriverAssignments') AND i.name IN (
        N'IX_VehicleDriverAssignments_TenantId_TransportDriverId_EffectiveFrom_EffectiveTo',
        N'IX_VehicleDriverAssignments_TenantId_VehicleId_EffectiveFrom_EffectiveTo'))
)").SingleAsync();
        Assert.Equal(5, count);
    }
}
