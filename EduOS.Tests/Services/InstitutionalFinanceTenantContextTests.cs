using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Accounting;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

/// <summary>
/// A valid tenant-admin role must not override a mismatched request/DbContext tenant.
/// These checks run before querying even empty institutional ledgers or reservations.
/// </summary>
public sealed class InstitutionalFinanceTenantContextTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 27);

    [Fact]
    public async Task Mismatched_tenant_context_denies_every_institution_wide_accounting_report()
    {
        await using var db = Database(contextTenantId: 42);
        var service = new AccountingReportingService(db, new TenantAdmin());

        (await service.GetIncomeStatementAsync(From, To)).StatusCode.Should().Be(403);
        (await service.GetBalanceSheetAsync(To)).StatusCode.Should().Be(403);
        (await service.GetBankBookAsync(1, From, To)).StatusCode.Should().Be(403);
        (await service.GetBankLiquidityAsync(From, To)).StatusCode.Should().Be(403);
        (await service.GetTrialBalanceAsync(To)).StatusCode.Should().Be(403);
        (await service.GetLedgerStatementAsync(1, From, To)).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Mismatched_tenant_context_denies_finance_mutations_and_reservation_reads()
    {
        await using var db = Database(contextTenantId: 42);
        var service = new FinanceAdjustmentService(db, new TenantAdmin(), TimeProvider.System);

        (await service.GetDiscountRulesAsync()).StatusCode.Should().Be(403);
        (await service.GetRefundRegisterAsync(null)).StatusCode.Should().Be(403);
        (await service.GetReservationFinancePreflightAsync("hostel", 1)).StatusCode.Should().Be(403);
    }

    private static EduOSDbContext Database(long contextTenantId)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = contextTenantId;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("institutional-finance-tenant-context-" + Guid.NewGuid().ToString("N"))
            .Options, accessor);
    }

    private sealed class TenantAdmin : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
