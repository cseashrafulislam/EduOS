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
        response.Data.Exceptions.Should().HaveCount(exceptions);
        response.Data.ClosingVariance.Should().Be(0m);
        (await db.Journals.SingleAsync()).State.Should().Be(JournalState.Posted);
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
        response.Data.UnmatchedStatementCount.Should().Be(2);
        response.Data.UnmatchedLedgerCount.Should().Be(1);
        response.Data.Exceptions.Should().OnlyContain(x => x.Reason.Contains("Duplicate key"));
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
