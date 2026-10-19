using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Communication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class CommunicationDeliveryApiContractTests
{
    [Fact]
    public void Existing_delivery_history_controller_is_the_only_owner_of_the_route()
    {
        var controller = typeof(CommunicationDeliveryHistoryApiController);
        var authorization = Assert.Single(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal("TenantAdmin", authorization.Roles);
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
        var route = Assert.Single(controller.GetCustomAttributes(typeof(RouteAttribute), true)
            .Cast<RouteAttribute>());
        Assert.Equal("api/communication-admin/deliveries", route.Template);
        var action = controller.GetMethod(nameof(CommunicationDeliveryHistoryApiController.Index));
        Assert.NotNull(action);
        var get = Assert.Single(action!.GetCustomAttributes(typeof(HttpGetAttribute), true)
            .Cast<HttpGetAttribute>());
        Assert.Null(get.Template);
        Assert.Null(typeof(CommunicationAdministrationController).GetMethod("Deliveries"));
        Assert.Null(typeof(CommunicationDeliveryDto).GetProperty("Body"));
        Assert.Null(typeof(CommunicationDeliveryDto).GetProperty("ProtectedCredential"));
    }
}
