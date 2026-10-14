using EduOS.App.Controllers;
using EduOS.App.Controllers.Api;
using EduOS.Core.Interfaces;
using EduOS.Service.Services.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class IntegrationAdministrationSecurityTests
{
    [Fact]
    public void Integration_api_requires_tenant_admin_and_antiforgery()
    {
        var type = typeof(IntegrationAdministrationApiController);
        Assert.Equal("TenantAdmin", Assert.Single(type.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()).Roles);
        Assert.NotEmpty(type.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
        Assert.Equal("TenantAdmin", Assert.Single(typeof(IntegrationAdministrationController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
    }

    [Theory]
    [InlineData("http://example.com/hook")]
    [InlineData("https://localhost/hook")]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://10.0.0.1/hook")]
    [InlineData("https://service.internal/hook")]
    [InlineData("https://example.com:8443/hook")]
    [InlineData("https://user:password@example.com/hook")]
    [InlineData("https://example.com/hook?token=secret")]
    [InlineData("https://example.com/hook#fragment")]
    public void Unsafe_webhook_url_is_rejected(string url) =>
        Assert.False(IntegrationAdministrationService.IsPublicWebhookUrl(url));

    [Fact]
    public void Public_https_dns_url_is_accepted_for_configuration() =>
        Assert.True(IntegrationAdministrationService.IsPublicWebhookUrl("https://hooks.example.org/eduos"));

    [Fact]
    public async Task Unauthenticated_user_cannot_read_or_modify_integration_configuration()
    {
        var user = new Mock<ICurrentUserService>();
        var service = new IntegrationAdministrationService(null!, user.Object, null!, null!,
            NullLogger<IntegrationAdministrationService>.Instance);
        Assert.Equal(403, (await service.GetWebhookEndpointsAsync(1, 20)).StatusCode);
        Assert.Equal(403, (await service.GetWebhookDeliveriesAsync(1, 1, 20)).StatusCode);
        Assert.Equal(403, (await service.GetApiCredentialsAsync(1, 20)).StatusCode);
        Assert.Equal(403, (await service.SaveWebhookEndpointAsync(null, new())).StatusCode);
        Assert.Equal(403, (await service.CreateApiCredentialAsync(new())).StatusCode);
        Assert.Equal(403, (await service.RevokeApiCredentialAsync(1, new())).StatusCode);
    }
}
