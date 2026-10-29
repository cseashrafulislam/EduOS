using EduOS.Core.DTOs.Payroll;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.HR;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PayrollLoanRecoverySettlementTests
{
    [Fact]
    public async Task Posted_payroll_reconciles_loan_asset_and_recovery_once()
    {
        await using var db = Seed();
        var run = await db.PayrollRuns.SingleAsync();
        var request = Request(run);
        var service = new PayrollJournalPostingService(db, new TestUser(), TimeProvider.System);
        var posted = await service.PostAsync(run.PublicId, request);
        Assert.True(posted.StatusCode == 200, posted.Message);
        var accrual = await db.Journals.SingleAsync(x => x.SourceType == "PayrollRun");
        var lines = await db.JournalLines.Where(x => x.JournalId == accrual.Id).ToListAsync();
        lines.Sum(x => x.DebitAmount).Should().Be(100m);
        lines.Sum(x => x.CreditAmount).Should().Be(100m);
        lines.Should().ContainSingle(x => x.AccountId == 3 && x.EmployeeId == 7 &&
            x.Description == "Payroll loan recovery 1" && x.CreditAmount == 25m);
        var recovery = await db.LoanAdvanceRecoveries.SingleAsync();
        recovery.PayrollEmployeeId.Should().Be(1);
        recovery.JournalId.Should().Be(accrual.Id);
        recovery.Amount.Should().Be(25m);
        (await db.LoanAdvances.SingleAsync()).OutstandingAmount.Should().Be(75m);
        var replay = await service.PostAsync(run.PublicId, request);
        replay.StatusCode.Should().Be(200);
        (await db.LoanAdvanceRecoveries.CountAsync()).Should().Be(1);
        (await db.Journals.CountAsync(x => x.SourceType == "PayrollRun")).Should().Be(1);
    }

    [Fact]
    public async Task Cash_recovery_after_posted_payroll_withholding_reconciles_both_sources()
    {
        await using var db = Seed();
        var run = await db.PayrollRuns.SingleAsync();
        var posting = await new PayrollJournalPostingService(db, new TestUser(), TimeProvider.System)
            .PostAsync(run.PublicId, Request(run));
        Assert.True(posting.StatusCode == 200, posting.Message);
        var loan = await db.LoanAdvances.SingleAsync();
        var cashService = new LoanRecoveryService(db, new TestUser(), TimeProvider.System);
        var history = await cashService.GetHistoryAsync(loan.Id);
        history.StatusCode.Should().Be(200);
        var received = await cashService.PostAsync(loan.Id, new PostLoanRecoveryRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RecoveryDate = new DateOnly(2026, 10, 21),
            Amount = 10m, AccountingPeriodId = 1, CashAccountId = 4,
            RowVersion = Convert.ToBase64String(loan.RowVersion)
        });
        Assert.True(received.StatusCode == 200, received.Message);
        (await db.LoanAdvances.SingleAsync()).OutstandingAmount.Should().Be(65m);
        (await db.LoanAdvanceRecoveries.CountAsync()).Should().Be(2);
        (await db.Journals.CountAsync(x => x.SourceType == "LoanAdvanceRecovery")).Should().Be(1);
    }

    [Fact]
    public async Task Wrong_receivable_mapping_rejects_without_mutating_loan()
    {
        await using var db = Seed();
        var run = await db.PayrollRuns.SingleAsync();
        var request = Request(run);
        request.Mappings = new[]
        {
            new PayrollComponentAccountMappingDto { SalaryComponentId = 1, AccountId = 1 },
            new PayrollComponentAccountMappingDto { SalaryComponentId = 2, AccountId = 4 }
        };
        var result = await new PayrollJournalPostingService(db, new TestUser(), TimeProvider.System)
            .PostAsync(run.PublicId, request);
        result.StatusCode.Should().Be(409);
        (await db.LoanAdvances.SingleAsync()).OutstandingAmount.Should().Be(100m);
        (await db.LoanAdvanceRecoveries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Missing_disbursement_rejects_without_recovery()
    {
        await using var db = Seed(withDisbursement: false);
        var run = await db.PayrollRuns.SingleAsync();
        var result = await new PayrollJournalPostingService(db, new TestUser(), TimeProvider.System)
            .PostAsync(run.PublicId, Request(run));
        result.StatusCode.Should().Be(409);
        (await db.LoanAdvanceRecoveries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Changed_loan_after_calculation_rejects_stale_payroll_snapshot()
    {
        await using var db = Seed();
        var run = await db.PayrollRuns.SingleAsync();
        var loan = await db.LoanAdvances.SingleAsync();
        loan.UpdatedAt = run.CalculatedAt!.Value.AddMinutes(1);
        var result = await new PayrollJournalPostingService(db, new TestUser(), TimeProvider.System)
            .PostAsync(run.PublicId, Request(run));
        result.StatusCode.Should().Be(409);
        (await db.LoanAdvanceRecoveries.CountAsync()).Should().Be(0);
    }

    private static PostPayrollJournalRequestDto Request(PayrollRun run) => new()
    {
        AccountingPeriodId = 1, SalaryPayableAccountId = 2,
        RowVersion = Convert.ToBase64String(run.RowVersion),
        Mappings = new[]
        {
            new PayrollComponentAccountMappingDto { SalaryComponentId = 1, AccountId = 1 },
            new PayrollComponentAccountMappingDto { SalaryComponentId = 2, AccountId = 3 }
        }
    };

    private static EduOSDbContext Seed(bool withDisbursement = true)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        var db = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("payroll-loan-settlement-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, accessor);
        var now = DateTime.UtcNow;
        db.Campuses.Add(new Campus { Id = 1, TenantId = 41, Name = "Main", Code = "MAIN" });
        db.Employees.Add(new Employee { Id = 7, TenantId = 41, EmployeeCode = "E-7",
            FullName = "Test Employee", JoiningDate = new DateOnly(2026, 1, 1) });
        db.EmployeeCampusAssignments.Add(new EmployeeCampusAssignment
        { Id = 1, TenantId = 41, EmployeeId = 7, CampusId = 1,
            IsPrimary = true, EffectiveFrom = new DateOnly(2026, 1, 1) });
        db.Accounts.AddRange(
            new Account { Id = 1, TenantId = 41, Code = "SAL-EXP", Name = "Salary Expense",
                AccountType = AccountType.Expense },
            new Account { Id = 2, TenantId = 41, Code = "SAL-PAY", Name = "Salary Payable",
                AccountType = AccountType.Liability },
            new Account { Id = 3, TenantId = 41, Code = "LOAN-AR", Name = "Employee Loan Receivable",
                AccountType = AccountType.Asset },
            new Account { Id = 4, TenantId = 41, Code = "CASH", Name = "Cash in Hand",
                AccountType = AccountType.Asset });
        db.AccountingPeriods.Add(new AccountingPeriod
        { Id = 1, TenantId = 41, Name = "October 2026",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31) });
        db.SalaryComponents.AddRange(
            new SalaryComponent { Id = 1, TenantId = 41, Code = "BASIC", Name = "Basic",
                Type = SalaryComponentType.Earning },
            new SalaryComponent { Id = 2, TenantId = 41, Code = "LOAN_RECOVERY", Name = "Loan Recovery",
                Type = SalaryComponentType.Deduction });
        db.LoanAdvances.Add(new LoanAdvance { Id = 1, TenantId = 41, EmployeeId = 7,
            IssueDate = new DateOnly(2026, 10, 5), PrincipalAmount = 100m,
            OutstandingAmount = 100m, InstallmentAmount = 25m,
            CreatedAt = now.AddDays(-10), RowVersion = new byte[] { 1 } });
        if (withDisbursement)
        {
        db.Journals.Add(new Journal { Id = 1, TenantId = 41, JournalNumber = "JRN-LOAN",
            JournalDate = new DateOnly(2026, 10, 5), AccountingPeriodId = 1,
            CampusId = 1, SourceType = "LoanAdvance", SourceId = 1,
            State = JournalState.Posted, PostedAt = now.AddDays(-5) });
        db.JournalLines.AddRange(
            new JournalLine { Id = 1, TenantId = 41, JournalId = 1, LineNo = 1,
                AccountId = 3, EmployeeId = 7, CampusId = 1, DebitAmount = 100m },
            new JournalLine { Id = 2, TenantId = 41, JournalId = 1, LineNo = 2,
                AccountId = 4, EmployeeId = 7, CampusId = 1, CreditAmount = 100m });
        }
        db.PayrollRuns.Add(new PayrollRun { Id = 1, TenantId = 41, PublicId = Guid.NewGuid(),
            RunNumber = "PR-202610", Year = 2026, Month = 10,
            PeriodStart = new DateOnly(2026, 10, 1), PeriodEnd = new DateOnly(2026, 10, 20),
            CampusId = 1, State = PayrollRunState.Approved,
            ApprovedByUserId = 8, CreatedBy = 7,
            CalculatedAt = now.AddMinutes(1), RowVersion = new byte[] { 1 } });
        db.PayrollEmployees.Add(new PayrollEmployee { Id = 1, TenantId = 41,
            PayrollRunId = 1, EmployeeId = 7, GrossAmount = 100m,
            DeductionAmount = 25m, NetAmount = 75m });
        db.PayrollLines.AddRange(
            new PayrollLine { Id = 1, TenantId = 41, PayrollEmployeeId = 1,
                SalaryComponentId = 1, Amount = 100m, Description = "Fixed" },
            new PayrollLine { Id = 2, TenantId = 41, PayrollEmployeeId = 1,
                SalaryComponentId = 2, Amount = 25m, Description = "Scheduled loan recovery" });
        db.SaveChanges();
        return db;
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
        public bool IsInRole(string value) => value == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
