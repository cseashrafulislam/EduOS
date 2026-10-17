using System.Net;
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

public sealed class WebhookTestDeliverySecurityTests
{
    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.10.0.1", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("192.0.2.1", false)]
    [InlineData("198.51.100.1", false)]
    [InlineData("203.0.113.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("1.1.1.1", true)]
    [InlineData("8.8.8.8", true)]
    public void Only_public_ipv4_destinations_are_accepted(string ip, bool expected) =>
        Assert.Equal(expected, SecureWebhookTransport.IsPublicAddress(IPAddress.Parse(ip)));

    [Fact]
    public void Signature_uses_timestamp_and_exact_body()
    {
        Assert.Equal("sha256=6a506f9155f1c2a58bb2969a676eacdae239dfe2801e1518bf41a3de0e4d7732",
            SecureWebhookTransport.Signature("0123456789abcdef", "1700000000", "{\"test\":true}"));
    }

    [Fact]
    public void Test_ui_requires_tenant_admin_and_antiforgery()
    {
        var type = typeof(WebhookTestsController);
        Assert.Equal("TenantAdmin", Assert.Single(type.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()).Roles);
        Assert.NotEmpty(type.GetMethod(nameof(WebhookTestsController.Test))!
            .GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true));
        Assert.NotEmpty(type.GetMethod(nameof(WebhookTestsController.Retry))!
            .GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true));
        Assert.NotEmpty(typeof(IntegrationAdministrationApiController)
            .GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
    }

    [Fact]
    public async Task Unauthenticated_user_cannot_test_or_retry()
    {
        var user = new Mock<ICurrentUserService>();
        var service = new IntegrationAdministrationService(null!, user.Object, null!, null!,
            NullLogger<IntegrationAdministrationService>.Instance);
        Assert.Equal(403, (await service.TestWebhookAsync(1, new())).StatusCode);
        Assert.Equal(403, (await service.RetryWebhookAsync(1, new())).StatusCode);
    }
}
