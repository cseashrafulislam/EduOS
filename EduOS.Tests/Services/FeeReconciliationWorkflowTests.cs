using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class FeeReconciliationWorkflowTests
{
    [Fact]
    public async Task Cross_tenant_context_fails_closed_for_both_reports()
    {
        await using var db = Database(42);
        var service = new FeeReceivablesAgingService(db, new TestUser());
        (await service.GetReconciliationAsync(null, null, 1, 25)).StatusCode.Should().Be(403);
        (await service.GetReportAsync(new FeeReceivablesAgingQueryDto
            { AgingDate = new DateOnly(2026, 10, 27) })).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Reconciliation_rejects_invalid_pagination()
    {
        await using var db = Database();
        var service = new FeeReceivablesAgingService(db, new TestUser());
        (await service.GetReconciliationAsync(null, null, 0, 25)).StatusCode.Should().Be(400);
        (await service.GetReconciliationAsync(null, null, 1, 101)).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Reconciliation_flags_unexplained_paid_balance_without_changing_invoice()
    {
        await using var db = Database();
        Seed(db, paid: 40m);
        var service = new FeeReceivablesAgingService(db, new TestUser());
        var result = await service.GetReconciliationAsync(null, null, 1, 25);
        result.StatusCode.Should().Be(200);
        result.Data!.Rows.Should().ContainSingle();
        result.Data.Rows[0].Status.Should().Be("Requires review");
        result.Data.Rows[0].NetAllocatedAmount.Should().Be(0m);
        result.Data.PageExceptions.Should().Be(1);
        db.StudentInvoices.Single().PaidAmount.Should().Be(40m);
    }

    [Fact]
    public async Task Unposted_zero_paid_invoice_is_not_a_reconciliation_exception()
    {
        await using var db = Database();
        Seed(db, paid: 0m);
        var result = await new FeeReceivablesAgingService(db, new TestUser())
            .GetReconciliationAsync(null, null, 1, 25);
        result.StatusCode.Should().Be(200);
        result.Data!.Rows[0].Status.Should().Be("Unposted");
        result.Data.PageExceptions.Should().Be(0);
    }

    private static void Seed(EduOSDbContext db, decimal paid)
    {
        db.Campuses.Add(new Campus { Id = 7, TenantId = 41, Name = "Main", IsActive = true });
        db.Students.Add(new Student { Id = 8, TenantId = 41, StudentCode = "S-8", FullName = "Student" });
        db.StudentEnrollments.Add(new StudentEnrollment { Id = 9, TenantId = 41,
            StudentId = 8, CampusId = 7, RollNo = "8" });
        db.StudentInvoices.Add(new StudentInvoice { Id = 10, TenantId = 41,
            StudentEnrollmentId = 9, InvoiceNumber = "INV-10",
            InvoiceDate = new DateOnly(2026, 10, 1), TotalAmount = 100m,
            PaidAmount = paid, DueAmount = 100m - paid, CurrencyCode = "BDT" });
        db.SaveChanges();
    }

    private static EduOSDbContext Database(long tenantId = 41)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantId;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("finance-reconcile-" + Guid.NewGuid().ToString("N")).Options, accessor);
    }

    private sealed class TestUser : ICurrentUserService
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
