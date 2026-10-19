using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class CommunicationDeliveryCsvExportContractTests
{
    [Fact]
    public void Export_is_owned_by_existing_tenant_admin_delivery_controller()
    {
        var controller = typeof(CommunicationDeliveryHistoryApiController);
        var route = Assert.Single(controller.GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>());
        Assert.Equal("api/communication-admin/deliveries", route.Template);
        var role = Assert.Single(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("TenantAdmin", role.Roles);
        var method = controller.GetMethod(nameof(CommunicationDeliveryHistoryApiController.Export));
        Assert.NotNull(method);
        var http = Assert.Single(method!.GetCustomAttributes(typeof(HttpGetAttribute), true).Cast<HttpGetAttribute>());
        Assert.Equal("export", http.Template);
        Assert.NotNull(typeof(ICommunicationAdministrationService).GetMethod("ExportDeliveriesCsvAsync"));
    }
}
