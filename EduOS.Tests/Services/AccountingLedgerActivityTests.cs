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

public sealed class AccountingLedgerActivityTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);

    [Fact]
    public async Task Posted_activity_pages_without_changing_full_period_balances()
    {
        await using var db = Database();
        Seed(db);
        Post(db, 1, new DateOnly(2026, 9, 30), JournalState.Posted, 100m);
        Post(db, 2, new DateOnly(2026, 10, 3), JournalState.Posted, 50m);
        Post(db, 3, new DateOnly(2026, 10, 4), JournalState.Reversed, -20m);
        Post(db, 4, new DateOnly(2026, 10, 5), JournalState.Posted, -20m, reversalOf: 3);
        Post(db, 5, new DateOnly(2026, 10, 6), JournalState.Draft, 900m);
        await db.SaveChangesAsync();

        var service = new AccountingReportingService(db, new User());
        var first = await service.GetLedgerActivityAsync(1, From, To, 1, 2);
        var second = await service.GetLedgerActivityAsync(1, From, To, 2, 2);
        first.StatusCode.Should().Be(200);
        second.StatusCode.Should().Be(200);
        first.Data!.Entries.TotalCount.Should().Be(3);
        first.Data.Entries.Items.Should().HaveCount(2);
        second.Data!.Entries.Items.Should().ContainSingle();
        first.Data.OpeningBalance.Should().Be(100m);
        first.Data.TotalDebit.Should().Be(50m);
        first.Data.TotalCredit.Should().Be(40m);
        first.Data.ClosingBalance.Should().Be(110m);
        second.Data.ClosingBalance.Should().Be(110m);
        second.Data.Entries.Items.Single().IsReversal.Should().BeTrue();
        first.Data.Entries.Items.Last().IsReversedOriginal.Should().BeTrue();
        (await db.Journals.CountAsync()).Should().Be(5);
    }

    [Fact]
    public async Task Tenant_role_account_and_invalid_paging_fail_closed()
    {
        await using var db = Database();
        Seed(db);
        var admin = new AccountingReportingService(db, new User());
        (await new AccountingReportingService(db, new User(false))
            .GetLedgerActivityAsync(1, From, To)).StatusCode.Should().Be(403);
        (await admin.GetLedgerActivityAsync(999, From, To)).StatusCode.Should().Be(404);
        (await admin.GetLedgerActivityAsync(1, To, From)).StatusCode.Should().Be(400);
        (await admin.GetLedgerActivityAsync(1, From, To, 0, 25)).StatusCode.Should().Be(400);
        (await admin.GetLedgerActivityAsync(1, From, To, 1, 101)).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Tampered_posted_journal_blocks_report_instead_of_exporting_false_totals()
    {
        await using var db = Database();
        Seed(db);
        Post(db, 1, From, JournalState.Posted, 50m);
        db.JournalLines.Local.Single(x => x.JournalId == 1 && x.AccountId == 2).CreditAmount = 49m;
        await db.SaveChangesAsync();
        (await new AccountingReportingService(db, new User())
            .GetLedgerActivityAsync(1, From, To)).StatusCode.Should().Be(409);
    }

    private static void Seed(EduOSDbContext db) => db.Accounts.AddRange(
        new Account { Id = 1, TenantId = 41, Code = "CASH", Name = "Cash account", AccountType = AccountType.Asset },
        new Account { Id = 2, TenantId = 41, Code = "INC", Name = "Income", AccountType = AccountType.Income });

    private static void Post(EduOSDbContext db, long id, DateOnly date, JournalState state,
        decimal amount, long? reversalOf = null)
    {
        db.Journals.Add(new Journal { Id = id, TenantId = 41, PublicId = Guid.NewGuid(),
            JournalNumber = "J-" + id, JournalDate = date, AccountingPeriodId = 1,
            State = state, CurrencyCode = "BDT", ReversalOfJournalId = reversalOf,
            ReferenceNumber = "REF-" + id, SourceType = "Manual" });
        db.JournalLines.AddRange(
            new JournalLine { Id = id * 10 + 1, TenantId = 41, JournalId = id, LineNo = 1,
                AccountId = 1, DebitAmount = Math.Max(0m, amount), CreditAmount = Math.Max(0m, -amount) },
            new JournalLine { Id = id * 10 + 2, TenantId = 41, JournalId = id, LineNo = 2,
                AccountId = 2, DebitAmount = Math.Max(0m, -amount), CreditAmount = Math.Max(0m, amount) });
    }

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("journal-activity-" + Guid.NewGuid().ToString("N")).Options, accessor);
    }

    private sealed class User(bool admin = true) : ICurrentUserService
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
