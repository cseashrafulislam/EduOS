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

public sealed class LoanRecoveryServiceTests
{
    [Fact]
    public async Task Non_administrator_cannot_read_recovery_register()
    {
        await using var db = Database();
        (await Service(db, "Accountant").GetRegisterAsync(true, 1, 10)).StatusCode.Should().Be(403);
    }
    [Fact]
    public async Task Non_administrator_cannot_post_recovery()
    {
        await using var db = Database();
        (await Service(db, "HR").PostAsync(11, Request())).StatusCode.Should().Be(403);
    }
    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 11)]
    public async Task Register_rejects_invalid_pagination(int page, int size)
    {
        await using var db = Database();
        (await Service(db).GetRegisterAsync(true, page, size)).StatusCode.Should().Be(400);
    }
    [Fact]
    public async Task Missing_loan_cannot_create_recovery_or_journal()
    {
        await using var db = Database();
        (await Service(db).PostAsync(404, Request())).StatusCode.Should().Be(404);
        (await db.LoanAdvanceRecoveries.CountAsync()).Should().Be(0);
        (await db.Journals.CountAsync()).Should().Be(0);
    }
    [Fact]
    public async Task Cash_recovery_posts_balanced_journal_and_replays_without_double_reducing_balance()
    {
        await using var db = Database();
        await SeedDisbursedLoan(db);
        var request = Request();
        request.RowVersion = Convert.ToBase64String((await db.LoanAdvances.SingleAsync()).RowVersion);
        var first = await Service(db).PostAsync(11, request);
        first.StatusCode.Should().Be(200);
        var recovery = await db.LoanAdvanceRecoveries.SingleAsync();
        recovery.JournalId.Should().NotBeNull();
        recovery.ClientRequestId.Should().Be(request.ClientRequestId);
        var journal = await db.Journals.SingleAsync(x => x.SourceType == "LoanAdvanceRecovery");
        journal.State.Should().Be(JournalState.Posted);
        journal.SourceId.Should().Be(recovery.Id);
        journal.CampusId.Should().Be(3);
        var lines = await db.JournalLines.Where(x => x.JournalId == journal.Id).ToListAsync();
        lines.Should().HaveCount(2);
        lines.Sum(x => x.DebitAmount).Should().Be(20m);
        lines.Sum(x => x.CreditAmount).Should().Be(20m);
        lines.Single(x => x.DebitAmount > 0m).AccountId.Should().Be(3);
        lines.Single(x => x.CreditAmount > 0m).AccountId.Should().Be(2);
        (await db.LoanAdvances.SingleAsync()).OutstandingAmount.Should().Be(180m);
        var replay = await Service(db).PostAsync(11, request);
        replay.StatusCode.Should().Be(200);
        (await db.LoanAdvanceRecoveries.CountAsync()).Should().Be(1);
        (await db.Journals.CountAsync(x => x.SourceType == "LoanAdvanceRecovery")).Should().Be(1);
        (await db.LoanAdvances.SingleAsync()).OutstandingAmount.Should().Be(180m);
    }
    [Fact]
    public async Task Recovery_cannot_exceed_outstanding_balance()
    {
        await using var db = Database();
        await SeedDisbursedLoan(db);
        var request = Request(); request.Amount = 201m;
        request.RowVersion = Convert.ToBase64String((await db.LoanAdvances.SingleAsync()).RowVersion);
        (await Service(db).PostAsync(11, request)).StatusCode.Should().Be(409);
        (await db.LoanAdvanceRecoveries.CountAsync()).Should().Be(0);
    }
    [Fact]
    public async Task Invalid_client_request_cannot_create_recovery()
    {
        await using var db = Database();
        await SeedDisbursedLoan(db);
        var request = Request(); request.ClientRequestId = Guid.Empty;
        (await Service(db).PostAsync(11, request)).StatusCode.Should().Be(400);
        (await db.LoanAdvanceRecoveries.CountAsync()).Should().Be(0);
    }
    [Fact]
    public async Task Payroll_loan_withholding_blocks_unallocated_cash_recovery()
    {
        await using var db = Database();
        await SeedDisbursedLoan(db);
        db.SalaryComponents.Add(new SalaryComponent
        { Id = 31, TenantId = 41, Code = "LOAN_RECOVERY", Name = "Loan recovery",
            Type = SalaryComponentType.Deduction });
        db.PayrollRuns.Add(new PayrollRun
        { Id = 41, TenantId = 41, Year = 2026, Month = 10,
            PeriodStart = new DateOnly(2026, 10, 1), PeriodEnd = new DateOnly(2026, 10, 31),
            RunNumber = "PR-TEST", State = PayrollRunState.Calculated });
        db.PayrollEmployees.Add(new PayrollEmployee
        { Id = 51, TenantId = 41, EmployeeId = 7, PayrollRunId = 41 });
        db.PayrollLines.Add(new PayrollLine
        { Id = 61, TenantId = 41, PayrollEmployeeId = 51, SalaryComponentId = 31, Amount = 20m });
        await db.SaveChangesAsync();
        var request = Request();
        request.RowVersion = Convert.ToBase64String((await db.LoanAdvances.SingleAsync()).RowVersion);
        (await Service(db).PostAsync(11, request)).StatusCode.Should().Be(409);
        (await db.LoanAdvanceRecoveries.CountAsync()).Should().Be(0);
    }
    [Fact]
    public async Task History_lists_only_posted_recoveries_for_tenant()
    {
        await using var db = Database();
        await SeedDisbursedLoan(db);
        var request = Request();
        request.RowVersion = Convert.ToBase64String((await db.LoanAdvances.SingleAsync()).RowVersion);
        (await Service(db).PostAsync(11, request)).StatusCode.Should().Be(200);
        var result = await Service(db).GetHistoryAsync(11);
        result.StatusCode.Should().Be(200);
        result.Data.Should().ContainSingle();
    }
    private static PostLoanRecoveryRequestDto Request() => new()
    {
        ClientRequestId = Guid.Parse("b93c4167-6c0a-48a6-948d-317890fc8d9d"),
        RecoveryDate = new DateOnly(2026, 10, 21),
        AccountingPeriodId = 1, CashAccountId = 3, Amount = 20m,
        RowVersion = "AQ=="
    };
    private static async Task SeedDisbursedLoan(EduOSDbContext db)
    {
        db.Employees.Add(new Employee
        { Id = 7, TenantId = 41, FullName = "Employee", EmployeeCode = "E-7",
            JoiningDate = new DateOnly(2025, 1, 1) });
        db.Campuses.Add(new Campus { Id = 3, TenantId = 41, Code = "MAIN", Name = "Main" });
        db.EmployeeCampusAssignments.Add(new EmployeeCampusAssignment
        { Id = 1, TenantId = 41, EmployeeId = 7, CampusId = 3,
            EffectiveFrom = new DateOnly(2025, 1, 1) });
        db.LoanAdvances.Add(new LoanAdvance
        { Id = 11, TenantId = 41, EmployeeId = 7, IssueDate = new DateOnly(2026, 10, 20),
            PrincipalAmount = 200m, OutstandingAmount = 200m, InstallmentAmount = 20m,
            CreatedBy = 5, RowVersion = new byte[] { 1 } });
        db.AccountingPeriods.Add(new AccountingPeriod
        { Id = 1, TenantId = 41, Name = "October",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31) });
        db.Accounts.AddRange(
            new Account { Id = 2, TenantId = 41, Code = "LOAN-REC", Name = "Loan Receivable",
                AccountType = AccountType.Asset },
            new Account { Id = 3, TenantId = 41, Code = "CASH-HAND", Name = "Cash in Hand",
                AccountType = AccountType.Asset });
        await db.SaveChangesAsync();
        var loan = await db.LoanAdvances.SingleAsync();
        var response = await new LoanDisbursementService(db, new TestUser("TenantAdmin"),
            TimeProvider.System).PostAsync(11, new PostLoanDisbursementRequestDto
            {
                DisbursementDate = new DateOnly(2026, 10, 20),
                AccountingPeriodId = 1, LoanReceivableAccountId = 2, CashAccountId = 3,
                RowVersion = Convert.ToBase64String(loan.RowVersion)
            });
        response.StatusCode.Should().Be(200);
    }
    private static LoanRecoveryService Service(EduOSDbContext db, string role = "TenantAdmin") =>
        new(db, new TestUser(role), TimeProvider.System);
    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("loan-recovery-" + Guid.NewGuid().ToString("N"))
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
