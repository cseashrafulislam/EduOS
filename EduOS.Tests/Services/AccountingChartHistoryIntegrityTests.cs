using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Accounting;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AccountingChartHistoryIntegrityTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Historical_journal_line_blocks_reparenting_or_deactivation(bool historicalDeleted, bool reparent)
    {
        await using var db = Database();
        var first = await AddAccount(db, "100", "Assets", true);
        var second = await AddAccount(db, "200", "Other assets", true);
        var ledger = await AddAccount(db, "101", "Cash", false, first.Id);
        var journal = new Journal { TenantId = 41, JournalNumber = "JV-HISTORY",
            JournalDate = new DateOnly(2026, 10, 1), AccountingPeriodId = 1, State = JournalState.Posted };
        db.Journals.Add(journal);
        await db.SaveChangesAsync();
        db.JournalLines.Add(new JournalLine { TenantId = 41, JournalId = journal.Id,
            AccountId = ledger.Id, LineNo = 1, DebitAmount = 100m, IsDeleted = historicalDeleted });
        await db.SaveChangesAsync();

        var result = await Service(db).SaveAccountAsync(ledger.Id,
            Request(ledger, parent: reparent ? second.Id : first.Id, active: reparent));
        result.StatusCode.Should().Be(409);
        result.Message.Should().Contain("journal history");
        (await db.Accounts.SingleAsync(x => x.Id == ledger.Id)).ParentAccountId.Should().Be(first.Id);
        ledger.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Group_with_children_cannot_move_or_change_type()
    {
        await using var db = Database();
        var first = await AddAccount(db, "100", "Assets", true);
        var second = await AddAccount(db, "200", "Other assets", true);
        var group = await AddAccount(db, "110", "Current assets", true, first.Id);
        await AddAccount(db, "111", "Cash", false, group.Id);
        var move = await Service(db).SaveAccountAsync(group.Id, Request(group, parent: second.Id));
        move.StatusCode.Should().Be(409);
        move.Message.Should().Contain("has children");
        var typeChange = Request(group, parent: first.Id);
        typeChange.AccountType = AccountType.Liability;
        (await Service(db).SaveAccountAsync(group.Id, typeChange)).StatusCode.Should().Be(409);
        group.ParentAccountId.Should().Be(first.Id);
        group.AccountType.Should().Be(AccountType.Asset);
    }

    [Fact]
    public async Task Bank_linked_account_cannot_be_deactivated_before_first_journal()
    {
        await using var db = Database();
        var ledger = await AddAccount(db, "120", "Bank cash", false);
        db.BankAccounts.Add(new BankAccount { TenantId = 41, AccountId = ledger.Id,
            BankName = "Test bank", AccountName = "Institution", AccountNumber = "001" });
        await db.SaveChangesAsync();
        var result = await Service(db).SaveAccountAsync(ledger.Id, Request(ledger, active: false));
        result.StatusCode.Should().Be(409);
        result.Message.Should().Contain("Bank-linked");
        ledger.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Unused_account_can_be_reparented_without_touching_journal_history()
    {
        await using var db = Database();
        var first = await AddAccount(db, "100", "Assets", true);
        var second = await AddAccount(db, "200", "Other assets", true);
        var ledger = await AddAccount(db, "101", "Cash", false, first.Id);
        var request = Request(ledger, parent: second.Id);
        request.Name = "Cash renamed";
        var result = await Service(db).SaveAccountAsync(ledger.Id, request);
        result.Success.Should().BeTrue();
        result.Data!.ParentAccountId.Should().Be(second.Id);
        result.Data.Name.Should().Be("Cash renamed");
    }

    [Fact]
    public async Task Accounting_setup_rejects_authenticated_tenant_context_mismatch()
    {
        await using var db = Database(42);
        var service = Service(db);
        (await service.GetAccountsAsync(1, 25, null)).StatusCode.Should().Be(403);
        (await service.SaveAccountAsync(null, new SaveAccountRequestDto {
            Code = "100", Name = "Cash", AccountType = AccountType.Asset
        })).StatusCode.Should().Be(403);
        db.Accounts.Should().BeEmpty();
    }

    private static SaveAccountRequestDto Request(Account account, long? parent = null, bool active = true)
        => new() { Code = account.Code, Name = account.Name, AccountType = account.AccountType,
            ParentAccountId = parent, IsGroup = account.IsGroup, IsActive = active,
            RowVersion = Convert.ToBase64String(account.RowVersion) };

    private static async Task<Account> AddAccount(EduOSDbContext db, string code,
        string name, bool group, long? parent = null)
    {
        var row = new Account { TenantId = 41, Code = code, Name = name,
            AccountType = AccountType.Asset, IsGroup = group, AllowsPosting = !group,
            ParentAccountId = parent, IsActive = true, RowVersion = new byte[] { 1, 2, 3, 4 } };
        db.Accounts.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    private static AccountingSetupService Service(EduOSDbContext db)
        => new(db, new TestUser(), TimeProvider.System);

    private static EduOSDbContext Database(long tenant = 41)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("chart-history-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options, new HttpContextAccessor { HttpContext = http });
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
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
