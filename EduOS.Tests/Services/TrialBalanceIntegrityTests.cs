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

public sealed class TrialBalanceIntegrityTests
{
    private static readonly DateOnly AsOf = new(2026, 10, 27);

    [Fact]
    public async Task Two_offsetting_unbalanced_journals_must_not_pass_trial_balance()
    {
        await using var db = Database();
        SeedAccounts(db);
        AddJournal(db, 1, JournalState.Posted, 100m, 90m);
        AddJournal(db, 2, JournalState.Posted, 90m, 100m);
        await db.SaveChangesAsync();

        (await Service(db).GetTrialBalanceAsync(AsOf)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Balanced_posting_and_compensating_reversal_exclude_drafts()
    {
        await using var db = Database();
        SeedAccounts(db);
        AddJournal(db, 1, JournalState.Posted, 100m, 100m);
        AddJournal(db, 2, JournalState.Reversed, 30m, 30m, reverse: true);
        AddJournal(db, 3, JournalState.Draft, 999m, 999m);
        await db.SaveChangesAsync();

        var result = await Service(db).GetTrialBalanceAsync(AsOf);
        result.StatusCode.Should().Be(200);
        result.Data.Should().HaveCount(2);
        result.Data!.Single(x => x.AccountCode == "CASH").DebitBalance.Should().Be(70m);
        result.Data.Single(x => x.AccountCode == "REVENUE").CreditBalance.Should().Be(70m);
    }

    [Fact]
    public async Task Balanced_visible_posting_with_deleted_lines_is_rejected()
    {
        await using var db = Database();
        SeedAccounts(db);
        AddJournal(db, 1, JournalState.Posted, 100m, 100m);
        db.JournalLines.AddRange(
            new JournalLine { Id = 13, TenantId = 41, JournalId = 1, AccountId = 1,
                LineNo = 3, DebitAmount = 25m, IsDeleted = true },
            new JournalLine { Id = 14, TenantId = 41, JournalId = 1, AccountId = 2,
                LineNo = 4, CreditAmount = 25m, IsDeleted = true });
        await db.SaveChangesAsync();

        var result = await Service(db).GetTrialBalanceAsync(AsOf);
        result.StatusCode.Should().Be(409);
        result.Message.Should().Contain("Soft-deleted posted journal lines");
    }

    [Fact]
    public async Task Soft_deleted_posted_header_is_not_silently_omitted()
    {
        await using var db = Database();
        SeedAccounts(db);
        AddJournal(db, 1, JournalState.Posted, 100m, 100m);
        AddJournal(db, 2, JournalState.Posted, 25m, 25m);
        db.Journals.Local.Single(x => x.Id == 2).IsDeleted = true;
        await db.SaveChangesAsync();

        var result = await Service(db).GetTrialBalanceAsync(AsOf);
        result.StatusCode.Should().Be(409);
        result.Message.Should().Contain("Soft-deleted posted journals");
    }

    [Fact]
    public async Task Deleted_draft_history_does_not_block_valid_posted_trial_balance()
    {
        await using var db = Database();
        SeedAccounts(db);
        AddJournal(db, 1, JournalState.Posted, 100m, 100m);
        AddJournal(db, 2, JournalState.Draft, 25m, 25m);
        db.Journals.Local.Single(x => x.Id == 2).IsDeleted = true;
        await db.SaveChangesAsync();

        (await Service(db).GetTrialBalanceAsync(AsOf)).StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Double_sided_line_is_rejected()
    {
        await using var db = Database();
        SeedAccounts(db);
        AddJournal(db, 1, JournalState.Posted, 100m, 100m);
        db.JournalLines.Local.Single(x => x.JournalId == 1 && x.AccountId == 1).CreditAmount = 1m;
        await db.SaveChangesAsync();

        (await Service(db).GetTrialBalanceAsync(AsOf)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Non_bdt_posted_journal_is_rejected()
    {
        await using var db = Database();
        SeedAccounts(db);
        AddJournal(db, 1, JournalState.Posted, 100m, 100m);
        db.Journals.Local.Single().CurrencyCode = "USD";
        await db.SaveChangesAsync();

        (await Service(db).GetTrialBalanceAsync(AsOf)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Invalid_posting_account_is_rejected()
    {
        await using var db = Database();
        SeedAccounts(db);
        AddJournal(db, 1, JournalState.Posted, 100m, 100m);
        db.Accounts.Local.Single(x => x.Id == 1).IsGroup = true;
        await db.SaveChangesAsync();

        (await Service(db).GetTrialBalanceAsync(AsOf)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Missing_posting_account_is_rejected()
    {
        await using var db = Database();
        SeedAccounts(db);
        AddJournal(db, 1, JournalState.Posted, 100m, 100m);
        db.JournalLines.Local.Single(x => x.JournalId == 1 && x.AccountId == 2).AccountId = 404;
        await db.SaveChangesAsync();

        (await Service(db).GetTrialBalanceAsync(AsOf)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Empty_posted_journal_is_rejected()
    {
        await using var db = Database();
        db.Journals.Add(new Journal { Id = 1, TenantId = 41, JournalNumber = "J-1",
            JournalDate = AsOf, AccountingPeriodId = 1, State = JournalState.Posted });
        await db.SaveChangesAsync();

        (await Service(db).GetTrialBalanceAsync(AsOf)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Tenant_context_mismatch_is_denied()
    {
        await using var db = Database(42);
        (await Service(db).GetTrialBalanceAsync(AsOf)).StatusCode.Should().Be(403);
    }

    private static void SeedAccounts(EduOSDbContext db) =>
        db.Accounts.AddRange(
            new Account { Id = 1, TenantId = 41, Code = "CASH", Name = "Cash", AccountType = AccountType.Asset },
            new Account { Id = 2, TenantId = 41, Code = "REVENUE", Name = "Revenue", AccountType = AccountType.Income });

    private static void AddJournal(EduOSDbContext db, long id, JournalState state,
        decimal debit, decimal credit, bool reverse = false)
    {
        db.Journals.Add(new Journal { Id = id, TenantId = 41, JournalNumber = "J-" + id,
            JournalDate = AsOf, AccountingPeriodId = 1, State = state });
        db.JournalLines.AddRange(
            new JournalLine { Id = id * 10 + 1, TenantId = 41, JournalId = id,
                AccountId = 1, LineNo = 1,
                DebitAmount = reverse ? 0m : debit, CreditAmount = reverse ? debit : 0m },
            new JournalLine { Id = id * 10 + 2, TenantId = 41, JournalId = id,
                AccountId = 2, LineNo = 2,
                DebitAmount = reverse ? credit : 0m, CreditAmount = reverse ? 0m : credit });
    }

    private static AccountingReportingService Service(EduOSDbContext db) => new(db, new TestUser());

    private static EduOSDbContext Database(long tenant = 41)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenant;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("trial-balance-" + Guid.NewGuid().ToString("N")).Options, accessor);
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
