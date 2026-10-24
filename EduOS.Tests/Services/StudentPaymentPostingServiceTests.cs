using EduOS.Core.DTOs.Finance;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentPaymentPostingServiceTests
{
    [Fact]
    public async Task Staff_cannot_read_receipt_posting_register()
    {
        await using var db = Database();
        var result = await new StudentPaymentPostingService(db, new TestUser("Staff"), TimeProvider.System)
            .GetRegisterAsync(null, null, true, 1, 25);
        result.StatusCode.Should().Be(403);
    }
    [Fact]
    public async Task Accountant_cannot_post_institutional_cash_journal()
    {
        await using var db = Database();
        var result = await new StudentPaymentPostingService(db, new TestUser("Accountant"), TimeProvider.System)
            .PostAsync(Guid.NewGuid(), ValidRequest());
        result.StatusCode.Should().Be(403);
    }
    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Register_rejects_invalid_pagination(int page, int pageSize)
    {
        await using var db = Database();
        var result = await new StudentPaymentPostingService(db, new TestUser("TenantAdmin"), TimeProvider.System)
            .GetRegisterAsync(null, null, true, page, pageSize);
        result.StatusCode.Should().Be(400);
    }
    [Fact]
    public async Task Posting_requires_cash_account_and_concurrency_token()
    {
        await using var db = Database();
        var result = await new StudentPaymentPostingService(db, new TestUser("TenantAdmin"), TimeProvider.System)
            .PostAsync(Guid.NewGuid(), new PostStudentPaymentJournalRequestDto
            { AccountingPeriodId = 1, CashAccountId = 0, RowVersion = "" });
        result.StatusCode.Should().Be(400);
    }
    private static PostStudentPaymentJournalRequestDto ValidRequest() =>
        new() { AccountingPeriodId = 1, CashAccountId = 1, RowVersion = "AQ==" };
    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("payment-gl-" + Guid.NewGuid().ToString("N")).Options, accessor);
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
