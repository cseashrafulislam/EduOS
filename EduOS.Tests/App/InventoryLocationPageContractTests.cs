using EduOS.Core.DTOs.Inventory;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Interfaces.IServices;
using Xunit;

namespace EduOS.Tests.App;

public sealed class InventoryLocationPageContractTests
{
    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "TestAssets", name);

    [Fact]
    public void Location_reads_expose_one_paged_contract_and_entity_address()
    {
        Assert.Null(typeof(IInventoryCatalogService).GetMethod("GetLocationsAsync"));
        Assert.NotNull(typeof(IInventoryCatalogService).GetMethod("GetLocationsPageAsync"));
        Assert.NotNull(typeof(InventoryLocation).GetProperty(nameof(InventoryLocation.Address)));
        Assert.NotNull(typeof(InventoryLocationDto).GetProperty(nameof(InventoryLocationDto.Address)));
        var controller = File.ReadAllText(Asset("InventoryCatalogController.cs"));
        Assert.Contains("int page=1,int pageSize=25", controller);
        Assert.DoesNotContain("service.GetLocationsAsync", controller);
    }

    [Fact]
    public void All_location_dropdowns_use_the_paged_loader_without_array_fallback()
    {
        var loader = File.ReadAllText(Asset("movements-api.js"));
        Assert.Contains("catalog/locations?' + query", loader);
        Assert.Contains("pageSize: String(pageSize)", loader);
        Assert.Contains("rows.length !== data.totalCount", loader);
        Assert.Contains("data.hasNextPage", loader);
        Assert.DoesNotContain("Array.isArray(locations)", loader);
        Assert.Contains("knownLocations ?? activeLocations()", loader);
        foreach (var name in new[] { "movements-reorder.js", "movements-stock-position.js" })
        {
            var source = File.ReadAllText(Asset(name));
            Assert.Contains("await activeLocations()", source);
            Assert.DoesNotContain("get('catalog/locations')", source);
        }
        Assert.Contains("data.locations", File.ReadAllText(Asset("movements-form.js")));
    }

    [Fact]
    public void Location_address_is_visible_and_restored_during_edit()
    {
        var view = File.ReadAllText(Asset("InventoryCatalogIndex.cshtml"));
        var script = File.ReadAllText(Asset("inventory-catalog.js"));
        Assert.Contains("<th>Address</th>", view);
        Assert.Contains("id=\"locationAddress\"", view);
        Assert.Contains("row.campusId, row.address, row.isActive", script);
        Assert.Contains("el('locationAddress').value = row.address ?? ''", script);
        Assert.Contains("address: address || null", script);
    }
}
