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

public sealed class PayrollJournalPostingServiceTests
{
    [Fact]
    public async Task Staff_cannot_read_institutional_payroll_gl()
    {
        await using var db = Database();
        var result = await new PayrollJournalPostingService(db, new TestUser("Staff"), TimeProvider.System)
            .GetRegisterAsync(true, 1, 5);
        result.StatusCode.Should().Be(403);
    }
    [Fact]
    public async Task Accountant_cannot_post_payroll_gl()
    {
        await using var db = Database();
        var result = await new PayrollJournalPostingService(db, new TestUser("Accountant"), TimeProvider.System)
            .PostAsync(Guid.NewGuid(), ValidRequest());
        result.StatusCode.Should().Be(403);
    }
    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 0)]
    [InlineData(1, 11)]
    public async Task Register_rejects_invalid_pagination(int page, int size)
    {
        await using var db = Database();
        var result = await new PayrollJournalPostingService(db, new TestUser("TenantAdmin"), TimeProvider.System)
            .GetRegisterAsync(true, page, size);
        result.StatusCode.Should().Be(400);
    }
    [Fact]
    public async Task Posting_requires_unique_component_mapping()
    {
        await using var db = Database();
        var request = ValidRequest();
        request.Mappings = new[]
        {
            new PayrollComponentAccountMappingDto { SalaryComponentId = 1, AccountId = 3 },
            new PayrollComponentAccountMappingDto { SalaryComponentId = 1, AccountId = 4 }
        };
        var result = await new PayrollJournalPostingService(db, new TestUser("TenantAdmin"), TimeProvider.System)
            .PostAsync(Guid.NewGuid(), request);
        result.StatusCode.Should().Be(400);
    }
    [Fact]
    public async Task Posting_rejects_missing_run_without_creating_journal()
    {
        await using var db = Database();
        var result = await new PayrollJournalPostingService(db, new TestUser("TenantAdmin"), TimeProvider.System)
            .PostAsync(Guid.NewGuid(), ValidRequest());
        result.StatusCode.Should().Be(404);
        (await db.Journals.CountAsync()).Should().Be(0);
    }
    [Theory]
    [InlineData(1, 15, 1, 15, true, 41, false, 200)]
    [InlineData(16, 31, 16, 31, true, 41, false, 200)]
    [InlineData(1, 15, 1, 14, true, 41, false, 409)]
    [InlineData(1, 15, 1, 15, false, 41, false, 409)]
    [InlineData(1, 15, 1, 15, true, 42, false, 409)]
    [InlineData(1, 15, 1, 15, true, 41, true, 409)]
    public async Task Posting_checks_primary_campus_for_actual_calendar_day_service_period(
        int joinDay, int leaveDay, int assignmentStart, int assignmentEnd,
        bool primary, long assignmentTenant, bool conflictingPrimary, int expectedStatus)
    {
        await using var db = Database();
        var runReference = Guid.NewGuid();
        var start = new DateOnly(2026, 10, 1);
        var end = new DateOnly(2026, 10, 31);
        db.Campuses.Add(new Campus { Id = 5, TenantId = 41, Code = "MAIN", Name = "Main" });
        db.Employees.Add(new Employee
        {
            Id = 10, TenantId = 41, PersonId = 101, DesignationId = 1,
            EmployeeCode = "EMP-10", FullName = "Staff Member",
            JoiningDate = new DateOnly(2026, 10, joinDay),
            LeavingDate = leaveDay == 31 ? null : new DateOnly(2026, 10, leaveDay)
        });
        db.EmployeeCampusAssignments.Add(new EmployeeCampusAssignment
        {
            Id = 1, TenantId = assignmentTenant, EmployeeId = 10, CampusId = 5,
            IsPrimary = primary, EffectiveFrom = new DateOnly(2026, 10, assignmentStart),
            EffectiveTo = new DateOnly(2026, 10, assignmentEnd)
        });
        if (conflictingPrimary)
            db.EmployeeCampusAssignments.Add(new EmployeeCampusAssignment
            {
                Id = 2, TenantId = 41, EmployeeId = 10, CampusId = 6,
                IsPrimary = true, EffectiveFrom = start, EffectiveTo = new DateOnly(2026, 10, 10)
            });
        db.AccountingPeriods.Add(new AccountingPeriod
        { Id = 1, TenantId = 41, Name = "October", StartDate = start, EndDate = end });
        db.Accounts.AddRange(
            new Account { Id = 2, TenantId = 41, Code = "PAY", Name = "Salary Payable",
                AccountType = AccountType.Liability },
            new Account { Id = 3, TenantId = 41, Code = "EXP", Name = "Salary Expense",
                AccountType = AccountType.Expense });
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = 1, TenantId = 41, PublicId = runReference, ClientRequestId = Guid.NewGuid(),
            RunNumber = "PAY-2026-10", CampusId = 5, Year = 2026, Month = 10,
            PeriodStart = start, PeriodEnd = end, ProrationModeCode = "CalendarDays",
            State = PayrollRunState.Approved, CreatedBy = 8, ApprovedByUserId = 9,
            RowVersion = [1]
        });
        db.PayrollEmployees.Add(new PayrollEmployee
        {
            Id = 1, TenantId = 41, PayrollRunId = 1, EmployeeId = 10,
            GrossAmount = 15000m, NetAmount = 15000m
        });
        db.SalaryComponents.Add(new SalaryComponent
        { Id = 1, TenantId = 41, Code = "BASIC", Name = "Basic", Type = SalaryComponentType.Earning });
        db.PayrollLines.Add(new PayrollLine
        { Id = 1, TenantId = 41, PayrollEmployeeId = 1, SalaryComponentId = 1, Amount = 15000m });
        await db.SaveChangesAsync();

        var result = await new PayrollJournalPostingService(db, new TestUser("TenantAdmin"), TimeProvider.System)
            .PostAsync(runReference, ValidRequest());
        result.StatusCode.Should().Be(expectedStatus);
        (await db.Journals.CountAsync()).Should().Be(expectedStatus == 200 ? 1 : 0);
        if (expectedStatus == 200)
        {
            (await db.JournalLines.SumAsync(x => x.DebitAmount)).Should().Be(15000m);
            (await db.JournalLines.SumAsync(x => x.CreditAmount)).Should().Be(15000m);
            (await db.PayrollRuns.SingleAsync()).State.Should().Be(PayrollRunState.Posted);
        }
    }
    private static PostPayrollJournalRequestDto ValidRequest() => new()
    {
        AccountingPeriodId = 1, SalaryPayableAccountId = 2, RowVersion = "AQ==",
        Mappings = new[] { new PayrollComponentAccountMappingDto { SalaryComponentId = 1, AccountId = 3 } }
    };
    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("payroll-gl-" + Guid.NewGuid().ToString("N"))
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
