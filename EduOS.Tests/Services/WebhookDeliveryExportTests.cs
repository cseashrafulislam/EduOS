using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces;
using EduOS.Service.Services.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class WebhookDeliveryExportTests
{
    [Fact]
    public async Task Anonymous_user_cannot_export()
    {
        var service = new IntegrationAdministrationService(null!, new Mock<ICurrentUserService>().Object,
            null!, null!, NullLogger<IntegrationAdministrationService>.Instance);
        Assert.Equal(403, (await service.ExportWebhookDeliveriesCsvAsync(1)).StatusCode);
    }

    [Fact]
    public void Export_api_requires_admin_get_and_no_store()
    {
        var type = typeof(IntegrationAdministrationApiController);
        Assert.Equal("TenantAdmin", Assert.Single(type.GetCustomAttributes(
            typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        Assert.NotEmpty(type.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
        Assert.NotEmpty(type.GetCustomAttributes(typeof(ResponseCacheAttribute), true));
        var action = type.GetMethod("ExportDeliveries");
        Assert.NotNull(action);
        Assert.Equal("webhooks/{id:long}/deliveries/export",
            Assert.Single(action!.GetCustomAttributes(typeof(HttpGetAttribute), true)
                .Cast<HttpGetAttribute>()).Template);
    }
}
