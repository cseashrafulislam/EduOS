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

public sealed class BankStatementReconciliationPreviewTests
{
    private static readonly DateOnly Day = new(2026, 10, 15);
    [Theory]
    [InlineData("DEP-1", 1, 0)]
    [InlineData(" dep-1 ", 1, 0)]
    [InlineData("OTHER", 0, 2)]
    [InlineData("", 0, 2)]
    public async Task Exact_unique_reference_only_is_matched(string reference, int matched, int exceptions)
    {
        await using var db = Database();
        Seed(db);
        var response = await new AccountingReportingService(db, new User())
            .PreviewBankStatementAsync(Request(reference));
        response.StatusCode.Should().Be(200);
        response.Data!.MatchedCount.Should().Be(matched);
        response.Data.Matches.Should().HaveCount(matched);
        if (matched == 1)
        {
            response.Data.Matches[0].JournalNumber.Should().Be("J-1");
            response.Data.Matches[0].JournalLineId.Should().Be(1);
            response.Data.Matches[0].StatementRowNumber.Should().Be(1);
            response.Data.Matches[0].JournalLineVersion.Should().NotBeNull();
            response.Data.Matches[0].Deposit.Should().Be(100m);
        }
        response.Data.Exceptions.Should().HaveCount(exceptions);
        if (exceptions > 0)
        {
            response.Data.Exceptions.Single(x => x.Source == "Ledger").JournalLineId.Should().Be(1);
            response.Data.Exceptions.Single(x => x.Source == "Statement").StatementRowNumber.Should().Be(1);
        }
        response.Data.ClosingVariance.Should().Be(0m);
        response.Data.IsExactMatch.Should().Be(matched == 1);
        (await db.Journals.SingleAsync()).State.Should().Be(JournalState.Posted);
    }
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(100, -1)]
    [InlineData(-1, 100)]
    public async Task Direct_api_rejects_negative_deposit_or_withdrawal(decimal deposit, decimal withdrawal)
    {
        await using var db = Database();
        Seed(db);
        var request = Request("DEP-1");
        request.Movements = new[]
        {
            new BankStatementMovementDto { Date = Day, Reference = "DEP-1",
                Deposit = deposit, Withdrawal = withdrawal }
        };
        (await new AccountingReportingService(db, new User())
            .PreviewBankStatementAsync(request)).StatusCode.Should().Be(400);
    }
    [Fact]
    public async Task Invalid_statement_balance_is_rejected()
    {
        await using var db = Database();
        Seed(db);
        var request = Request("DEP-1"); request.ClosingBalance = 99m;
        (await new AccountingReportingService(db, new User())
            .PreviewBankStatementAsync(request)).StatusCode.Should().Be(422);
    }
    [Fact]
    public async Task Non_admin_and_foreign_bank_are_denied()
    {
        await using var db = Database();
        Seed(db);
        (await new AccountingReportingService(db, new User(false))
            .PreviewBankStatementAsync(Request("DEP-1"))).StatusCode.Should().Be(403);
        var request = Request("DEP-1"); request.BankAccountId = 999;
        (await new AccountingReportingService(db, new User())
            .PreviewBankStatementAsync(request)).StatusCode.Should().Be(404);
    }
    [Fact]
    public async Task Duplicate_statement_rows_keep_distinct_ordinals_without_mutating_ledger()
    {
        await using var db = Database();
        Seed(db);
        var shared = new BankStatementMovementDto { Date = Day, Reference = "DEP-1", Deposit = 100m };
        var request = Request("DEP-1");
        request.ClosingBalance = 200m;
        request.Movements = new[] { shared, shared };
        var response = await new AccountingReportingService(db, new User())
            .PreviewBankStatementAsync(request);
        response.StatusCode.Should().Be(200);
        response.Data!.Matches.Should().BeEmpty();
        response.Data.Exceptions.Where(x => x.Source == "Statement")
            .Select(x => x.StatementRowNumber).Should().BeEquivalentTo(new int?[] { 1, 2 });
        response.Data.Exceptions.Single(x => x.Source == "Ledger").JournalLineId.Should().Be(1);
        (await db.Journals.SingleAsync()).State.Should().Be(JournalState.Posted);
    }

    [Fact]
    public async Task Duplicate_statement_keys_never_auto_match()
    {
        await using var db = Database();
        Seed(db);
        var request = Request("DEP-1");
        request.ClosingBalance = 200m;
        request.Movements = new[]
        {
            new BankStatementMovementDto { Date = Day, Reference = "DEP-1", Deposit = 100m },
            new BankStatementMovementDto { Date = Day, Reference = "DEP-1", Deposit = 100m }
        };
        var response = await new AccountingReportingService(db, new User())
            .PreviewBankStatementAsync(request);
        response.Data!.MatchedCount.Should().Be(0);
        response.Data.Matches.Should().BeEmpty();
        response.Data.UnmatchedStatementCount.Should().Be(2);
        response.Data.UnmatchedLedgerCount.Should().Be(1);
        response.Data.Exceptions.Should().OnlyContain(x => x.Reason.Contains("Duplicate key"));
    }
    [Fact]
    public async Task Equal_closing_balances_cannot_hide_offsetting_opening_and_movement_differences()
    {
        await using var db = Database();
        Seed(db);
        var request = Request("DEP-1");
        request.OpeningBalance = 10m;
        request.ClosingBalance = 100m;
        request.Movements = new[]
        {
            new BankStatementMovementDto { Date = Day, Reference = "DEP-1", Deposit = 90m }
        };
        var response = await new AccountingReportingService(db, new User())
            .PreviewBankStatementAsync(request);
        response.StatusCode.Should().Be(200);
        response.Data!.OpeningVariance.Should().Be(-10m);
        response.Data.MovementVariance.Should().Be(10m);
        response.Data.ClosingVariance.Should().Be(0m);
        response.Data.IsExactMatch.Should().BeFalse();
        response.Data.Exceptions.Should().OnlyContain(x =>
            x.Reason.Contains("Amount/direction differs"));
    }

    [Fact]
    public async Task Same_reference_on_different_day_is_a_review_hint_not_a_match()
    {
        await using var db = Database();
        Seed(db);
        var request = Request("DEP-1");
        request.ToDate = Day.AddDays(1);
        request.Movements = new[]
        {
            new BankStatementMovementDto { Date = Day.AddDays(1), Reference = "DEP-1", Deposit = 100m }
        };
        var response = await new AccountingReportingService(db, new User())
            .PreviewBankStatementAsync(request);
        response.StatusCode.Should().Be(200);
        response.Data!.MatchedCount.Should().Be(0);
        response.Data.IsExactMatch.Should().BeFalse();
        response.Data.Exceptions.Should().HaveCount(2);
        response.Data.Exceptions.Should().OnlyContain(x => x.Reason == "Date differs: manual review");
        (await db.Journals.SingleAsync()).State.Should().Be(JournalState.Posted);
    }

    [Fact]
    public async Task Reused_reference_on_different_days_cannot_produce_false_exact_match()
    {
        await using var db = Database();
        Seed(db);
        SeedSecondJournal(db, "DEP-1", Day.AddDays(1), 50m);
        var request = Request("DEP-1");
        request.ToDate = Day.AddDays(1);
        request.ClosingBalance = 150m;
        request.Movements = new[]
        {
            new BankStatementMovementDto { Date = Day, Reference = "DEP-1", Deposit = 100m },
            new BankStatementMovementDto { Date = Day.AddDays(1), Reference = "DEP-1", Deposit = 50m }
        };
        var result = await new AccountingReportingService(db, new User()).PreviewBankStatementAsync(request);
        result.StatusCode.Should().Be(200);
        result.Data!.ReferenceCollisionCount.Should().Be(1);
        result.Data.MatchedCount.Should().Be(0);
        result.Data.IsExactMatch.Should().BeFalse();
        result.Data.UnmatchedLedgerCount.Should().Be(2);
        result.Data.UnmatchedStatementCount.Should().Be(2);
        result.Data.Exceptions.Should().HaveCount(4);
        result.Data.Exceptions.Should().OnlyContain(x => x.Reason == "Reference reused: ambiguous");
    }

    [Fact]
    public async Task Reused_reference_in_statement_only_blocks_otherwise_exact_ledger_match()
    {
        await using var db = Database();
        Seed(db);
        var request = Request("DEP-1");
        request.ToDate = Day.AddDays(1);
        request.ClosingBalance = 150m;
        request.Movements = new[]
        {
            new BankStatementMovementDto { Date = Day, Reference = "DEP-1", Deposit = 100m },
            new BankStatementMovementDto { Date = Day.AddDays(1), Reference = "dep-1", Deposit = 50m }
        };
        var result = await new AccountingReportingService(db, new User()).PreviewBankStatementAsync(request);
        result.StatusCode.Should().Be(200);
        result.Data!.ReferenceCollisionCount.Should().Be(1);
        result.Data.MatchedCount.Should().Be(0);
        result.Data.UnmatchedLedgerCount.Should().Be(1);
        result.Data.UnmatchedStatementCount.Should().Be(2);
        result.Data.Exceptions.Should().HaveCount(3);
        result.Data.Exceptions.Should().OnlyContain(x => x.Reason == "Reference reused: ambiguous");
    }

    private static void SeedSecondJournal(EduOSDbContext db, string reference, DateOnly date, decimal amount)
    {
        db.Journals.Add(new Journal { Id = 2, TenantId = 41, JournalNumber = "J-2",
            JournalDate = date, AccountingPeriodId = 1, State = JournalState.Posted,
            ReferenceNumber = reference });
        db.JournalLines.AddRange(
            new JournalLine { Id = 3, TenantId = 41, JournalId = 2, AccountId = 1,
                LineNo = 1, DebitAmount = amount },
            new JournalLine { Id = 4, TenantId = 41, JournalId = 2, AccountId = 2,
                LineNo = 2, CreditAmount = amount });
        db.SaveChanges();
    }

    private static BankStatementPreviewRequestDto Request(string reference) => new()
    {
        BankAccountId = 1, FromDate = Day, ToDate = Day, OpeningBalance = 0m, ClosingBalance = 100m,
        Movements = new[] { new BankStatementMovementDto { Date = Day, Reference = reference, Deposit = 100m } }
    };
    private static void Seed(EduOSDbContext db)
    {
        db.Accounts.AddRange(
            new Account { Id = 1, TenantId = 41, Code = "BANK", Name = "Bank", AccountType = AccountType.Asset },
            new Account { Id = 2, TenantId = 41, Code = "INC", Name = "Income", AccountType = AccountType.Income });
        db.BankAccounts.Add(new BankAccount { Id = 1, TenantId = 41, AccountId = 1,
            BankName = "Bank", AccountName = "Current", AccountNumber = "000" });
        db.Journals.Add(new Journal { Id = 1, TenantId = 41, JournalNumber = "J-1",
            JournalDate = Day, AccountingPeriodId = 1, State = JournalState.Posted, ReferenceNumber = "DEP-1" });
        db.JournalLines.AddRange(
            new JournalLine { Id = 1, TenantId = 41, JournalId = 1, AccountId = 1, LineNo = 1, DebitAmount = 100m },
            new JournalLine { Id = 2, TenantId = 41, JournalId = 1, AccountId = 2, LineNo = 2, CreditAmount = 100m });
        db.SaveChanges();
    }
    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("bank-statement-" + Guid.NewGuid().ToString("N")).Options, accessor);
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
