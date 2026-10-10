using EduOS.App.Authorization;
using EduOS.App.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class InventoryCatalogMvcAuthorizationTests
{
    [Fact]
    public void Catalog_page_matches_inventory_api_read_authorization()
    {
        var mvc = typeof(InventoryCatalogController);
        var api = typeof(EduOS.App.Controllers.Api.InventoryCatalogController);
        var pageRoles = mvc.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single(x => x.GetType() == typeof(AuthorizeAttribute)).Roles;
        var apiRoles = api.GetMethod(nameof(EduOS.App.Controllers.Api.InventoryCatalogController.Items))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single(x => x.GetType() == typeof(AuthorizeAttribute)).Roles;
        pageRoles.Should().Be("TenantAdmin,Principal,InventoryManager,StoreKeeper,Accountant");
        pageRoles.Should().Be(apiRoles);
        api.GetCustomAttributes(typeof(ResponseCacheAttribute), true)
            .Cast<ResponseCacheAttribute>()
            .Should().ContainSingle(x => x.NoStore && x.Location == ResponseCacheLocation.None);
        mvc.GetCustomAttributes(typeof(RequireModuleAttribute), true)
            .Cast<RequireModuleAttribute>().Should().ContainSingle(x => x.Policy == "EduOSModule:INVENTORY");
        mvc.GetMethod(nameof(InventoryCatalogController.Index))!
            .GetCustomAttributes(typeof(ResponseCacheAttribute), true)
            .Cast<ResponseCacheAttribute>().Should().ContainSingle(x => x.NoStore);
    }
}
