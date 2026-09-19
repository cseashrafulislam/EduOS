using EduOS.Core.Entities.Inventory;
using Xunit;

namespace EduOS.Tests;

public sealed class InventoryIdContractTests
{
    [Theory]
    [InlineData(typeof(Asset), nameof(Asset.Id), typeof(long))]
    [InlineData(typeof(Asset), nameof(Asset.TenantId), typeof(long))]
    [InlineData(typeof(AssetMaintenance), nameof(AssetMaintenance.Id), typeof(long))]
    [InlineData(typeof(AssetMaintenance), nameof(AssetMaintenance.TenantId), typeof(long))]
    [InlineData(typeof(AssetMaintenance), nameof(AssetMaintenance.AssetId), typeof(long))]
    public void Mapped_asset_identifiers_remain_long(Type type, string propertyName, Type expectedType)
    {
        var property = type.GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.Equal(expectedType, property!.PropertyType);
    }
}
