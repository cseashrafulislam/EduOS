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

public sealed class AccountingHistoricalReportCoverageTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 27);

    [Theory]
    [InlineData("ledger")]
    [InlineData("trial")]
    [InlineData("income")]
    [InlineData("balance")]
    [InlineData("bankbook")]
    [InlineData("liquidity")]
    public async Task Deleted_posted_header_blocks_affected_report(string report)
    {
        await using var db = Database();
        Seed(db);
        Post(db, 1, JournalState.Posted);
        Post(db, 2, JournalState.Posted, deleted: true);
        await db.SaveChangesAsync();
        (await Status(db, report)).Should().Be(409);
    }

    [Theory]
    [InlineData("ledger")]
    [InlineData("trial")]
    [InlineData("income")]
    [InlineData("balance")]
    [InlineData("bankbook")]
    [InlineData("liquidity")]
    public async Task Deleted_balanced_line_pair_blocks_affected_report(string report)
    {
        await using var db = Database();
        Seed(db);
        Post(db, 1, JournalState.Posted);
        db.JournalLines.AddRange(
            new JournalLine { Id = 13, TenantId = 41, JournalId = 1,
                AccountId = 1, LineNo = 3, DebitAmount = 25m, IsDeleted = true },
            new JournalLine { Id = 14, TenantId = 41, JournalId = 1,
                AccountId = 2, LineNo = 4, CreditAmount = 25m, IsDeleted = true });
        await db.SaveChangesAsync();
        (await Status(db, report)).Should().Be(409);
    }

    [Theory]
    [InlineData("ledger")]
    [InlineData("trial")]
    [InlineData("income")]
    [InlineData("balance")]
    [InlineData("bankbook")]
    [InlineData("liquidity")]
    public async Task Deleted_draft_does_not_block_valid_report(string report)
    {
        await using var db = Database();
        Seed(db);
        Post(db, 1, JournalState.Posted);
        Post(db, 2, JournalState.Draft, deleted: true);
        await db.SaveChangesAsync();
        (await Status(db, report)).Should().Be(200);
    }

    [Fact]
    public async Task Bank_reads_are_scoped_to_their_ledger_history()
    {
        await using var db = Database();
        Seed(db);
        Post(db, 1, JournalState.Posted);
        db.Accounts.Add(new Account { Id = 3, TenantId = 41,
            Code = "OTHER", Name = "Other", AccountType = AccountType.Asset });
        db.Journals.Add(new Journal { Id = 2, TenantId = 41, JournalNumber = "J-2",
            JournalDate = To, AccountingPeriodId = 1, State = JournalState.Posted, IsDeleted = true });
        db.JournalLines.AddRange(
            new JournalLine { Id = 21, TenantId = 41, JournalId = 2,
                AccountId = 3, LineNo = 1, DebitAmount = 10m },
            new JournalLine { Id = 22, TenantId = 41, JournalId = 2,
                AccountId = 2, LineNo = 2, CreditAmount = 10m });
        await db.SaveChangesAsync();
        (await Status(db, "bankbook")).Should().Be(200);
        (await Status(db, "liquidity")).Should().Be(200);
        (await Status(db, "trial")).Should().Be(409);
    }

    private static async Task<int> Status(EduOSDbContext db, string report)
    {
        var service = new AccountingReportingService(db, new TestUser());
        return report switch
        {
            "ledger" => (await service.GetLedgerStatementAsync(1, From, To)).StatusCode,
            "trial" => (await service.GetTrialBalanceAsync(To)).StatusCode,
            "income" => (await service.GetIncomeStatementAsync(From, To)).StatusCode,
            "balance" => (await service.GetBalanceSheetAsync(To)).StatusCode,
            "bankbook" => (await service.GetBankBookAsync(1, From, To)).StatusCode,
            "liquidity" => (await service.GetBankLiquidityAsync(From, To)).StatusCode,
            _ => throw new ArgumentOutOfRangeException(nameof(report))
        };
    }

    private static void Seed(EduOSDbContext db)
    {
        db.Accounts.AddRange(
            new Account { Id = 1, TenantId = 41, Code = "BANK",
                Name = "Bank", AccountType = AccountType.Asset },
            new Account { Id = 2, TenantId = 41, Code = "INC",
                Name = "Income", AccountType = AccountType.Income });
        db.BankAccounts.Add(new BankAccount { Id = 1, TenantId = 41, AccountId = 1,
            BankName = "Institution Bank", AccountName = "Operating", AccountNumber = "001" });
    }

    private static void Post(EduOSDbContext db, long id, JournalState state, bool deleted = false)
    {
        db.Journals.Add(new Journal { Id = id, TenantId = 41, JournalNumber = "J-" + id,
            JournalDate = To, AccountingPeriodId = 1, State = state, IsDeleted = deleted });
        db.JournalLines.AddRange(
            new JournalLine { Id = id * 10 + 1, TenantId = 41, JournalId = id,
                AccountId = 1, LineNo = 1, DebitAmount = 100m },
            new JournalLine { Id = id * 10 + 2, TenantId = 41, JournalId = id,
                AccountId = 2, LineNo = 2, CreditAmount = 100m });
    }

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("historical-reports-" + Guid.NewGuid().ToString("N")).Options, accessor);
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
