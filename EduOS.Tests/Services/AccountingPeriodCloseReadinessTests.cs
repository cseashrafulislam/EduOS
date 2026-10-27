using EduOS.Core.DTOs.Accounting;
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

public sealed class AccountingPeriodCloseReadinessTests
{
    [Fact]
    public async Task Preview_is_restricted_to_period_closing_roles()
    {
        await using var db = Database();
        var result = await Service(db, "Accountant").GetPeriodCloseReadinessAsync(1);
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Foreign_tenant_period_is_not_visible()
    {
        var name = "period-close-" + Guid.NewGuid().ToString("N");
        long foreignId;
        await using (var other = Database(name, 42))
        {
            var period = new AccountingPeriod { TenantId = 42, Name = "Foreign",
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
            other.AccountingPeriods.Add(period);
            await other.SaveChangesAsync();
            foreignId = period.Id;
        }
        await using var db = Database(name);
        (await Service(db).GetPeriodCloseReadinessAsync(foreignId)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Unposted_journal_blocks_closure()
    {
        await using var db = Database();
        var period = await AddPeriod(db);
        db.Journals.Add(new Journal { TenantId = 41, AccountingPeriodId = period.Id,
            JournalDate = new DateOnly(2026, 2, 1), JournalNumber = "JV-DRAFT",
            State = JournalState.Draft });
        await db.SaveChangesAsync();
        var data = (await Service(db).GetPeriodCloseReadinessAsync(period.Id)).Data!;
        data.CanClose.Should().BeFalse();
        data.DraftCount.Should().Be(1);
        data.Blockers.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Balanced_posted_journal_is_ready_to_close()
    {
        await using var db = Database();
        var period = await AddPeriod(db);
        await AddPostedJournal(db, period.Id, 125.50m, 125.50m);
        var data = (await Service(db).GetPeriodCloseReadinessAsync(period.Id)).Data!;
        data.CanClose.Should().BeTrue();
        data.PostedCount.Should().Be(1);
        data.UnbalancedJournalCount.Should().Be(0);
        data.InvalidLineCount.Should().Be(0);
    }

    [Fact]
    public async Task Unbalanced_posted_journal_is_blocked()
    {
        await using var db = Database();
        var period = await AddPeriod(db);
        await AddPostedJournal(db, period.Id, 125m, 100m);
        var data = (await Service(db).GetPeriodCloseReadinessAsync(period.Id)).Data!;
        data.CanClose.Should().BeFalse();
        data.UnbalancedJournalCount.Should().Be(1);
    }

    [Fact]
    public async Task Out_of_period_posting_is_blocked()
    {
        await using var db = Database();
        var period = await AddPeriod(db);
        await AddPostedJournal(db, period.Id, 10m, 10m, new DateOnly(2027, 1, 1));
        var data = (await Service(db).GetPeriodCloseReadinessAsync(period.Id)).Data!;
        data.CanClose.Should().BeFalse();
        data.InvalidJournalCount.Should().Be(1);
    }

    [Theory]
    [InlineData(JournalState.Posted)]
    [InlineData(JournalState.Draft)]
    public async Task Legacy_soft_deleted_journal_blocks_period_close_preview(JournalState state)
    {
        await using var db = Database();
        var period = await AddPeriod(db);
        db.Journals.Add(new Journal { TenantId = 41, AccountingPeriodId = period.Id,
            JournalDate = new DateOnly(2026, 2, 1), JournalNumber = "JV-HISTORICAL",
            State = state, IsDeleted = true });
        await db.SaveChangesAsync();

        var result = await Service(db).GetPeriodCloseReadinessAsync(period.Id);
        result.Success.Should().BeTrue();
        result.Data!.CanClose.Should().BeFalse();
        result.Data.HistoricalDeletedJournalCount.Should().Be(1);
        result.Data.Blockers.Should().Contain(x => x.Contains("soft-deleted"));
    }

    [Fact]
    public async Task Soft_deleted_posting_lines_block_close_even_when_visible_lines_balance()
    {
        await using var db = Database();
        var period = await AddPeriod(db);
        await AddPostedJournal(db, period.Id, 125m, 125m);
        var journal = await db.Journals.SingleAsync(x => x.AccountingPeriodId == period.Id);
        var accounts = await db.Accounts.OrderBy(x => x.Id).ToArrayAsync();
        db.JournalLines.AddRange(
            new JournalLine { TenantId = 41, JournalId = journal.Id, LineNo = 3,
                AccountId = accounts[0].Id, DebitAmount = 25m, IsDeleted = true },
            new JournalLine { TenantId = 41, JournalId = journal.Id, LineNo = 4,
                AccountId = accounts[1].Id, CreditAmount = 25m, IsDeleted = true });
        await db.SaveChangesAsync();

        var result = await Service(db).GetPeriodCloseReadinessAsync(period.Id);
        result.StatusCode.Should().Be(200);
        result.Data!.CanClose.Should().BeFalse();
        result.Data.HistoricalDeletedJournalLineCount.Should().Be(2);
        result.Data.UnbalancedJournalCount.Should().Be(0);
        result.Data.Blockers.Should().Contain(x => x.Contains("soft-deleted journal lines"));
    }

    [Fact]
    public async Task Soft_deleted_lines_in_another_period_do_not_block_close()
    {
        await using var db = Database();
        var target = await AddPeriod(db);
        var other = new AccountingPeriod { TenantId = 41, Name = "FY 2027",
            StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 12, 31) };
        db.AccountingPeriods.Add(other);
        await db.SaveChangesAsync();
        await AddPostedJournal(db, other.Id, 10m, 10m, new DateOnly(2027, 2, 1));
        var journal = await db.Journals.SingleAsync(x => x.AccountingPeriodId == other.Id);
        var account = await db.Accounts.FirstAsync(x => x.TenantId == 41);
        db.JournalLines.Add(new JournalLine { TenantId = 41, JournalId = journal.Id,
            LineNo = 3, AccountId = account.Id, DebitAmount = 1m, IsDeleted = true });
        await db.SaveChangesAsync();

        var result = await Service(db).GetPeriodCloseReadinessAsync(target.Id);
        result.StatusCode.Should().Be(200);
        result.Data!.CanClose.Should().BeTrue();
        result.Data.HistoricalDeletedJournalLineCount.Should().Be(0);
    }

    [Fact]
    public async Task Legacy_soft_deleted_journal_prevents_editing_accounting_period()
    {
        await using var db = Database();
        var period = await AddPeriod(db);
        period.RowVersion = new byte[] { 1, 2, 3 };
        db.Journals.Add(new Journal { TenantId = 41, AccountingPeriodId = period.Id,
            JournalDate = new DateOnly(2026, 2, 1), JournalNumber = "JV-HISTORICAL",
            State = JournalState.Posted, IsDeleted = true });
        await db.SaveChangesAsync();

        var result = await Service(db).SavePeriodAsync(period.Id, new SaveAccountingPeriodRequestDto
        {
            Name = "Renamed period", StartDate = period.StartDate, EndDate = period.EndDate,
            RowVersion = Convert.ToBase64String(period.RowVersion)
        });
        result.StatusCode.Should().Be(409);
        result.Message.Should().Contain("with journals");
        period.Name.Should().Be("FY 2026");
    }

    private static async Task<AccountingPeriod> AddPeriod(EduOSDbContext db)
    {
        var period = new AccountingPeriod { TenantId = 41, Name = "FY 2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();
        return period;
    }

    private static async Task AddPostedJournal(EduOSDbContext db, long periodId,
        decimal debit, decimal credit, DateOnly? date = null)
    {
        var a = new Account { TenantId = 41, Code = "1000", Name = "Cash", AccountType = AccountType.Asset };
        var b = new Account { TenantId = 41, Code = "4000", Name = "Income", AccountType = AccountType.Income };
        db.Accounts.AddRange(a, b);
        await db.SaveChangesAsync();
        var j = new Journal { TenantId = 41, AccountingPeriodId = periodId,
            JournalDate = date ?? new DateOnly(2026, 3, 1), JournalNumber = "JV-POSTED",
            State = JournalState.Posted, CurrencyCode = "BDT" };
        db.Journals.Add(j);
        await db.SaveChangesAsync();
        db.JournalLines.AddRange(
            new JournalLine { TenantId = 41, JournalId = j.Id, LineNo = 1,
                AccountId = a.Id, DebitAmount = debit },
            new JournalLine { TenantId = 41, JournalId = j.Id, LineNo = 2,
                AccountId = b.Id, CreditAmount = credit });
        await db.SaveChangesAsync();
    }

    private static AccountingSetupService Service(EduOSDbContext db, string role = "TenantAdmin")
        => new(db, new User(role), TimeProvider.System);

    private static EduOSDbContext Database(string? name = null, long tenant = 41)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenant;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase(name ?? "period-close-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, accessor);
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
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
