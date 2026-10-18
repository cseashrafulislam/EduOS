using System.Security.Claims;
using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ApiCredentialUsageCsvExportTests
{
    [Fact]
    public async Task Browser_session_cannot_download_csv()
    {
        var service = new Mock<IApiCredentialUsageService>(MockBehavior.Strict);
        var controller = new ApiCredentialUsageApiController(service.Object);
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "123") }, "Cookies"));
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        Assert.IsType<UnauthorizedObjectResult>(await controller.Export());
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Missing_authorization_is_rejected()
    {
        var service = new Mock<IApiCredentialUsageService>(MockBehavior.Strict);
        var controller = new ApiCredentialUsageApiController(service.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        Assert.IsType<UnauthorizedObjectResult>(await controller.Export());
        service.VerifyNoOtherCalls();
    }
}
