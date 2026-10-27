using EduOS.Core.DTOs.Finance;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace EduOS.Tests.Services;
public sealed class StudentInvoicePostingServiceTests
{
    [Fact]
    public async Task Staff_cannot_read_invoice_posting_register()
    {
        await using var db = Database();
        var result = await new StudentInvoicePostingService(db, new TestUser("Staff"), TimeProvider.System)
            .GetRegisterAsync(null, null, true, 1, 25);
        result.StatusCode.Should().Be(403);
    }
    [Fact]
    public async Task Accountant_cannot_post_institution_wide_journal()
    {
        await using var db = Database();
        var result = await new StudentInvoicePostingService(db, new TestUser("Accountant"), TimeProvider.System)
            .PostAsync(Guid.NewGuid(), new PostStudentInvoiceJournalRequestDto
                { AccountingPeriodId = 1, RowVersion = "AQ==" });
        result.StatusCode.Should().Be(403);
    }
    [Fact]
    public async Task Posting_requires_open_period_and_rowversion()
    {
        await using var db = Database();
        var result = await new StudentInvoicePostingService(db, new TestUser("TenantAdmin"), TimeProvider.System)
            .PostAsync(Guid.NewGuid(), new PostStudentInvoiceJournalRequestDto
                { AccountingPeriodId = 0, RowVersion = "" });
        result.StatusCode.Should().Be(400);
    }
    [Fact]
    public async Task Invalid_page_size_is_rejected_before_database_query()
    {
        await using var db = Database();
        var result = await new StudentInvoicePostingService(db, new TestUser("TenantAdmin"), TimeProvider.System)
            .GetRegisterAsync(null, null, true, 1, 101);
        result.StatusCode.Should().Be(400);
    }
    [Fact]
    public async Task Mismatched_request_and_DbContext_tenant_denies_register_and_post()
    {
        await using var db = Database(42);
        var service = new StudentInvoicePostingService(db, new TestUser("TenantAdmin"), TimeProvider.System);
        (await service.GetRegisterAsync(null, null, true, 1, 25)).StatusCode.Should().Be(403);
        (await service.PostAsync(Guid.NewGuid(), new PostStudentInvoiceJournalRequestDto { AccountingPeriodId = 1, RowVersion = "AQ==" })).StatusCode.Should().Be(403);
    }
    private static EduOSDbContext Database(long contextTenantId = 41)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = contextTenantId;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("invoice-gl-" + Guid.NewGuid().ToString("N")).Options, accessor);
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
