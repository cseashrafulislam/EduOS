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
    public async Task Sensitive_setting_is_encrypted_and_masked_from_gateway_response()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"tenant-settings-{Guid.NewGuid():N}")
            .Options;
        var httpContext = CreateHttpContext(101);
        var accessor = new HttpContextAccessor { HttpContext = httpContext };

        await using var db = new EduOSDbContext(options, accessor);
        var service = new TenantSettingService(
            new GenericRepository<TenantSetting>(db),
            db,
            new CurrentUserService(accessor),
            new EphemeralDataProtectionProvider(),
            NullLogger<TenantSettingService>.Instance);

        await service.SaveSmsGatewayAsync(new SmsGatewaySettingsDto
        {
            Provider = "Test",
            ApiUrl = "https://sms.example.test",
            ApiKey = "a-real-secret-value",
            SenderId = "EduOS",
            IsEnabled = true
        });

        var stored = await db.TenantSettings
            .SingleAsync(x => x.SettingKey == "ApiKey");
        stored.SettingValue.Should().NotBe("a-real-secret-value");
        stored.SettingValue.Should().StartWith("dp:v1:");

        var response = await service.GetSmsGatewayAsync();
        response.Success.Should().BeTrue();
        response.Data!.ApiKey.Should().Be("********");

        var protectedValue = stored.SettingValue;
        await service.SaveSmsGatewayAsync(new SmsGatewaySettingsDto
        {
            Provider = "Test",
            ApiUrl = "https://sms.example.test",
            ApiKey = "********",
            SenderId = "EduOS",
            IsEnabled = true
        });

        stored.SettingValue.Should().Be(protectedValue);
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
}
