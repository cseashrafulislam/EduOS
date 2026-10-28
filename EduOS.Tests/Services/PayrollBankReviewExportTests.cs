using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.HR;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PayrollBankReviewExportTests
{
    [Fact]
    public async Task Bank_csv_denies_non_admin_without_exposing_account_number()
    {
        await using var db = Database();
        var result = await new PayrollPaymentPostingService(db, new User("Accountant"),
            TimeProvider.System, new EphemeralDataProtectionProvider())
            .GetBankReviewCsvAsync(Guid.NewGuid());
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Bank_csv_is_review_only_escaped_audited_and_does_not_settle_payment()
    {
        await using var db = Database();
        var provider = new EphemeralDataProtectionProvider();
        var runId = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 20);
        db.Accounts.Add(new Account { Id = 1, TenantId = 41, Code = "SALARY-PAYABLE",
            Name = "Salary Payable", AccountType = AccountType.Liability });
        db.PayrollRuns.Add(new PayrollRun { Id = 1, TenantId = 41, PublicId = runId,
            RunNumber = "PR-202610", PeriodEnd = date, State = PayrollRunState.Posted, JournalId = 1 });
        db.Employees.Add(new Employee { Id = 1, TenantId = 41, PublicId = Guid.NewGuid(),
            EmployeeCode = "=BAD", FullName = "+PAYEE", DesignationId = 1 });
        db.EmployeeBankAccounts.Add(new EmployeeBankAccount { Id = 1, TenantId = 41,
            EmployeeId = 1, BankName = "Destination Bank", AccountName = "=PAYEE",
            ProtectedAccountNumber = provider.CreateProtector("EduOS.HR.EmployeeBankAccount.v1", "41")
                .Protect("1234567890"), AccountNumberLast4 = "7890", CurrencyCode = "BDT" });
        db.BankAccounts.Add(new BankAccount { Id = 1, TenantId = 41, AccountId = 2,
            BankName = "Source Bank", AccountName = "Institution", AccountNumber = "123", CurrencyCode = "BDT" });
        db.PayrollEmployees.Add(new PayrollEmployee { Id = 1, TenantId = 41, PayrollRunId = 1,
            EmployeeId = 1, NetAmount = 100m, GrossAmount = 100m });
        db.Journals.Add(new Journal { Id = 1, TenantId = 41, JournalDate = date,
            SourceType = "PayrollRun", SourceId = 1, CurrencyCode = "BDT", State = JournalState.Posted });
        db.JournalLines.AddRange(
            new JournalLine { Id = 1, TenantId = 41, JournalId = 1, LineNo = 1,
                AccountId = 3, DebitAmount = 100m, Description = "Salary expense" },
            new JournalLine { Id = 2, TenantId = 41, JournalId = 1, LineNo = 2,
                AccountId = 1, CreditAmount = 100m, Description = "Payroll net payable" });
        db.PayrollPayments.Add(new PayrollPayment { Id = 1, TenantId = 41,
            PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(), PayrollEmployeeId = 1,
            Amount = 100m, PaymentDate = date, PaymentMethod = PaymentMethodType.BankTransfer,
            BankAccountId = 1, EmployeeBankAccountId = 1, ExternalReference = "MONEY-REVIEW",
            State = PaymentState.AwaitingVerification });
        await db.SaveChangesAsync();
        var result = await new PayrollPaymentPostingService(db, new User("TenantAdmin"),
            TimeProvider.System, provider).GetBankReviewCsvAsync(runId);
        result.StatusCode.Should().Be(200);
        result.Data!.Csv.Should().Contain("REVIEW_ONLY").And.Contain("1234567890")
            .And.Contain("\"'=BAD\"").And.Contain("\"'+PAYEE\"")
            .And.Contain("\"'=PAYEE\"");
        (await db.PayrollPayments.SingleAsync()).State.Should().Be(PaymentState.AwaitingVerification);
        var audit = await db.AuditLogs.SingleAsync(x => x.Action == "Payroll.BankReviewExported");
        audit.NewValue.Should().NotContain("1234567890");
    }

    private static EduOSDbContext Database()
    {
        var context = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        context.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("bank-review-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, context);
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
        public bool IsInRole(string name) => name == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
