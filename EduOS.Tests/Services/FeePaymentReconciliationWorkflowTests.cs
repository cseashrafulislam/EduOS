using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
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

public sealed class FeePaymentReconciliationWorkflowTests
{
    [Fact]
    public async Task Tenant_context_mismatch_fails_closed()
    {
        await using var db = Database(42);
        var result = await new FeeReceivablesAgingService(db, new TestUser())
            .GetPaymentReconciliationAsync(null, null, 1, 25);
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Invalid_pagination_is_rejected()
    {
        await using var db = Database();
        var result = await new FeeReceivablesAgingService(db, new TestUser())
            .GetPaymentReconciliationAsync(null, null, 1, 101);
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Unallocated_successful_receipt_is_visible_to_admin_and_flagged()
    {
        await using var db = Database();
        Seed(db);
        var result = await new FeeReceivablesAgingService(db, new TestUser())
            .GetPaymentReconciliationAsync(null, null, 1, 25);
        result.StatusCode.Should().Be(200);
        result.Data!.Rows.Should().ContainSingle();
        result.Data.Rows[0].Status.Should().Be("Requires review");
        result.Data.Rows[0].Issues.Should().Contain("no invoice allocation");
        result.Data.PageExceptions.Should().Be(1);
    }

    [Fact]
    public async Task Fully_allocated_unposted_receipt_is_valid()
    {
        await using var db = Database();
        Seed(db);
        db.PaymentAllocations.Add(new PaymentAllocation
        {
            Id = 30, TenantId = 41, StudentPaymentId = 20, StudentInvoiceId = 10, Amount = 100m
        });
        await db.SaveChangesAsync();
        var result = await new FeeReceivablesAgingService(db, new TestUser())
            .GetPaymentReconciliationAsync(null, null, 1, 25);
        result.StatusCode.Should().Be(200);
        result.Data!.Rows[0].Status.Should().Be("Unposted");
        result.Data.Rows[0].GrossAllocated.Should().Be(100m);
        result.Data.Rows[0].NetAllocated.Should().Be(100m);
    }

    [Fact]
    public async Task Deleted_allocation_remains_visible_as_historical_exception()
    {
        await using var db = Database();
        Seed(db);
        db.PaymentAllocations.Add(new PaymentAllocation
        {
            Id = 30, TenantId = 41, StudentPaymentId = 20, StudentInvoiceId = 10,
            Amount = 100m, IsDeleted = true
        });
        await db.SaveChangesAsync();
        var result = await new FeeReceivablesAgingService(db, new TestUser())
            .GetPaymentReconciliationAsync(null, null, 1, 25);
        result.StatusCode.Should().Be(200);
        result.Data!.Rows[0].Issues.Should().Contain("Historical or invalid payment allocation");
        result.Data.Rows[0].GrossAllocated.Should().Be(0m);
    }

    [Fact]
    public async Task Unallocated_receipt_is_not_exposed_to_campus_limited_user()
    {
        await using var db = Database();
        Seed(db);
        var result = await new FeeReceivablesAgingService(db, new TestUser(false))
            .GetPaymentReconciliationAsync(null, null, 1, 25);
        result.StatusCode.Should().Be(200);
        result.Data!.Rows.Should().BeEmpty();
    }

    private static void Seed(EduOSDbContext db)
    {
        db.Campuses.Add(new Campus { Id = 7, TenantId = 41, Name = "Main", IsActive = true });
        db.Students.Add(new Student { Id = 8, TenantId = 41, StudentCode = "S-8", FullName = "Student" });
        db.StudentEnrollments.Add(new StudentEnrollment
        { Id = 9, TenantId = 41, StudentId = 8, CampusId = 7, RollNo = "8" });
        db.StudentInvoices.Add(new StudentInvoice
        { Id = 10, TenantId = 41, StudentEnrollmentId = 9, InvoiceNumber = "INV-10",
          InvoiceDate = new DateOnly(2026, 10, 1), TotalAmount = 100m,
          DueAmount = 100m, CurrencyCode = "BDT" });
        db.StudentPayments.Add(new StudentPayment
        { Id = 20, TenantId = 41, StudentId = 8, ReceiptNumber = "R-20",
          PaymentDate = new DateOnly(2026, 10, 27), Amount = 100m,
          CurrencyCode = "BDT", State = PaymentState.Successful });
        db.SaveChanges();
    }

    private static EduOSDbContext Database(long tenantId = 41)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantId;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("payment-reconcile-" + Guid.NewGuid().ToString("N")).Options, accessor);
    }

    private sealed class TestUser(bool admin = true) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Accountant"];
        public bool IsInRole(string role) => Roles.Contains(role);
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
