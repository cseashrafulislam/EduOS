using EduOS.Core.DTOs.Payroll;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.HR;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PayrollJournalPostingServiceTests
{
    [Fact]
    public async Task Staff_cannot_read_institutional_payroll_gl()
    {
        await using var db = Database();
        var result = await new PayrollJournalPostingService(db, new TestUser("Staff"), TimeProvider.System)
            .GetRegisterAsync(true, 1, 5);
        result.StatusCode.Should().Be(403);
    }
    [Fact]
    public async Task Accountant_cannot_post_payroll_gl()
    {
        await using var db = Database();
        var result = await new PayrollJournalPostingService(db, new TestUser("Accountant"), TimeProvider.System)
            .PostAsync(Guid.NewGuid(), ValidRequest());
        result.StatusCode.Should().Be(403);
    }
    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 0)]
    [InlineData(1, 11)]
    public async Task Register_rejects_invalid_pagination(int page, int size)
    {
        await using var db = Database();
        var result = await new PayrollJournalPostingService(db, new TestUser("TenantAdmin"), TimeProvider.System)
            .GetRegisterAsync(true, page, size);
        result.StatusCode.Should().Be(400);
    }
    [Fact]
    public async Task Posting_requires_unique_component_mapping()
    {
        await using var db = Database();
        var request = ValidRequest();
        request.Mappings = new[]
        {
            new PayrollComponentAccountMappingDto { SalaryComponentId = 1, AccountId = 3 },
            new PayrollComponentAccountMappingDto { SalaryComponentId = 1, AccountId = 4 }
        };
        var result = await new PayrollJournalPostingService(db, new TestUser("TenantAdmin"), TimeProvider.System)
            .PostAsync(Guid.NewGuid(), request);
        result.StatusCode.Should().Be(400);
    }
    [Fact]
    public async Task Posting_rejects_missing_run_without_creating_journal()
    {
        await using var db = Database();
        var result = await new PayrollJournalPostingService(db, new TestUser("TenantAdmin"), TimeProvider.System)
            .PostAsync(Guid.NewGuid(), ValidRequest());
        result.StatusCode.Should().Be(404);
        (await db.Journals.CountAsync()).Should().Be(0);
    }
    private static PostPayrollJournalRequestDto ValidRequest() => new()
    {
        AccountingPeriodId = 1, SalaryPayableAccountId = 2, RowVersion = "AQ==",
        Mappings = new[] { new PayrollComponentAccountMappingDto { SalaryComponentId = 1, AccountId = 3 } }
    };
    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("payroll-gl-" + Guid.NewGuid().ToString("N"))
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
