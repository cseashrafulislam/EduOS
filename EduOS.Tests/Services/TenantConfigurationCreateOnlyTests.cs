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

public sealed class TenantConfigurationCreateOnlyTests
{
    [Fact]
    public async Task Existing_setting_cannot_be_changed_by_duplicate_create()
    {
        await using var fixture = Create();
        var first = await fixture.Service.SaveSettingAsync(null, new SaveTenantSettingRequestDto
        {
            Key = "Portal.Title", Category = "Portal", Value = "Original"
        });
        first.Success.Should().BeTrue();
        var retry = await fixture.Service.SaveSettingAsync(null, new SaveTenantSettingRequestDto
        {
            Key = "Portal.Title", Category = "Portal", Value = "Original"
        });
        retry.Success.Should().BeTrue();
        var changed = await fixture.Service.SaveSettingAsync(null, new SaveTenantSettingRequestDto
        {
            Key = "Portal.Title", Category = "Portal", Value = "Different"
        });
        changed.StatusCode.Should().Be(409);
        (await fixture.Db.TenantSettings.SingleAsync()).Value.Should().Be("Original");
        (await fixture.Db.TenantSettings.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Existing_terminology_cannot_be_changed_by_duplicate_create()
    {
        await using var fixture = Create();
        var first = await fixture.Service.SaveTerminologyAsync(null, new SaveTenantTerminologyRequestDto
        {
            Key = "Student", SingularLabel = "Student", PluralLabel = "Students"
        });
        first.Success.Should().BeTrue();
        var retry = await fixture.Service.SaveTerminologyAsync(null, new SaveTenantTerminologyRequestDto
        {
            Key = "Student", SingularLabel = "Student", PluralLabel = "Students"
        });
        retry.Success.Should().BeTrue();
        var changed = await fixture.Service.SaveTerminologyAsync(null, new SaveTenantTerminologyRequestDto
        {
            Key = "Student", SingularLabel = "Learner", PluralLabel = "Learners"
        });
        changed.StatusCode.Should().Be(409);
        (await fixture.Db.TenantTerminologies.SingleAsync()).SingularLabel.Should().Be("Student");
        (await fixture.Db.TenantTerminologies.CountAsync()).Should().Be(1);
    }

    private static Fixture Create()
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("tenant-config-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
        var service = new TenantSettingService(
            new GenericRepository<TenantSetting>(db),
            new GenericRepository<TenantTerminology>(db), db,
            new TestUser(), new EphemeralDataProtectionProvider(),
            NullLogger<TenantSettingService>.Instance);
        return new Fixture(db, service);
    }

    private sealed record Fixture(EduOSDbContext Db, TenantSettingService Service) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class TestUser : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 71;
        public long TenantId => 101;
        public string? FullName => "Tenant Admin";
        public string? Email => null;
        public bool IsTenantAdmin => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
