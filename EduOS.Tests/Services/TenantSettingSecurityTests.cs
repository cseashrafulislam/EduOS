using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Tenants;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TenantSettingSecurityTests
{
    [Fact]
    public async Task Sensitive_value_is_encrypted_redacted_and_preserved_when_mask_is_submitted()
    {
        await using var setup = Create();
        var original = await setup.Service.SaveSettingAsync(null, new SaveTenantSettingRequestDto
        {
            Key = "Sms.ApiKey", Category = "Sms", Value = "s3cr3t-value", IsSensitive = true
        });
        original.Success.Should().BeTrue(original.Message);
        var entity = await setup.Db.TenantSettings.SingleAsync();
        entity.Value.Should().StartWith("dp:v1:");
        entity.Value.Should().NotContain("s3cr3t-value");
        var read = await setup.Service.GetSettingAsync("Sms.ApiKey");
        read.Success.Should().BeTrue();
        read.Data!.Value.Should().BeNull();
        read.Data.HasValue.Should().BeTrue();
        var encrypted = entity.Value;
        var updated = await setup.Service.SaveSettingAsync(null, new SaveTenantSettingRequestDto
        {
            Key = "Sms.ApiKey", Category = "Sms", Value = "********", IsSensitive = true
        });
        updated.Success.Should().BeTrue(updated.Message);
        entity.Value.Should().Be(encrypted);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("script")]
    public async Task Branding_color_rejects_non_hex_values(string value)
    {
        await using var setup = Create();
        var response = await setup.Service.SaveSettingAsync(null, new SaveTenantSettingRequestDto
        {
            Key = "Branding.PrimaryColor", Category = "Branding", Value = value
        });
        response.Success.Should().BeFalse();
        (await setup.Db.TenantSettings.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Tenant_setting_reads_are_scoped_to_current_tenant()
    {
        await using var setup = Create();
        var written = await setup.Service.SaveSettingAsync(null, new SaveTenantSettingRequestDto
        {
            Key = "Branding.PrimaryColor", Category = "Branding", Value = "#334455"
        });
        written.Success.Should().BeTrue();
        var foreign = NewService(setup.Db, 202);
        (await foreign.GetSettingAsync("Branding.PrimaryColor")).StatusCode.Should().Be(404);
        (await foreign.GetSettingsAsync("Branding", 1, 20)).Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Sensitive_setting_cannot_be_downgraded_to_plaintext()
    {
        await using var setup = Create();
        (await setup.Service.SaveSettingAsync(null, new SaveTenantSettingRequestDto
        {
            Key = "Email.Password", Value = "secret", IsSensitive = true
        })).Success.Should().BeTrue();
        var response = await setup.Service.SaveSettingAsync(null, new SaveTenantSettingRequestDto
        {
            Key = "Email.Password", Value = "secret", IsSensitive = false
        });
        response.StatusCode.Should().Be(409);
        (await setup.Db.TenantSettings.SingleAsync()).IsSensitive.Should().BeTrue();
    }

    private static TestSetup Create()
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("settings-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
        return new TestSetup(db, NewService(db, 101));
    }

    private static TenantSettingService NewService(EduOSDbContext db, long tenant) => new(
        new GenericRepository<TenantSetting>(db),
        new GenericRepository<TenantTerminology>(db), db,
        new TestCurrentUser(tenant),
        new EphemeralDataProtectionProvider(),
        NullLogger<TenantSettingService>.Instance);

    private sealed record TestSetup(EduOSDbContext Db, TenantSettingService Service) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class TestCurrentUser(long tenant) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 71;
        public long TenantId => tenant;
        public string? FullName => "Tenant Admin";
        public string? Email => "owner@example.test";
        public bool IsTenantAdmin => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
