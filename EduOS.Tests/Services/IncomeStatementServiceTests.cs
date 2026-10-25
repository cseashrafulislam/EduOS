using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Accounting;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class IncomeStatementServiceTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);

    [Theory]
    [InlineData("Accountant")]
    [InlineData("Principal")]
    [InlineData("Staff")]
    public async Task Tenant_wide_income_statement_requires_tenant_admin(string role)
    {
        await using var db = Database();
        (await Service(db, role).GetIncomeStatementAsync(From, To)).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Invalid_period_is_rejected()
    {
        await using var db = Database();
        (await Service(db).GetIncomeStatementAsync(To, From)).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Posted_revenue_expenses_and_compensating_reversal_reconcile()
    {
        await using var db = Database();
        SeedAccounts(db);
        Journal(db, 1, JournalState.Posted, 1000m, 400m);
        Journal(db, 2, JournalState.Posted, 200m, 0m, reversed: true);
        Journal(db, 3, JournalState.Draft, 999m, 999m);
        await db.SaveChangesAsync();
        var result = await Service(db).GetIncomeStatementAsync(From, To);
        result.StatusCode.Should().Be(200);
        result.Data!.TotalIncome.Should().Be(800m);
        result.Data.TotalExpenses.Should().Be(400m);
        result.Data.NetSurplus.Should().Be(400m);
        result.Data.IncomeRows.Should().ContainSingle(x => x.AccountCode == "FEE-INCOME" && x.Amount == 800m);
        result.Data.ExpenseRows.Should().ContainSingle(x => x.AccountCode == "SALARY-EXPENSE" && x.Amount == 400m);
    }

    [Fact]
    public async Task Unbalanced_posted_journal_is_not_reported_as_profit()
    {
        await using var db = Database();
        SeedAccounts(db);
        Journal(db, 1, JournalState.Posted, 100m, 0m);
        db.JournalLines.Local.Single(x => x.JournalId == 1 && x.AccountId == 3).CreditAmount = 99m;
        await db.SaveChangesAsync();
        (await Service(db).GetIncomeStatementAsync(From, To)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Missing_posting_account_is_rejected()
    {
        await using var db = Database();
        SeedAccounts(db);
        Journal(db, 1, JournalState.Posted, 100m, 0m);
        db.JournalLines.Local.Single(x => x.JournalId == 1 && x.AccountId == 3).AccountId = 404;
        await db.SaveChangesAsync();
        (await Service(db).GetIncomeStatementAsync(From, To)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Mixed_currency_journal_is_rejected()
    {
        await using var db = Database();
        SeedAccounts(db);
        Journal(db, 1, JournalState.Posted, 100m, 0m);
        db.Journals.Local.Single().CurrencyCode = "USD";
        await db.SaveChangesAsync();
        (await Service(db).GetIncomeStatementAsync(From, To)).StatusCode.Should().Be(409);
    }

    private static void SeedAccounts(EduOSDbContext db)
    {
        db.Accounts.AddRange(
            new Account { Id=1, TenantId=41, Code="FEE-INCOME", Name="Fee income", AccountType=AccountType.Income },
            new Account { Id=2, TenantId=41, Code="SALARY-EXPENSE", Name="Salary expense", AccountType=AccountType.Expense },
            new Account { Id=3, TenantId=41, Code="CASH", Name="Cash", AccountType=AccountType.Asset });
    }
    private static void Journal(EduOSDbContext db, long id, JournalState state, decimal revenue, decimal expense, bool reversed=false)
    {
        db.Journals.Add(new Journal { Id=id, TenantId=41, JournalNumber="J-"+id,
            JournalDate=From, AccountingPeriodId=1, State=state });
        var sign = reversed ? -1m : 1m;
        db.JournalLines.AddRange(
            new JournalLine { Id=id*10+1, TenantId=41, JournalId=id, AccountId=1, LineNo=1,
                CreditAmount=sign>0?revenue:0m, DebitAmount=sign<0?revenue:0m },
            new JournalLine { Id=id*10+2, TenantId=41, JournalId=id, AccountId=2, LineNo=2,
                DebitAmount=sign>0?expense:0m, CreditAmount=sign<0?expense:0m },
            new JournalLine { Id=id*10+3, TenantId=41, JournalId=id, AccountId=3, LineNo=3,
                DebitAmount=sign>0?revenue-expense:0m, CreditAmount=sign<0?revenue-expense:0m });
    }
    private static AccountingReportingService Service(EduOSDbContext db, string role="TenantAdmin") =>
        new(db, new TestUser(role));
    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("income-statement-" + Guid.NewGuid().ToString("N")).Options, accessor);
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
