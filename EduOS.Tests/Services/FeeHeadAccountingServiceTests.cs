using EduOS.Core.DTOs.Finance;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class FeeHeadAccountingServiceTests
{
    [Fact]
    public async Task Staff_cannot_read_tenant_wide_fee_head_mappings()
    {
        await using var db = Database();
        var service = new FeeHeadAccountingService(db, new TestUser("Staff"), TimeProvider.System);
        var result = await service.GetSetupAsync();
        result.StatusCode.Should().Be(403);
    }
    [Fact]
    public async Task Accountant_cannot_change_fee_head_account_mappings()
    {
        await using var db = Database();
        var service = new FeeHeadAccountingService(db, new TestUser("Accountant"), TimeProvider.System);
        var result = await service.MapAccountsAsync(1, new SaveFeeHeadAccountMappingRequestDto
            { IncomeAccountId = 1, ReceivableAccountId = 2, RowVersion = "AQ==" });
        result.StatusCode.Should().Be(403);
    }
    [Fact]
    public async Task Invalid_mapping_rejected_before_database_transaction()
    {
        await using var db = Database();
        var service = new FeeHeadAccountingService(db, new TestUser("TenantAdmin"), TimeProvider.System);
        var result = await service.MapAccountsAsync(1, new SaveFeeHeadAccountMappingRequestDto
            { IncomeAccountId = 1, ReceivableAccountId = 1, RowVersion = "AQ==" });
        result.StatusCode.Should().Be(400);
    }
    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("fee-head-map-" + Guid.NewGuid().ToString("N")).Options, accessor);
    }
    private sealed class TestUser(string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
