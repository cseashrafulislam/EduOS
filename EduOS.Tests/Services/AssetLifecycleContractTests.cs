using EduOS.App.Authorization;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;
namespace EduOS.Tests.Services;
public sealed class AssetLifecycleContractTests
{
    [Fact]
    public void Canonical_asset_interface_is_fully_implemented()
    {
        Assert.True(typeof(IAssetLifecycleService).IsAssignableFrom(typeof(AssetLifecycleService)));
        foreach (var method in typeof(IAssetLifecycleService).GetMethods())
            Assert.NotNull(typeof(AssetLifecycleService).GetMethod(method.Name));
    }
    [Fact]
    public void Asset_api_and_mvc_require_inventory_module_and_roles()
    {
        var api = typeof(EduOS.App.Controllers.Api.AssetLifecycleController);
        var mvc = typeof(EduOS.App.Controllers.AssetLifecycleController);
        Assert.Equal("api/inventory/assets", api.GetCustomAttributes(typeof(RouteAttribute), true)
            .Cast<RouteAttribute>().Single().Template);
        Assert.Contains(api.GetCustomAttributes(typeof(RequireModuleAttribute), true)
            .Cast<RequireModuleAttribute>(), x => x.Policy == "EduOSModule:INVENTORY");
        Assert.Contains(mvc.GetCustomAttributes(typeof(RequireModuleAttribute), true)
            .Cast<RequireModuleAttribute>(), x => x.Policy == "EduOSModule:INVENTORY");
        Assert.Contains(mvc.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), x => x.Roles?.Contains("InventoryManager") == true);
        Assert.Contains(api.GetCustomAttributes(typeof(ResponseCacheAttribute), true)
            .Cast<ResponseCacheAttribute>(), x => x.NoStore);
    }
}
