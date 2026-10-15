using EduOS.Core.Entities.SaaS;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TenantDomainWorkflowTests
{
    [Theory]
    [InlineData(" Portal.Example.COM. ", "portal.example.com")]
    [InlineData("school.example.org", "school.example.org")]
    [InlineData("localhost", null)]
    [InlineData("127.0.0.1", null)]
    [InlineData("http://example.com", null)]
    [InlineData("*.example.com", null)]
    [InlineData("portal.example.test", null)]
    [InlineData("foo..example.com", null)]
    [InlineData("example.com:443", null)]
    public void Custom_domain_host_normalization_is_safe(string input, string? expected)
        => Assert.Equal(expected, TenantDomainService.NormalizeHost(input));

    [Fact]
    public async Task Domain_directory_is_tenant_scoped()
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("domain-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
        db.TenantDomains.AddRange(
            new TenantDomain { TenantId = 101, HostName = "alpha.example.org", IsActive = false },
            new TenantDomain { TenantId = 202, HostName = "beta.example.org", IsActive = false });
        await db.SaveChangesAsync();
        var service = new TenantDomainService(db, db, new TestUser(101, true),
            new FakeLookup(), TimeProvider.System, NullLogger<TenantDomainService>.Instance);
        var response = await service.GetDomainsAsync();
        Assert.True(response.Success);
        Assert.Single(response.Data!);
        Assert.Equal("alpha.example.org", response.Data![0].HostName);
    }

    [Fact]
    public async Task Non_administrator_cannot_read_domain_directory()
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("domain-deny-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
        var service = new TenantDomainService(db, db, new TestUser(101, false),
            new FakeLookup(), TimeProvider.System, NullLogger<TenantDomainService>.Instance);
        Assert.Equal(403, (await service.GetDomainsAsync()).StatusCode);
    }

    private sealed class FakeLookup : ITenantDomainTxtLookup
    {
        public Task<IReadOnlyList<string>> GetTxtAsync(string name, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private sealed class TestUser(long tenant, bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 19;
        public long TenantId => tenant;
        public string? FullName => "Test";
        public string? Email => "test@example.org";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : [];
        public bool IsInRole(string role) => admin && role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
