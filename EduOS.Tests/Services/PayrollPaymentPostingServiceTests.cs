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
    [Fact]
    public async Task Cash_settlement_posts_balanced_journal_once_and_replay_is_idempotent()
    {
        await using var db = Database();
        var reference = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 20);
        db.Accounts.AddRange(
            new EduOS.Core.Entities.Accounting.Account { Id = 1, TenantId = 41, Code = "SALARY-PAYABLE",
                Name = "Salary Payable", AccountType = EduOS.Core.Enums.Domain.AccountType.Liability },
            new EduOS.Core.Entities.Accounting.Account { Id = 2, TenantId = 41, Code = "CASH-HAND",
                Name = "Cash in Hand", AccountType = EduOS.Core.Enums.Domain.AccountType.Asset },
            new EduOS.Core.Entities.Accounting.Account { Id = 3, TenantId = 41, Code = "SALARY-EXP",
                Name = "Salary Expense", AccountType = EduOS.Core.Enums.Domain.AccountType.Expense });
        db.AccountingPeriods.Add(new EduOS.Core.Entities.Accounting.AccountingPeriod
        { Id = 1, TenantId = 41, Name = "October", StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 31) });
        db.PayrollRuns.Add(new EduOS.Core.Entities.Payroll.PayrollRun
        { Id = 1, TenantId = 41, PublicId = Guid.NewGuid(), RunNumber = "PAY-2026-10",
            PeriodEnd = new DateOnly(2026, 10, 19), State = EduOS.Core.Enums.Domain.PayrollRunState.Posted,
            JournalId = 1 });
        db.PayrollEmployees.Add(new EduOS.Core.Entities.Payroll.PayrollEmployee
        { Id = 1, TenantId = 41, PayrollRunId = 1, EmployeeId = 7,
            GrossAmount = 100m, DeductionAmount = 0m, NetAmount = 100m });
        db.Journals.Add(new EduOS.Core.Entities.Accounting.Journal
        { Id = 1, TenantId = 41, PublicId = Guid.NewGuid(), JournalNumber = "JRN-ACCRUAL",
            JournalDate = date, AccountingPeriodId = 1, SourceType = "PayrollRun",
            SourceId = 1, State = EduOS.Core.Enums.Domain.JournalState.Posted });
        db.JournalLines.AddRange(
            new EduOS.Core.Entities.Accounting.JournalLine
            { Id = 1, TenantId = 41, JournalId = 1, LineNo = 1, AccountId = 3,
                DebitAmount = 100m, Description = "Salary expense" },
            new EduOS.Core.Entities.Accounting.JournalLine
            { Id = 2, TenantId = 41, JournalId = 1, LineNo = 2, AccountId = 1,
                CreditAmount = 100m, Description = "Payroll net payable" });
        var payment = new EduOS.Core.Entities.Payroll.PayrollPayment
        { Id = 1, TenantId = 41, PublicId = reference, ClientRequestId = Guid.NewGuid(),
            PayrollEmployeeId = 1, PaymentDate = date, PaymentMethod = EduOS.Core.Enums.Domain.PaymentMethodType.Cash,
            Amount = 40m, State = EduOS.Core.Enums.Domain.PaymentState.AwaitingVerification,
            CreatedBy = 5, RowVersion = new byte[] { 1 } };
        db.PayrollPayments.Add(payment);
        await db.SaveChangesAsync();
        var request = new PostPayrollPaymentJournalRequestDto
        { AccountingPeriodId = 1, SettlementAccountId = 2,
            RowVersion = Convert.ToBase64String(payment.RowVersion) };
        var service = new PayrollPaymentPostingService(db, new TestUser("TenantAdmin"), TimeProvider.System);
        var first = await service.PostAsync(reference, request);
        first.StatusCode.Should().Be(200);
        var posted = await db.Journals.SingleAsync(x => x.SourceType == "PayrollPayment");
        var lines = await db.JournalLines.Where(x => x.JournalId == posted.Id).ToListAsync();
        lines.Should().HaveCount(2);
        lines.Sum(x => x.DebitAmount).Should().Be(40m);
        lines.Sum(x => x.CreditAmount).Should().Be(40m);
        lines.Single(x => x.DebitAmount > 0m).AccountId.Should().Be(1);
        lines.Single(x => x.CreditAmount > 0m).AccountId.Should().Be(2);
        (await db.PayrollPayments.SingleAsync(x => x.Id == 1)).State
            .Should().Be(EduOS.Core.Enums.Domain.PaymentState.Successful);
        var replay = await service.PostAsync(reference, request);
        replay.StatusCode.Should().Be(200);
        (await db.Journals.CountAsync(x => x.SourceType == "PayrollPayment")).Should().Be(1);
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
