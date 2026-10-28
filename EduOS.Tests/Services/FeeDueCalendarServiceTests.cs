using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class FeeDueCalendarServiceTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 12, 31);

    [Fact]
    public async Task Staff_cannot_read_due_calendar()
    {
        await using var db = Database();
        var response = await new FeeReceivablesAgingService(db, new User("Staff"))
            .GetDueCalendarAsync(new FeeDueCalendarQueryDto { FromDate = From, ToDate = To });
        response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Invalid_range_and_page_are_rejected()
    {
        await using var db = Database();
        var service = new FeeReceivablesAgingService(db, new User("TenantAdmin"));
        (await service.GetDueCalendarAsync(new FeeDueCalendarQueryDto
            { FromDate = To, ToDate = From })).StatusCode.Should().Be(400);
        (await service.GetDueCalendarAsync(new FeeDueCalendarQueryDto
            { FromDate = From, ToDate = To, PageSize = 101 })).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Campus_restricted_accountant_sees_only_granted_issued_invoices()
    {
        await using var db = Database();
        Seed(db);
        db.UserCampusAccesses.Add(new UserCampusAccess
            { Id = 1, TenantId = 41, UserId = 9, CampusId = 10, IsActive = true });
        await db.SaveChangesAsync();
        var service = new FeeReceivablesAgingService(db, new User("Accountant"));
        var report = await service.GetDueCalendarAsync(new FeeDueCalendarQueryDto
            { FromDate = From, ToDate = To });
        report.StatusCode.Should().Be(200);
        report.Data!.Invoices.TotalCount.Should().Be(1);
        report.Data.Invoices.Items.Single().InvoiceNumber.Should().Be("INV-10");
        report.Data.Months.Should().ContainSingle(x => x.CurrencyCode == "BDT" &&
            x.Outstanding == 70m && x.InvoiceCount == 1);
        (await service.GetDueCalendarAsync(new FeeDueCalendarQueryDto
            { FromDate = From, ToDate = To, CampusId = 20 })).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Tenant_administrator_gets_currency_separated_month_totals()
    {
        await using var db = Database();
        Seed(db);
        var result = await new FeeReceivablesAgingService(db, new User("TenantAdmin"))
            .GetDueCalendarAsync(new FeeDueCalendarQueryDto { FromDate = From, ToDate = To });
        result.StatusCode.Should().Be(200);
        result.Data!.Invoices.TotalCount.Should().Be(2);
        result.Data.Months.Should().HaveCount(2);
        result.Data.Months.Sum(x => x.Outstanding).Should().Be(120m);
    }

    [Fact]
    public async Task Corrupt_invoice_amounts_fail_closed()
    {
        await using var db = Database();
        Seed(db, corrupt: true);
        var result = await new FeeReceivablesAgingService(db, new User("TenantAdmin"))
            .GetDueCalendarAsync(new FeeDueCalendarQueryDto { FromDate = From, ToDate = To });
        result.StatusCode.Should().Be(409);
    }

    private static void Seed(EduOSDbContext db, bool corrupt = false)
    {
        db.Campuses.AddRange(
            new Campus { Id = 10, TenantId = 41, Code = "A", Name = "North" },
            new Campus { Id = 20, TenantId = 41, Code = "B", Name = "South" });
        db.Students.AddRange(
            new Student { Id = 1, TenantId = 41, StudentCode = "S1", FullName = "One" },
            new Student { Id = 2, TenantId = 41, StudentCode = "S2", FullName = "Two" });
        db.StudentEnrollments.AddRange(
            new StudentEnrollment { Id = 11, TenantId = 41, StudentId = 1, CampusId = 10 },
            new StudentEnrollment { Id = 22, TenantId = 41, StudentId = 2, CampusId = 20 });
        db.StudentInvoices.AddRange(
            new StudentInvoice { Id = 100, TenantId = 41, StudentEnrollmentId = 11,
                InvoiceNumber = "INV-10", InvoiceDate = From, DueDate = new DateOnly(2026, 10, 15),
                CurrencyCode = "BDT", TotalAmount = 100m, PaidAmount = 30m, DueAmount = corrupt ? 71m : 70m,
                State = InvoiceState.PartiallyPaid },
            new StudentInvoice { Id = 200, TenantId = 41, StudentEnrollmentId = 22,
                InvoiceNumber = "INV-20", InvoiceDate = From, DueDate = new DateOnly(2026, 11, 15),
                CurrencyCode = "USD", TotalAmount = 50m, DueAmount = 50m, State = InvoiceState.Issued });
        db.SaveChanges();
    }

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("fee-due-calendar-" + Guid.NewGuid().ToString("N")).Options, accessor);
    }

    private sealed class User(string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
