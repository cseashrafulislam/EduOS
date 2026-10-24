using EduOS.Core.DTOs.Payroll;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.HR;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LoanDisbursementServiceTests
{
    [Fact]
    public async Task Non_administrator_cannot_read_employee_loan_register()
    {
        await using var db = Database();
        var result = await Service(db, "Accountant").GetRegisterAsync(true, 1, 10);
        result.StatusCode.Should().Be(403);
    }
    [Fact]
    public async Task Non_administrator_cannot_post_disbursement()
    {
        await using var db = Database();
        var result = await Service(db, "HR").PostAsync(1, Request());
        result.StatusCode.Should().Be(403);
    }
    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 11)]
    public async Task Register_rejects_invalid_pagination(int page, int size)
    {
        await using var db = Database();
        var result = await Service(db).GetRegisterAsync(true, page, size);
        result.StatusCode.Should().Be(400);
    }
    [Fact]
    public async Task Missing_loan_does_not_create_a_journal()
    {
        await using var db = Database();
        var result = await Service(db).PostAsync(404, Request());
        result.StatusCode.Should().Be(404);
        (await db.Journals.CountAsync()).Should().Be(0);
    }
    [Fact]
    public async Task Cash_disbursement_posts_balanced_journal_and_replays_idempotently()
    {
        await using var db = Database();
        var date = new DateOnly(2026, 10, 20);
        db.Employees.Add(new EduOS.Core.Entities.HR.Employee
        { Id = 7, TenantId = 41, FullName = "Employee", EmployeeCode = "E-7",
            JoiningDate = new DateOnly(2025, 1, 1) });
        db.Campuses.Add(new EduOS.Core.Entities.SaaS.Campus
        { Id = 3, TenantId = 41, Code = "MAIN", Name = "Main" });
        db.EmployeeCampusAssignments.Add(new EduOS.Core.Entities.HR.EmployeeCampusAssignment
        { Id = 1, TenantId = 41, EmployeeId = 7, CampusId = 3,
            EffectiveFrom = new DateOnly(2025, 1, 1) });
        db.LoanAdvances.Add(new EduOS.Core.Entities.Payroll.LoanAdvance
        { Id = 11, TenantId = 41, EmployeeId = 7, IssueDate = date,
            PrincipalAmount = 200m, OutstandingAmount = 200m, InstallmentAmount = 20m,
            CreatedBy = 5, RowVersion = new byte[] { 1 } });
        db.AccountingPeriods.Add(new EduOS.Core.Entities.Accounting.AccountingPeriod
        { Id = 1, TenantId = 41, Name = "October",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31) });
        db.Accounts.AddRange(
            new EduOS.Core.Entities.Accounting.Account
            { Id = 2, TenantId = 41, Code = "LOAN-REC", Name = "Employee Loan Receivable",
                AccountType = EduOS.Core.Enums.Domain.AccountType.Asset },
            new EduOS.Core.Entities.Accounting.Account
            { Id = 3, TenantId = 41, Code = "CASH-HAND", Name = "Cash in Hand",
                AccountType = EduOS.Core.Enums.Domain.AccountType.Asset });
        await db.SaveChangesAsync();
        var loan = await db.LoanAdvances.SingleAsync();
        var request = Request();
        request.RowVersion = Convert.ToBase64String(loan.RowVersion);
        var first = await Service(db).PostAsync(11, request);
        first.StatusCode.Should().Be(200);
        var journal = await db.Journals.SingleAsync(x => x.SourceType == "LoanAdvance");
        journal.CampusId.Should().Be(3);
        var lines = await db.JournalLines.Where(x => x.JournalId == journal.Id).ToListAsync();
        lines.Should().HaveCount(2);
        lines.Sum(x => x.DebitAmount).Should().Be(200m);
        lines.Sum(x => x.CreditAmount).Should().Be(200m);
        lines.Single(x => x.DebitAmount > 0).AccountId.Should().Be(2);
        lines.Single(x => x.CreditAmount > 0).AccountId.Should().Be(3);
        var replay = await Service(db).PostAsync(11, request);
        replay.StatusCode.Should().Be(200);
        (await db.Journals.CountAsync(x => x.SourceType == "LoanAdvance")).Should().Be(1);
    }
    private static PostLoanDisbursementRequestDto Request() => new()
    {
        DisbursementDate = new DateOnly(2026, 10, 20),
        AccountingPeriodId = 1, LoanReceivableAccountId = 2, CashAccountId = 3,
        RowVersion = "AQ=="
    };
    private static LoanDisbursementService Service(EduOSDbContext db, string role = "TenantAdmin") =>
        new(db, new TestUser(role), TimeProvider.System);
    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("loan-disbursement-" + Guid.NewGuid().ToString("N"))
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
