using EduOS.Core.DTOs.Finance;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class FeeReceivablesAgingServiceTests
{
    [Fact]
    public async Task Staff_cannot_read_institutional_receivables()
    {
        await using var db = Database();
        var service = new FeeReceivablesAgingService(db, new TestUser(false));
        var result = await service.GetReportAsync(new FeeReceivablesAgingQueryDto
            { AgingDate = new DateOnly(2026, 10, 14) });
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Invalid_page_is_rejected_before_querying()
    {
        await using var db = Database();
        var service = new FeeReceivablesAgingService(db, new TestUser(true));
        var result = await service.GetReportAsync(new FeeReceivablesAgingQueryDto
            { AgingDate = new DateOnly(2026, 10, 14), PageSize = 101 });
        result.StatusCode.Should().Be(400);
    }

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("aging-" + Guid.NewGuid().ToString("N")).Options, accessor);
    }

    private sealed class TestUser(bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Staff"];
        public bool IsInRole(string role) => Roles.Contains(role);
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
