using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Accounting;
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

    [Fact]
    public async Task Paid_refund_without_allocation_is_flagged()
    {
        await using var db = Database();
        Seed(db);
        db.PaymentAllocations.Add(new PaymentAllocation
        { Id = 30, TenantId = 41, StudentPaymentId = 20, StudentInvoiceId = 10, Amount = 100m });
        db.Refunds.Add(new Refund
        { Id = 40, TenantId = 41, StudentPaymentId = 20, Amount = 20m,
          State = RefundState.Paid, ReversalJournalId = 99, Reason = "Return" });
        await db.SaveChangesAsync();
        var result = await new FeeReceivablesAgingService(db, new TestUser())
            .GetPaymentReconciliationAsync(null, null, 1, 25);
        result.StatusCode.Should().Be(200);
        result.Data!.Rows[0].Status.Should().Be("Requires review");
        result.Data.Rows[0].Issues.Should().Contain("Paid refund total");
    }

    [Fact]
    public async Task Overallocated_receipt_is_flagged()
    {
        await using var db = Database();
        Seed(db);
        db.PaymentAllocations.Add(new PaymentAllocation
        { Id = 30, TenantId = 41, StudentPaymentId = 20, StudentInvoiceId = 10, Amount = 120m });
        await db.SaveChangesAsync();
        var result = await new FeeReceivablesAgingService(db, new TestUser())
            .GetPaymentReconciliationAsync(null, null, 1, 25);
        result.StatusCode.Should().Be(200);
        result.Data!.Rows[0].GrossAllocated.Should().Be(120m);
        result.Data.Rows[0].Issues.Should().Contain("differs from allocated");
    }

    [Fact]
    public async Task Orphan_allocation_cannot_bypass_campus_scoped_receipt_visibility()
    {
        await using var db = Database();
        Seed(db);
        db.UserCampusAccesses.Add(new UserCampusAccess
        { Id = 50, TenantId = 41, UserId = 9, CampusId = 7, IsActive = true });
        db.PaymentAllocations.AddRange(
            new PaymentAllocation
            { Id = 30, TenantId = 41, StudentPaymentId = 20, StudentInvoiceId = 10, Amount = 50m },
            new PaymentAllocation
            { Id = 31, TenantId = 41, StudentPaymentId = 20, StudentInvoiceId = 999, Amount = 50m });
        await db.SaveChangesAsync();
        var service = new FeeReceivablesAgingService(db, new TestUser(false));
        var scoped = await service.GetPaymentReconciliationAsync(null, null, 1, 25);
        scoped.StatusCode.Should().Be(200);
        scoped.Data!.Rows.Should().BeEmpty();
        var admin = await new FeeReceivablesAgingService(db, new TestUser())
            .GetPaymentReconciliationAsync(null, null, 1, 25);
        admin.Data!.Rows.Should().ContainSingle();
        admin.Data.Rows[0].Issues.Should().Contain("Unresolved allocation campus provenance");
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task Paid_refund_journal_is_reconciled_in_both_finance_reports(
        bool deletedLine, bool wrongSource, bool reversed)
    {
        await using var db = Database();
        SeedPaidRefund(db, deletedLine, wrongSource, reversed);
        var service = new FeeReceivablesAgingService(db, new TestUser());
        var receipt = await service.GetPaymentReconciliationAsync(null, null, 1, 25);
        var invoice = await service.GetReconciliationAsync(null, null, 1, 25);
        receipt.StatusCode.Should().Be(200);
        invoice.StatusCode.Should().Be(200);
        var valid = !deletedLine && !wrongSource && !reversed;
        if (valid)
        {
            receipt.Data!.Rows[0].Status.Should().Be("Unposted");
            invoice.Data!.Rows[0].Status.Should().Be("Unposted");
        }
        else
        {
            receipt.Data!.Rows[0].Issues.Should().Contain("Paid refund journal requires reconciliation");
            invoice.Data!.Rows[0].Issues.Should().Contain("Paid refund journal requires reconciliation");
        }
    }

    [Fact]
    public async Task Soft_deleted_paid_refund_journal_is_reported_in_both_workspaces()
    {
        await using var db = Database();
        SeedPaidRefund(db, false, false, false);
        db.Journals.Single(x => x.Id == 70).IsDeleted = true;
        await db.SaveChangesAsync();
        var service = new FeeReceivablesAgingService(db, new TestUser());
        (await service.GetPaymentReconciliationAsync(null, null, 1, 25))
            .Data!.Rows[0].Issues.Should().Contain("Paid refund journal requires reconciliation");
        (await service.GetReconciliationAsync(null, null, 1, 25))
            .Data!.Rows[0].Issues.Should().Contain("Paid refund journal requires reconciliation");
    }

    [Fact]
    public async Task Shared_paid_refund_journal_is_not_accepted_as_independent_provenance()
    {
        await using var db = Database();
        SeedPaidRefund(db, false, false, false);
        db.Refunds.Add(new Refund
        {
            Id = 41, TenantId = 41, StudentPaymentId = 20, Amount = 5m,
            State = RefundState.Paid, ReversalJournalId = 70, Reason = "Duplicate link"
        });
        await db.SaveChangesAsync();
        var service = new FeeReceivablesAgingService(db, new TestUser());
        (await service.GetPaymentReconciliationAsync(null, null, 1, 25))
            .Data!.Rows[0].Issues.Should().Contain("Paid refund journal requires reconciliation");
        (await service.GetReconciliationAsync(null, null, 1, 25))
            .Data!.Rows[0].Issues.Should().Contain("Paid refund journal requires reconciliation");
    }

    private static void SeedPaidRefund(EduOSDbContext db, bool deletedLine, bool wrongSource, bool reversed)
    {
        Seed(db);
        var invoice = db.StudentInvoices.Single();
        invoice.PaidAmount = 80m;
        invoice.DueAmount = 20m;
        db.PaymentAllocations.Add(new PaymentAllocation
        { Id = 30, TenantId = 41, StudentPaymentId = 20, StudentInvoiceId = 10, Amount = 100m });
        db.Refunds.Add(new Refund
        { Id = 40, TenantId = 41, StudentPaymentId = 20, Amount = 20m,
          State = RefundState.Paid, ReversalJournalId = 70, Reason = "Return" });
        db.RefundAllocations.Add(new RefundAllocation
        { Id = 50, TenantId = 41, RefundId = 40, PaymentAllocationId = 30, Amount = 20m });
        db.Journals.Add(new Journal
        {
            Id = 70, TenantId = 41, JournalNumber = "JRN-70",
            SourceType = wrongSource ? "Other" : "StudentRefund", SourceId = 40,
            State = JournalState.Posted, CampusId = 7, CurrencyCode = "BDT"
        });
        db.JournalLines.AddRange(
            new JournalLine
            { Id = 71, TenantId = 41, JournalId = 70, LineNo = 1,
              AccountId = 100, CampusId = 7, StudentId = 8, DebitAmount = 20m,
              IsDeleted = deletedLine },
            new JournalLine
            { Id = 72, TenantId = 41, JournalId = 70, LineNo = 2,
              AccountId = 101, CampusId = 7, StudentId = 8, CreditAmount = 20m });
        if (reversed)
            db.Journals.Add(new Journal
            {
                Id = 73, TenantId = 41, JournalNumber = "JRN-73",
                State = JournalState.Posted, ReversalOfJournalId = 70
            });
        db.SaveChanges();
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
