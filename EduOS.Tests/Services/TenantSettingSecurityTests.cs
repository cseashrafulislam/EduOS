using EduOS.Core.DTOs.Tenants;
using EduOS.Core.Entities.Tenants;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Helpers;
using EduOS.Service.Services.Tenants;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public class TenantSettingSecurityTests
{
    [Fact]
    public async Task Sensitive_setting_is_encrypted_masked_and_preserved_when_mask_is_submitted()
    {
        await using var setup = CreateSetup();

        var saved = await setup.Service.SaveSmsGatewayAsync(new SmsGatewaySettingsDto
        {
            Provider = "Custom",
            ApiUrl = "https://sms.example.com/send",
            ApiKey = "a-real-secret-value",
            SenderId = "EduOS",
            IsEnabled = true
        });

        saved.Success.Should().BeTrue(saved.Message);
        var stored = await setup.Context.TenantSettings
            .SingleAsync(x => x.SettingKey == "ApiKey");
        stored.SettingValue.Should().NotBe("a-real-secret-value");
        stored.SettingValue.Should().StartWith("dp:v1:");

        var response = await setup.Service.GetSmsGatewayAsync();
        response.Success.Should().BeTrue();
        response.Data!.ApiKey.Should().Be("********");

        var protectedValue = stored.SettingValue;
        var updated = await setup.Service.SaveSmsGatewayAsync(new SmsGatewaySettingsDto
        {
            Provider = "Custom",
            ApiUrl = "https://sms.example.com/send",
            ApiKey = "********",
            SenderId = "EduOS",
            IsEnabled = true
        });

        updated.Success.Should().BeTrue(updated.Message);
        stored.SettingValue.Should().Be(protectedValue);
    }

    [Theory]
    [InlineData("http://sms.example.com/send")]
    [InlineData("https://localhost/send")]
    [InlineData("https://127.0.0.1/send")]
    [InlineData("https://192.168.0.10/send")]
    [InlineData("https://metadata.internal/send")]
    public async Task Sms_gateway_rejects_insecure_or_private_endpoints(string apiUrl)
    {
        await using var setup = CreateSetup();

        var result = await setup.Service.SaveSmsGatewayAsync(new SmsGatewaySettingsDto
        {
            Provider = "Custom",
            ApiUrl = apiUrl,
            ApiKey = "secret",
            SenderId = "EduOS",
            IsEnabled = true
        });

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        (await setup.Context.TenantSettings.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Sms_gateway_cannot_be_enabled_without_a_new_or_stored_secret()
    {
        await using var setup = CreateSetup();

        var result = await setup.Service.SaveSmsGatewayAsync(new SmsGatewaySettingsDto
        {
            Provider = "BulkSMSBD",
            ApiUrl = "https://api.bulksmsbd.com/send",
            SenderId = "EduOS",
            IsEnabled = true
        });

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Theory]
    [InlineData("localhost", 587)]
    [InlineData("smtp.example.com", 8080)]
    public async Task Email_gateway_rejects_private_hosts_and_unapproved_ports(
        string host,
        int port)
    {
        await using var setup = CreateSetup();

        var result = await setup.Service.SaveEmailGatewayAsync(new EmailGatewaySettingsDto
        {
            SmtpHost = host,
            SmtpPort = port,
            FromEmail = "notices@example.com",
            IsEnabled = true
        });

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    private static TestSetup CreateSetup()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"tenant-settings-{Guid.NewGuid():N}")
            .Options;
        var accessor = new TestHttpContextAccessor
        {
            HttpContext = CreateHttpContext(101)
        };
        var context = new EduOSDbContext(options, accessor);
        var service = new TenantSettingService(
            new GenericRepository<TenantSetting>(context),
            context,
            new CurrentUserService(accessor),
            new EphemeralDataProtectionProvider(),
            NullLogger<TenantSettingService>.Instance);
        return new TestSetup(context, service);
    }

    private static DefaultHttpContext CreateHttpContext(long tenantId)
    {
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "9001"),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", tenantId.ToString())
            ], "TestAuthentication"))
        };
    }

    private sealed record TestSetup(
        EduOSDbContext Context,
        TenantSettingService Service) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
