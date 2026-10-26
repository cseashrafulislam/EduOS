using EduOS.Core.Entities.Transport;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportIdentityIndexTests
{
    [Fact]
    public void Vehicle_and_driver_identity_indexes_are_tenant_scoped_unique_and_soft_delete_aware()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("transport-index-model-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 100L;
        using var db = new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });

        AssertNormalizedIndex(db, typeof(Vehicle), "NormalizedVehicleNumber", "VehicleNumber", 50);
        AssertNormalizedIndex(db, typeof(TransportDriver), "NormalizedLicenseNumber", "LicenseNumber", 100);
        var vehicle = db.Model.FindEntityType(typeof(Vehicle))!;
        var legacyIndex = Assert.Single(vehicle.GetIndexes(), i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "VehicleNumber" }));
        Assert.False(legacyIndex.IsUnique);
    }

    private static void AssertNormalizedIndex(EduOSDbContext db, Type entityType,
        string normalized, string original, int length)
    {
        var entity = db.Model.FindEntityType(entityType);
        Assert.NotNull(entity);
        var property = entity!.FindProperty(normalized);
        Assert.NotNull(property);
        Assert.Equal(length, property!.GetMaxLength());
        Assert.Equal("UPPER(LTRIM(RTRIM([" + original + "])))", property.GetComputedColumnSql());
        var index = Assert.Single(entity.GetIndexes(), i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", normalized }));
        Assert.True(index.IsUnique);
        var expectedFilter = original == "LicenseNumber"
            ? "[LicenseNumber] IS NOT NULL AND [IsDeleted] = 0"
            : "[IsDeleted] = 0";
        Assert.Equal(expectedFilter, index.GetFilter());
        Assert.DoesNotContain("[" + normalized + "]", index.GetFilter()!);
    }
}
