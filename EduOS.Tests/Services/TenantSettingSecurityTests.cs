using EduOS.Core.DTOs.Tenants;
using EduOS.Core.Entities.SaaS;
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
            .SingleAsync(x => x.Key == "Sms:ApiKey");
        stored.Value.Should().NotBe("a-real-secret-value");
        stored.Value.Should().StartWith("dp:v1:");

        var response = await setup.Service.GetSmsGatewayAsync();
        response.Success.Should().BeTrue();
        response.Data!.ApiKey.Should().Be("********");

        var protectedValue = stored.Value;
        var updated = await setup.Service.SaveSmsGatewayAsync(new SmsGatewaySettingsDto
        {
            Provider = "Custom",
            ApiUrl = "https://sms.example.com/send",
            ApiKey = "********",
            SenderId = "EduOS",
            IsEnabled = true
        });

        updated.Success.Should().BeTrue(updated.Message);
        stored.Value.Should().Be(protectedValue);
    }

    [Fact]
    public async Task Gateway_categories_have_distinct_keys_and_readable_local_names()
    {
        await using var setup = CreateSetup();
        var sms = await setup.Service.SaveSmsGatewayAsync(new SmsGatewaySettingsDto
        {
            Provider = "Custom",
            ApiUrl = "https://sms.example.com/send",
            SenderId = "EduOS",
            IsEnabled = false
        });
        var email = await setup.Service.SaveEmailGatewayAsync(new EmailGatewaySettingsDto
        {
            FromEmail = "system@example.com",
            IsEnabled = false
        });

        sms.Success.Should().BeTrue();
        email.Success.Should().BeTrue();
        var keys = await setup.Context.TenantSettings.Select(x => x.Key).ToListAsync();
        keys.Should().Contain("Sms:IsEnabled");
        keys.Should().Contain("Email:IsEnabled");
        keys.Should().OnlyHaveUniqueItems();
        var smsResult = await setup.Service.GetAllByCategoryAsync("Sms");
        var emailResult = await setup.Service.GetAllByCategoryAsync("Email");
        smsResult.Success.Should().BeTrue();
        emailResult.Success.Should().BeTrue();
        smsResult.Data!.Should().ContainKey("Provider");
        smsResult.Data!.Should().ContainKey("IsEnabled");
        emailResult.Data!.Should().ContainKey("FromEmail");
        emailResult.Data!.Should().ContainKey("IsEnabled");
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
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "9001"),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", tenantId.ToString())
            ], "TestAuthentication"))
        };
        context.Items["TenantId"] = tenantId;
        return context;
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
