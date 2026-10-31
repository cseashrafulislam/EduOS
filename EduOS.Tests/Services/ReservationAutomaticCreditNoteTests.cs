using EduOS.Service.Services.Finance;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ReservationAutomaticCreditNoteTests
{
    [Fact]
    public void Credit_key_is_stable_and_scoped_to_tenant_operation_and_clearance()
    {
        var key = ReservationAutomaticCreditNote.CreditKey(11, "HOSTEL", 701);
        Assert.Equal(key, ReservationAutomaticCreditNote.CreditKey(11, "HOSTEL", 701));
        Assert.NotEqual(key, ReservationAutomaticCreditNote.CreditKey(11, "TRANSPORT", 701));
        Assert.NotEqual(key, ReservationAutomaticCreditNote.CreditKey(12, "HOSTEL", 701));
        Assert.NotEqual(key, ReservationAutomaticCreditNote.CreditKey(11, "HOSTEL", 702));
        Assert.NotEqual(ReservationAutomaticInvoicePosting.InvoiceKey(11, "HOSTEL", 701), key);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReservationAutomaticCreditNote.CreditKey(11, "INVALID", 701));
    }

    [Theory]
    [InlineData(150, 120, 200, 120, 30)]
    [InlineData(50, 120, 200, 50, 0)]
    [InlineData(100, 0, 200, 0, 100)]
    [InlineData(80, 70, 150, 70, 10)]
    [InlineData(50, 0, 50, 0, 50)]
    public void Credit_applies_to_unpaid_receivable_before_paid_student_liability(
        decimal credit, decimal remainingDue, decimal capacity, decimal ar, decimal liability)
    {
        var actual = ReservationAutomaticCreditNote.Split(credit, remainingDue, capacity);
        Assert.Equal(ar, actual.ReceivableReduction);
        Assert.Equal(liability, actual.StudentCreditPayable);
        Assert.Equal(credit, actual.ReceivableReduction + actual.StudentCreditPayable);
    }

    [Theory]
    [InlineData(0, 0, 100)]
    [InlineData(-1, 0, 100)]
    [InlineData(101, 20, 100)]
    [InlineData(10, -1, 100)]
    [InlineData(10, 101, 100)]
    [InlineData(10.001, 0, 100)]
    public void Over_credit_and_malformed_money_fail_closed(decimal credit, decimal due, decimal capacity) =>
        Assert.Throws<InvalidOperationException>(() =>
            ReservationAutomaticCreditNote.Split(credit, due, capacity));

    [Fact]
    public async Task Only_posted_unreversed_credit_lines_affect_live_invoice_balance()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("reservation-credit-" + Guid.NewGuid().ToString("N")).Options, accessor);
        db.Accounts.AddRange(
            new Account { Id = 100, TenantId = 41, Code = "AR", Name = "Receivable", AccountType = AccountType.Asset },
            new Account { Id = 101, TenantId = 41, Code = "SCP", Name = "Student Credit", AccountType = AccountType.Liability },
            new Account { Id = 102, TenantId = 41, Code = "REV", Name = "Income", AccountType = AccountType.Income });
        db.Journals.AddRange(
            new Journal { Id = 1000, TenantId = 41, JournalNumber = "CR-1", State = JournalState.Posted,
                SourceType = ReservationAutomaticCreditNote.SourceType, SourceId = 55 },
            new Journal { Id = 1001, TenantId = 41, JournalNumber = "CR-2", State = JournalState.Approved,
                SourceType = ReservationAutomaticCreditNote.SourceType, SourceId = 55 },
            new Journal { Id = 1002, TenantId = 41, JournalNumber = "CR-3", State = JournalState.Posted,
                SourceType = "StudentInvoice", SourceId = 55 },
            new Journal { Id = 1003, TenantId = 41, JournalNumber = "CR-4", State = JournalState.Posted,
                SourceType = ReservationAutomaticCreditNote.SourceType, SourceId = 56 },
            new Journal { Id = 1004, TenantId = 41, JournalNumber = "CR-4R", State = JournalState.Posted,
                ReversalOfJournalId = 1003 });
        db.JournalLines.AddRange(
            new JournalLine { Id = 2001, TenantId = 41, JournalId = 1000, AccountId = 102,
                LineNo = 1, DebitAmount = 150 },
            new JournalLine { Id = 2002, TenantId = 41, JournalId = 1000, AccountId = 100,
                LineNo = 2, CreditAmount = 120 },
            new JournalLine { Id = 2003, TenantId = 41, JournalId = 1000, AccountId = 101,
                LineNo = 3, CreditAmount = 30 },
            new JournalLine { Id = 2004, TenantId = 41, JournalId = 1001, AccountId = 100,
                LineNo = 1, CreditAmount = 50 },
            new JournalLine { Id = 2005, TenantId = 41, JournalId = 1002, AccountId = 100,
                LineNo = 1, CreditAmount = 5 },
            new JournalLine { Id = 2006, TenantId = 41, JournalId = 1003, AccountId = 100,
                LineNo = 1, CreditAmount = 80 });
        await db.SaveChangesAsync();

        var credits = await ReservationCreditNoteBalances.ForInvoicesAsync(db, 41, [55, 56], default);
        Assert.Single(credits);
        Assert.Equal(120m, credits[55].UnpaidReduction);
        Assert.Equal(30m, credits[55].StudentCreditPayable);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReservationCreditNoteBalances.ForInvoicesAsync(db, 42, [55], default));
    }

    [Fact]
    public async Task Paid_and_unpaid_original_invoice_creates_one_balanced_approved_then_posted_credit()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        await using var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("reservation-credit-post-" + Guid.NewGuid().ToString("N")).Options, accessor);
        var invoiceRef = Guid.NewGuid();
        var today = new DateOnly(2026, 10, 31);
        db.StudentEnrollments.Add(new StudentEnrollment
        {
            Id = 9, TenantId = 41, StudentId = 7, CampusId = 8, IsCurrent = true,
            State = EnrollmentState.Active
        });
        db.Accounts.AddRange(
            new Account { Id = 100, TenantId = 41, Code = "AR", Name = "Receivable", AccountType = AccountType.Asset },
            new Account { Id = 101, TenantId = 41, Code = "SCP", Name = "Student Credit", AccountType = AccountType.Liability },
            new Account { Id = 102, TenantId = 41, Code = "REV", Name = "Income", AccountType = AccountType.Income });
        db.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = 2, TenantId = 41, Name = "October", StartDate = today.AddDays(-30),
            EndDate = today.AddDays(30)
        });
        db.StudentInvoices.Add(new StudentInvoice
        {
            Id = 55, TenantId = 41, PublicId = invoiceRef, StudentEnrollmentId = 9,
            InvoiceNumber = "INV-55", InvoiceDate = today, DueDate = today,
            Subtotal = 200m, TotalAmount = 200m, PaidAmount = 80m, DueAmount = 120m,
            State = InvoiceState.PartiallyPaid, JournalId = 100
        });
        db.StudentInvoiceLines.Add(new StudentInvoiceLine
        {
            Id = 56, TenantId = 41, StudentInvoiceId = 55, FeeHeadId = 12,
            Description = "Reservation", Amount = 200m, NetAmount = 200m
        });
        db.Journals.AddRange(
            new Journal { Id = 100, TenantId = 41, JournalNumber = "SRC-100", State = JournalState.Posted,
                SourceType = "StudentInvoice", SourceId = 55, CampusId = 8 },
            new Journal { Id = 101, TenantId = 41, JournalNumber = "REC-101", State = JournalState.Posted,
                SourceType = "StudentPayment", SourceId = 700, CampusId = 8 });
        db.JournalLines.AddRange(
            new JournalLine { Id = 1, TenantId = 41, JournalId = 100, LineNo = 1,
                AccountId = 100, CampusId = 8, StudentId = 7, DebitAmount = 200m },
            new JournalLine { Id = 2, TenantId = 41, JournalId = 100, LineNo = 2,
                AccountId = 102, CampusId = 8, StudentId = 7, FeeHeadId = 12, CreditAmount = 200m });
        db.StudentPayments.Add(new StudentPayment
        {
            Id = 700, TenantId = 41, StudentId = 7, ReceiptNumber = "R-700",
            PaymentDate = today, Amount = 80m, JournalId = 101,
            State = PaymentState.Successful, ReceivedByUserId = 31
        });
        db.PaymentAllocations.Add(new PaymentAllocation
        {
            Id = 701, TenantId = 41, StudentPaymentId = 700, StudentInvoiceId = 55, Amount = 80m
        });
        await db.SaveChangesAsync();

        var (journalId, error) = await ReservationAutomaticCreditNote.PrepareAsync(
            db, 41, 22, 31, "HOSTEL", 901, 8, 7, 9, -150m, invoiceRef,
            101, 2, today, DateTime.UtcNow, default);
        Assert.Null(error);
        Assert.NotNull(journalId);
        Assert.True(await ReservationAutomaticCreditNote.PreparedAsync(
            db, 41, "HOSTEL", 901, journalId!.Value, 8, 7, 9, -150m, default));
        var creditEntries = await db.JournalLines.AsNoTracking()
            .Where(x => x.JournalId == journalId).OrderBy(x => x.LineNo).ToListAsync();
        Assert.Equal(3, creditEntries.Count);
        Assert.Equal(150m, creditEntries[0].DebitAmount);
        Assert.Equal(120m, creditEntries[1].CreditAmount);
        Assert.Equal(30m, creditEntries[2].CreditAmount);
        Assert.True(await ReservationAutomaticCreditNote.FinalizeAsync(
            db, 41, "HOSTEL", 901, journalId.Value, 8, 7, 9, -150m,
            31, DateTime.UtcNow, default));
        Assert.Equal(JournalState.Posted,
            (await db.Journals.SingleAsync(x => x.Id == journalId)).State);
        var unchanged = await db.StudentInvoices.AsNoTracking().SingleAsync(x => x.Id == 55);
        Assert.Equal(200m, unchanged.TotalAmount);
        Assert.Equal(80m, unchanged.PaidAmount);
        Assert.Equal(120m, unchanged.DueAmount);
        var balance = (await ReservationCreditNoteBalances.ForInvoicesAsync(db, 41, [55], default))[55];
        Assert.Equal(120m, balance.UnpaidReduction);
        Assert.Equal(30m, balance.StudentCreditPayable);

        var (duplicate, rejection) = await ReservationAutomaticCreditNote.PrepareAsync(
            db, 41, 22, 31, "TRANSPORT", 902, 8, 7, 9, -60m, invoiceRef,
            101, 2, today, DateTime.UtcNow, default);
        Assert.Null(duplicate);
        Assert.NotNull(rejection);
    }
}
