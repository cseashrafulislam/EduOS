using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Communication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class CommunicationDeliveryApiContractTests
{
    [Fact]
    public void History_route_is_authorized_and_exposes_no_message_body()
    {
        var controller = typeof(CommunicationAdministrationController);
        var authorization = Assert.Single(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal("TenantAdmin", authorization.Roles);
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
        var action = controller.GetMethod(nameof(CommunicationAdministrationController.Deliveries));
        Assert.NotNull(action);
        var get = Assert.Single(action!.GetCustomAttributes(typeof(HttpGetAttribute), true)
            .Cast<HttpGetAttribute>());
        Assert.Equal("deliveries", get.Template);
        Assert.Null(typeof(CommunicationDeliveryDto).GetProperty("Body"));
    }
}
