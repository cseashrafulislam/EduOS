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

public sealed class InstitutionalAdditionalAccountingReportsTests
{
    private static readonly DateOnly September = new(2026, 9, 15);
    private static readonly DateOnly October = new(2026, 10, 15);

    [Fact]
    public async Task Day_book_pages_complete_posted_journals_and_keeps_full_period_totals()
    {
        await using var db = Database();
        SeedAccounts(db);
        AddJournal(db, 1, September, 100m, 1, 2);
        AddJournal(db, 2, October, 200m, 1, 2);
        AddJournal(db, 3, October, 50m, 2, 1, JournalState.Reversed);
        AddJournal(db, 4, October, 50m, 1, 2, JournalState.Posted, reversalOf:3);
        AddJournal(db, 5, October, 999m, 1, 2, JournalState.Draft);
        await db.SaveChangesAsync();
        var service = Service(db);
        var first = await service.GetPostedDayBookAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 1, 2);
        var second = await service.GetPostedDayBookAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 2, 2);
        first.StatusCode.Should().Be(200);
        second.StatusCode.Should().Be(200);
        first.Data!.Journals.TotalCount.Should().Be(3);
        first.Data.Journals.Items.Should().HaveCount(2);
        second.Data!.Journals.Items.Should().ContainSingle();
        first.Data.TotalDebit.Should().Be(300m);
        first.Data.TotalCredit.Should().Be(300m);
        second.Data.TotalDebit.Should().Be(300m);
        second.Data.Journals.Items[0].IsReversal.Should().BeTrue();
        first.Data.Journals.Items[1].IsReversedOriginal.Should().BeTrue();
        first.Data.Journals.Items.Should().OnlyContain(x => x.LineCount == 2 && x.DebitAmount == x.CreditAmount);
    }

    [Fact]
    public async Task Day_book_rejects_unbalanced_deleted_and_cross_tenant_history()
    {
        await using var db = Database();
        SeedAccounts(db);
        AddJournal(db, 1, October, 100m, 1, 2);
        await db.SaveChangesAsync();
        var service = Service(db);
        var from = new DateOnly(2026, 10, 1);
        var to = new DateOnly(2026, 10, 31);
        (await service.GetPostedDayBookAsync(from, to, 0, 25)).StatusCode.Should().Be(400);
        (await service.GetPostedDayBookAsync(from, to, 1, 101)).StatusCode.Should().Be(400);
        (await Service(db, false).GetPostedDayBookAsync(from, to)).StatusCode.Should().Be(403);
        (await service.GetPostedDayBookAsync(from, to)).StatusCode.Should().Be(200);
        var credit = await db.JournalLines.SingleAsync(x => x.JournalId == 1 && x.AccountId == 2);
        credit.CreditAmount = 99m;
        await db.SaveChangesAsync();
        (await service.GetPostedDayBookAsync(from, to)).StatusCode.Should().Be(409);
        credit.CreditAmount = 100m;
        credit.IsDeleted = true;
        await db.SaveChangesAsync();
        (await service.GetPostedDayBookAsync(from, to)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Comparative_income_includes_prior_only_accounts_with_signed_variances()
    {
        await using var db = Database();
        SeedAccounts(db);
        AddJournal(db, 1, September, 60m, 3, 1);
        AddJournal(db, 2, October, 100m, 1, 2);
        await db.SaveChangesAsync();
        var result = await Service(db).GetComparativeIncomeStatementAsync(
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31),
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        result.StatusCode.Should().Be(200);
        result.Data!.CurrentIncome.Should().Be(100m);
        result.Data.ComparisonIncome.Should().Be(0m);
        result.Data.CurrentExpenses.Should().Be(0m);
        result.Data.ComparisonExpenses.Should().Be(60m);
        result.Data.CurrentNetSurplus.Should().Be(100m);
        result.Data.ComparisonNetSurplus.Should().Be(-60m);
        result.Data.Accounts.Should().Contain(x => x.AccountCode == "EXP" &&
            x.CurrentAmount == 0m && x.ComparisonAmount == 60m && x.Variance == -60m);
        result.Data.Accounts.Should().Contain(x => x.AccountCode == "INC" &&
            x.CurrentAmount == 100m && x.ComparisonAmount == 0m && x.Variance == 100m);
    }

    [Fact]
    public async Task Comparisons_fail_closed_for_unauthorized_or_invalid_periods()
    {
        await using var db = Database();
        var admin = Service(db);
        var accountant = Service(db, false);
        (await accountant.GetComparativeIncomeStatementAsync(September, October, September, October))
            .StatusCode.Should().Be(403);
        (await accountant.GetComparativeBalanceSheetAsync(October, September))
            .StatusCode.Should().Be(403);
        (await admin.GetComparativeIncomeStatementAsync(October, September, September, October))
            .StatusCode.Should().Be(400);
        (await admin.GetComparativeBalanceSheetAsync(default, September))
            .StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Comparative_balance_is_lifetime_position_and_reconciles_unclosed_surplus()
    {
        await using var db = Database();
        SeedAccounts(db);
        AddJournal(db, 1, September, 60m, 3, 1);
        AddJournal(db, 2, October, 100m, 1, 2);
        await db.SaveChangesAsync();
        var result = await Service(db).GetComparativeBalanceSheetAsync(
            new DateOnly(2026, 10, 31), new DateOnly(2026, 9, 30));
        result.StatusCode.Should().Be(200);
        result.Data!.CurrentAssets.Should().Be(40m);
        result.Data.ComparisonAssets.Should().Be(-60m);
        result.Data.CurrentUnclosedSurplus.Should().Be(40m);
        result.Data.ComparisonUnclosedSurplus.Should().Be(-60m);
        result.Data.Accounts.Should().Contain(x => x.AccountCode == "CASH" &&
            x.CurrentAmount == 40m && x.ComparisonAmount == -60m && x.Variance == 100m);
    }

    private static void SeedAccounts(EduOSDbContext db) => db.Accounts.AddRange(
        new Account { Id = 1, TenantId = 41, Code = "CASH", Name = "Cash", AccountType = AccountType.Asset },
        new Account { Id = 2, TenantId = 41, Code = "INC", Name = "Income", AccountType = AccountType.Income },
        new Account { Id = 3, TenantId = 41, Code = "EXP", Name = "Expense", AccountType = AccountType.Expense });

    private static void AddJournal(EduOSDbContext db, long id, DateOnly date, decimal amount,
        long debitId, long creditId, JournalState state = JournalState.Posted, long? reversalOf = null)
    {
        db.Journals.Add(new Journal { Id = id, TenantId = 41, PublicId = Guid.NewGuid(),
            JournalNumber = "JV-" + id, JournalDate = date, AccountingPeriodId = 1,
            State = state, CurrencyCode = "BDT", SourceType = "Manual", ReversalOfJournalId = reversalOf });
        db.JournalLines.AddRange(
            new JournalLine { Id = id * 10 + 1, TenantId = 41, JournalId = id, LineNo = 1,
                AccountId = debitId, DebitAmount = amount },
            new JournalLine { Id = id * 10 + 2, TenantId = 41, JournalId = id, LineNo = 2,
                AccountId = creditId, CreditAmount = amount });
    }

    private static AccountingReportingService Service(EduOSDbContext db, bool admin = true) =>
        new(db, new TestUser(admin));

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("additional-finance-" + Guid.NewGuid().ToString("N")).Options, accessor);
    }

    private sealed class TestUser(bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Accountant"];
        public bool IsInRole(string value) => admin ? value == "TenantAdmin" : value == "Accountant";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
