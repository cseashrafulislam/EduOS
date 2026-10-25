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

public sealed class BalanceSheetServiceTests
{
    private static readonly DateOnly AsOf = new(2026, 10, 25);

    [Theory]
    [InlineData("Accountant")]
    [InlineData("Principal")]
    [InlineData("Staff")]
    public async Task Tenant_wide_balance_sheet_requires_tenant_admin(string role)
    {
        await using var db = Database();
        (await Service(db, role).GetBalanceSheetAsync(AsOf)).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Missing_as_of_date_is_rejected()
    {
        await using var db = Database();
        (await Service(db).GetBalanceSheetAsync(default)).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Posted_accounts_reversals_and_unclosed_surplus_reconcile()
    {
        await using var db = Database();
        SeedAccounts(db);
        Journal(db, 1, JournalState.Posted, 1000m, 300m, 200m, 500m, 0m);
        Journal(db, 2, JournalState.Posted, -100m, 0m, 0m, 0m, 100m);
        Journal(db, 3, JournalState.Posted, -50m, 0m, 0m, -50m, 0m);
        Journal(db, 4, JournalState.Draft, 999m, 0m, 0m, 999m, 0m);
        await db.SaveChangesAsync();
        var result = await Service(db).GetBalanceSheetAsync(AsOf);
        result.StatusCode.Should().Be(200);
        result.Data!.TotalAssets.Should().Be(850m);
        result.Data.TotalLiabilities.Should().Be(300m);
        result.Data.TotalEquity.Should().Be(200m);
        result.Data.UnclosedNetSurplus.Should().Be(350m);
        result.Data.TotalLiabilitiesAndEquity.Should().Be(850m);
        result.Data.AssetRows.Should().ContainSingle(x => x.AccountCode == "CASH" && x.Amount == 850m);
    }

    [Fact]
    public async Task Unbalanced_posted_journal_is_rejected()
    {
        await using var db = Database();
        SeedAccounts(db);
        Journal(db, 1, JournalState.Posted, 1000m, 300m, 200m, 500m, 0m);
        db.JournalLines.Local.Single(x => x.JournalId == 1 && x.AccountId == 2).CreditAmount = 299m;
        await db.SaveChangesAsync();
        (await Service(db).GetBalanceSheetAsync(AsOf)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Missing_posting_account_is_rejected()
    {
        await using var db = Database();
        SeedAccounts(db);
        Journal(db, 1, JournalState.Posted, 1000m, 300m, 200m, 500m, 0m);
        db.JournalLines.Local.Single(x => x.JournalId == 1 && x.AccountId == 2).AccountId = 404;
        await db.SaveChangesAsync();
        (await Service(db).GetBalanceSheetAsync(AsOf)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Mixed_currency_journal_is_rejected()
    {
        await using var db = Database();
        SeedAccounts(db);
        Journal(db, 1, JournalState.Posted, 1000m, 300m, 200m, 500m, 0m);
        db.Journals.Local.Single().CurrencyCode = "USD";
        await db.SaveChangesAsync();
        (await Service(db).GetBalanceSheetAsync(AsOf)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Mixed_currency_posting_account_is_rejected()
    {
        await using var db = Database();
        SeedAccounts(db);
        Journal(db, 1, JournalState.Posted, 1000m, 300m, 200m, 500m, 0m);
        db.Accounts.Local.Single(x => x.Id == 1).CurrencyCode = "USD";
        await db.SaveChangesAsync();
        (await Service(db).GetBalanceSheetAsync(AsOf)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Empty_posted_journal_is_rejected()
    {
        await using var db = Database();
        db.Journals.Add(new Journal { Id=1, TenantId=41, JournalNumber="J-1",
            JournalDate=AsOf, AccountingPeriodId=1, State=JournalState.Posted });
        await db.SaveChangesAsync();
        (await Service(db).GetBalanceSheetAsync(AsOf)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Invalid_double_sided_posting_line_is_rejected()
    {
        await using var db = Database();
        SeedAccounts(db);
        Journal(db, 1, JournalState.Posted, 1000m, 300m, 200m, 500m, 0m);
        db.JournalLines.Local.Single(x => x.JournalId == 1 && x.AccountId == 1).CreditAmount = 1m;
        await db.SaveChangesAsync();
        (await Service(db).GetBalanceSheetAsync(AsOf)).StatusCode.Should().Be(409);
    }

    private static void SeedAccounts(EduOSDbContext db)
    {
        db.Accounts.AddRange(
            new Account { Id=1, TenantId=41, Code="CASH", Name="Cash", AccountType=AccountType.Asset },
            new Account { Id=2, TenantId=41, Code="PAYABLE", Name="Payable", AccountType=AccountType.Liability },
            new Account { Id=3, TenantId=41, Code="CAPITAL", Name="Capital", AccountType=AccountType.Equity },
            new Account { Id=4, TenantId=41, Code="FEE", Name="Fee income", AccountType=AccountType.Income },
            new Account { Id=5, TenantId=41, Code="SALARY", Name="Salary expense", AccountType=AccountType.Expense });
    }

    // Signed movements: negative amount is posted on the opposite side, never as a negative line.
    private static void Journal(EduOSDbContext db, long id, JournalState state,
        decimal asset, decimal liability, decimal equity, decimal income, decimal expense)
    {
        db.Journals.Add(new Journal { Id=id, TenantId=41, JournalNumber="J-"+id,
            JournalDate=AsOf, AccountingPeriodId=1, State=state });
        var movements = new[] {
            (AccountId:1L, Amount:asset), (AccountId:2L, Amount:-liability),
            (AccountId:3L, Amount:-equity), (AccountId:4L, Amount:-income),
            (AccountId:5L, Amount:expense)
        };
        var lineNo = 0;
        foreach (var item in movements.Where(x => x.Amount != 0m))
        {
            ++lineNo;
            db.JournalLines.Add(new JournalLine { Id=id*10+lineNo, TenantId=41, JournalId=id,
                AccountId=item.AccountId, LineNo=lineNo,
                DebitAmount=Math.Max(0m, item.Amount), CreditAmount=Math.Max(0m, -item.Amount) });
        }
    }

    private static AccountingReportingService Service(EduOSDbContext db, string role="TenantAdmin") =>
        new(db, new TestUser(role));
    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("balance-sheet-" + Guid.NewGuid().ToString("N")).Options, accessor);
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
