using System.Security.Claims;
using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
namespace EduOS.Tests.Services;
public sealed class ApiCredentialUsageEndpointSecurityTests
{
    [Fact]
    public async Task Authenticated_cookie_session_cannot_use_credential_endpoint()
    {
        var service = new Mock<IApiCredentialUsageService>(MockBehavior.Strict);
        var controller = new ApiCredentialUsageApiController(service.Object);
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "123") }, "Cookies"));
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        Assert.IsType<UnauthorizedObjectResult>(await controller.Get());
        service.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task Missing_authorization_header_is_rejected_before_service()
    {
        var service = new Mock<IApiCredentialUsageService>(MockBehavior.Strict);
        var controller = new ApiCredentialUsageApiController(service.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        Assert.IsType<UnauthorizedObjectResult>(await controller.Get());
        service.VerifyNoOtherCalls();
    }
}
