using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.HR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PayrollReconciliationJournalEvidenceTests
{
    private static readonly DateOnly PeriodEnd = new(2026, 10, 31);
    private static readonly DateOnly PaymentDate = new(2026, 11, 1);

    [Fact]
    public async Task Valid_posted_cash_payment_requires_exact_historical_gl_provenance()
    {
        await using var db = Database();
        Seed(db);
        await db.SaveChangesAsync();

        var result = await new PayrollReconciliationService(db, new User())
            .GetAsync(2026, 10, 7, 1, 10);

        Assert.Equal(200, result.StatusCode);
        var row = Assert.Single(result.Data!.Items);
        Assert.Equal("Verified", row.AccrualStatus);
        Assert.Equal("Partial", row.SettlementStatus);
        Assert.Equal(40m, row.VerifiedPaid);
        Assert.Equal(60m, row.Outstanding);
    }

    [Theory]
    [InlineData("wrong-date")]
    [InlineData("wrong-campus")]
    [InlineData("wrong-employee")]
    [InlineData("wrong-description")]
    [InlineData("unsupported-method")]
    [InlineData("bank-missing-destination")]
    [InlineData("soft-deleted-line")]
    public async Task False_payment_evidence_never_counts_as_verified_salary(string defect)
    {
        await using var db = Database();
        Seed(db);
        var payment = db.PayrollPayments.Local.Single();
        var journal = db.Journals.Local.Single(x => x.Id == 2);
        var debit = db.JournalLines.Local.Single(x => x.Id == 3);
        switch (defect)
        {
            case "wrong-date": journal.JournalDate = PaymentDate.AddDays(1); break;
            case "wrong-campus": journal.CampusId = 8; break;
            case "wrong-employee": debit.EmployeeId = 999; break;
            case "wrong-description": debit.Description = "Unrelated journal line"; break;
            case "unsupported-method": payment.PaymentMethod = PaymentMethodType.Card; break;
            case "bank-missing-destination": payment.PaymentMethod = PaymentMethodType.BankTransfer; break;
            case "soft-deleted-line": debit.IsDeleted = true; break;
        }
        await db.SaveChangesAsync();

        var result = await new PayrollReconciliationService(db, new User())
            .GetAsync(2026, 10, 7, 1, 10);

        Assert.Equal(200, result.StatusCode);
        var row = Assert.Single(result.Data!.Items);
        Assert.Equal("NeedsReview", row.SettlementStatus);
        Assert.Equal(0m, row.VerifiedPaid);
        Assert.Equal(100m, row.Outstanding);
    }

    [Fact]
    public async Task Wrong_accrual_date_invalidates_salary_payable_evidence()
    {
        await using var db = Database();
        Seed(db);
        db.Journals.Local.Single(x => x.Id == 1).JournalDate = PeriodEnd.AddDays(-1);
        await db.SaveChangesAsync();

        var result = await new PayrollReconciliationService(db, new User())
            .GetAsync(2026, 10, 7, 1, 10);

        Assert.Equal(200, result.StatusCode);
        var row = Assert.Single(result.Data!.Items);
        Assert.Equal("NeedsReview", row.AccrualStatus);
        Assert.Equal("NeedsReview", row.SettlementStatus);
    }

    [Fact]
    public async Task Invalid_pending_method_does_not_look_like_safe_verification()
    {
        await using var db = Database();
        Seed(db);
        var payment = db.PayrollPayments.Local.Single();
        payment.State = PaymentState.AwaitingVerification;
        payment.JournalId = null;
        payment.PaymentMethod = PaymentMethodType.Card;
        await db.SaveChangesAsync();

        var result = await new PayrollReconciliationService(db, new User())
            .GetAsync(2026, 10, 7, 1, 10);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("NeedsReview", Assert.Single(result.Data!.Items).SettlementStatus);
    }

    private static void Seed(EduOSDbContext db)
    {
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = 1, TenantId = 41, PublicId = Guid.NewGuid(), RunNumber = "PR-2026-10",
            Year = 2026, Month = 10, PeriodStart = new DateOnly(2026, 10, 1),
            PeriodEnd = PeriodEnd, CampusId = 7, State = PayrollRunState.Posted, JournalId = 1
        });
        db.PayrollEmployees.Add(new PayrollEmployee
        {
            Id = 1, TenantId = 41, PayrollRunId = 1, EmployeeId = 101,
            EmployeeCodeSnapshot = "E101", EmployeeNameSnapshot = "Test Employee",
            GrossAmount = 100m, NetAmount = 100m
        });
        db.PayrollPayments.Add(new PayrollPayment
        {
            Id = 1, TenantId = 41, PayrollEmployeeId = 1, PublicId = Guid.NewGuid(),
            ClientRequestId = Guid.NewGuid(), Amount = 40m, CurrencyCode = "BDT",
            State = PaymentState.Successful, JournalId = 2, PaymentDate = PaymentDate,
            PaymentMethod = PaymentMethodType.Cash
        });
        db.Journals.AddRange(
            new Journal { Id = 1, TenantId = 41, JournalNumber = "ACC-1",
                AccountingPeriodId = 1, SourceType = "PayrollRun", SourceId = 1,
                State = JournalState.Posted, CurrencyCode = "BDT", CampusId = 7,
                JournalDate = PeriodEnd },
            new Journal { Id = 2, TenantId = 41, JournalNumber = "PAY-1",
                AccountingPeriodId = 1, SourceType = "PayrollPayment", SourceId = 1,
                State = JournalState.Posted, CurrencyCode = "BDT", CampusId = 7,
                JournalDate = PaymentDate });
        db.JournalLines.AddRange(
            new JournalLine { Id = 1, TenantId = 41, JournalId = 1, LineNo = 1,
                AccountId = 1, CampusId = 7, CreditAmount = 100m,
                Description = "Payroll net payable" },
            new JournalLine { Id = 2, TenantId = 41, JournalId = 1, LineNo = 2,
                AccountId = 3, CampusId = 7, DebitAmount = 100m,
                Description = "Component 1 earning expense" },
            new JournalLine { Id = 3, TenantId = 41, JournalId = 2, LineNo = 1,
                AccountId = 1, CampusId = 7, EmployeeId = 101, DebitAmount = 40m,
                Description = "Salary payable settlement" },
            new JournalLine { Id = 4, TenantId = 41, JournalId = 2, LineNo = 2,
                AccountId = 2, CampusId = 7, EmployeeId = 101, CreditAmount = 40m,
                Description = "Cash/bank payroll disbursement" });
    }

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("payroll-reconciliation-evidence-" + Guid.NewGuid().ToString("N"))
            .Options, accessor);
    }

    private sealed class User : ICurrentUserService
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
