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

public sealed class BankLiquidityServiceTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 25);

    [Theory]
    [InlineData("Accountant")]
    [InlineData("Principal")]
    [InlineData("Staff")]
    public async Task Institution_wide_bank_liquidity_is_admin_only(string role)
    {
        await using var db = Database();
        (await Service(db, role).GetBankLiquidityAsync(From, To)).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Opening_external_and_internal_transfers_reconcile_without_drafts()
    {
        await using var db = Database();
        Seed(db);
        Post(db, 1, From.AddDays(-1), JournalState.Posted, (1, 50m, 0m), (3, 0m, 50m));
        Post(db, 2, From, JournalState.Posted, (1, 100m, 0m), (3, 0m, 100m));
        Post(db, 3, From.AddDays(1), JournalState.Posted, (4, 20m, 0m), (1, 0m, 20m));
        Post(db, 4, From.AddDays(2), JournalState.Posted, (2, 25m, 0m), (1, 0m, 25m));
        Post(db, 5, From.AddDays(3), JournalState.Draft, (1, 500m, 0m), (3, 0m, 500m));
        await db.SaveChangesAsync();
        var result = await Service(db).GetBankLiquidityAsync(From, To);
        result.StatusCode.Should().Be(200);
        var data = result.Data!;
        data.OpeningBalance.Should().Be(50m);
        data.ExternalInflows.Should().Be(100m);
        data.ExternalOutflows.Should().Be(20m);
        data.TransferIn.Should().Be(25m);
        data.TransferOut.Should().Be(25m);
        data.ClosingBalance.Should().Be(130m);
        data.Banks.Single(x => x.BankAccountId == 1).ClosingBalance.Should().Be(105m);
        data.Banks.Single(x => x.BankAccountId == 2).ClosingBalance.Should().Be(25m);
    }

    [Fact]
    public async Task Compensating_reversal_is_included_without_mutating_original()
    {
        await using var db = Database();
        Seed(db);
        Post(db, 1, From, JournalState.Reversed, (1, 30m, 0m), (3, 0m, 30m));
        Post(db, 2, From.AddDays(1), JournalState.Posted, (3, 30m, 0m), (1, 0m, 30m));
        await db.SaveChangesAsync();
        var result = (await Service(db).GetBankLiquidityAsync(From, To)).Data!;
        result.ExternalInflows.Should().Be(30m);
        result.ExternalOutflows.Should().Be(30m);
        result.ClosingBalance.Should().Be(0m);
    }

    [Fact]
    public async Task Duplicate_bank_mapping_is_rejected()
    {
        await using var db = Database();
        Seed(db);
        db.BankAccounts.Add(new BankAccount { Id = 3, TenantId = 41, AccountId = 1,
            BankName = "Duplicate", AccountName = "Duplicate", AccountNumber = "003" });
        await db.SaveChangesAsync();
        (await Service(db).GetBankLiquidityAsync(From, To)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Ambiguous_mixed_bank_transfer_is_rejected()
    {
        await using var db = Database();
        Seed(db);
        Post(db, 1, From, JournalState.Posted, (2, 70m, 0m), (1, 0m, 50m), (3, 0m, 20m));
        await db.SaveChangesAsync();
        (await Service(db).GetBankLiquidityAsync(From, To)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Unbalanced_journal_is_rejected()
    {
        await using var db = Database();
        Seed(db);
        Post(db, 1, From, JournalState.Posted, (1, 20m, 0m), (3, 0m, 19m));
        await db.SaveChangesAsync();
        (await Service(db).GetBankLiquidityAsync(From, To)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Wrong_campus_is_rejected()
    {
        await using var db = Database();
        Seed(db);
        db.BankAccounts.Local.Single(x => x.Id == 1).CampusId = 7;
        Post(db, 1, From, JournalState.Posted, (1, 20m, 0m), (3, 0m, 20m));
        db.Journals.Local.Single().CampusId = 8;
        await db.SaveChangesAsync();
        (await Service(db).GetBankLiquidityAsync(From, To)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Foreign_tenant_banks_are_not_disclosed()
    {
        var name = "liquidity-foreign-" + Guid.NewGuid().ToString("N");
        await using (var foreign = Database(name, 99))
        {
            foreign.Accounts.Add(new Account { Id = 1, TenantId = 99, Code = "FOREIGN",
                Name = "Foreign", AccountType = AccountType.Asset });
            foreign.BankAccounts.Add(new BankAccount { Id = 1, TenantId = 99, AccountId = 1,
                BankName = "Foreign Bank", AccountName = "Foreign", AccountNumber = "999" });
            await foreign.SaveChangesAsync();
        }
        await using var db = Database(name, 41);
        var result = await Service(db).GetBankLiquidityAsync(From, To);
        result.StatusCode.Should().Be(200);
        result.Data!.Banks.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_date_range_is_rejected()
    {
        await using var db = Database();
        (await Service(db).GetBankLiquidityAsync(To, From)).StatusCode.Should().Be(400);
    }

    private static void Seed(EduOSDbContext db)
    {
        db.Accounts.AddRange(
            new Account { Id = 1, TenantId = 41, Code = "B1", Name = "Bank 1", AccountType = AccountType.Asset },
            new Account { Id = 2, TenantId = 41, Code = "B2", Name = "Bank 2", AccountType = AccountType.Asset },
            new Account { Id = 3, TenantId = 41, Code = "INC", Name = "Income", AccountType = AccountType.Income },
            new Account { Id = 4, TenantId = 41, Code = "EXP", Name = "Expense", AccountType = AccountType.Expense });
        db.BankAccounts.AddRange(
            new BankAccount { Id = 1, TenantId = 41, AccountId = 1,
                BankName = "First", AccountName = "Main", AccountNumber = "001" },
            new BankAccount { Id = 2, TenantId = 41, AccountId = 2,
                BankName = "Second", AccountName = "Reserve", AccountNumber = "002" });
    }

    private static void Post(EduOSDbContext db, long id, DateOnly date, JournalState state,
        params (long account, decimal debit, decimal credit)[] lines)
    {
        db.Journals.Add(new Journal { Id = id, TenantId = 41, JournalNumber = "J-" + id,
            JournalDate = date, AccountingPeriodId = 1, State = state, CurrencyCode = "BDT" });
        for (var i = 0; i < lines.Length; i++)
            db.JournalLines.Add(new JournalLine { Id = id * 10 + i + 1, TenantId = 41,
                JournalId = id, LineNo = i + 1, AccountId = lines[i].account,
                DebitAmount = lines[i].debit, CreditAmount = lines[i].credit });
    }

    private static AccountingReportingService Service(EduOSDbContext db, string role = "TenantAdmin") =>
        new(db, new TestUser(role));
    private static EduOSDbContext Database(string? name = null, long tenantId = 41)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantId;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase(name ?? "bank-liquidity-" + Guid.NewGuid().ToString("N")).Options, accessor);
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
