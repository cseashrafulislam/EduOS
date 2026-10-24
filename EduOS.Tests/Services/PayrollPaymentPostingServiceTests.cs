using EduOS.Core.DTOs.Payroll;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.HR;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PayrollPaymentPostingServiceTests
{
    [Fact]
    public async Task Staff_cannot_read_payroll_payment_register()
    {
        await using var db = Database();
        var response = await new PayrollPaymentPostingService(db, new TestUser("Staff"), TimeProvider.System)
            .GetRegisterAsync(true, 1, 10);
        response.StatusCode.Should().Be(403);
    }
    [Fact]
    public async Task Accountant_cannot_verify_payroll_payment()
    {
        await using var db = Database();
        var response = await new PayrollPaymentPostingService(db, new TestUser("Accountant"), TimeProvider.System)
            .PostAsync(Guid.NewGuid(), ValidRequest());
        response.StatusCode.Should().Be(403);
    }
    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 11)]
    public async Task Register_rejects_invalid_pagination(int page, int size)
    {
        await using var db = Database();
        var response = await new PayrollPaymentPostingService(db, new TestUser("TenantAdmin"), TimeProvider.System)
            .GetRegisterAsync(true, page, size);
        response.StatusCode.Should().Be(400);
    }
    [Fact]
    public async Task Post_requires_period_settlement_ledger_and_version()
    {
        await using var db = Database();
        var response = await new PayrollPaymentPostingService(db, new TestUser("TenantAdmin"), TimeProvider.System)
            .PostAsync(Guid.NewGuid(), new PostPayrollPaymentJournalRequestDto
            { AccountingPeriodId = 1, SettlementAccountId = 0, RowVersion = "" });
        response.StatusCode.Should().Be(400);
    }
    [Fact]
    public async Task Missing_payment_does_not_create_a_journal()
    {
        await using var db = Database();
        var response = await new PayrollPaymentPostingService(db, new TestUser("TenantAdmin"), TimeProvider.System)
            .PostAsync(Guid.NewGuid(), ValidRequest());
        response.StatusCode.Should().Be(404);
        (await db.Journals.CountAsync()).Should().Be(0);
    }
    private static PostPayrollPaymentJournalRequestDto ValidRequest() =>
        new() { AccountingPeriodId = 1, SettlementAccountId = 2, RowVersion = "AQ==" };
    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("payroll-payment-gl-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, accessor);
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
