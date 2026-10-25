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

public sealed class BankBookServiceTests
{
    private static readonly DateOnly Start = new(2026, 10, 1);
    private static readonly DateOnly End = new(2026, 10, 25);

    [Theory]
    [InlineData("Accountant")]
    [InlineData("Principal")]
    [InlineData("Staff")]
    public async Task Institution_wide_bank_book_requires_tenant_admin(string role)
    {
        await using var db = Database();
        (await Service(db, role).GetBankBookAsync(1, Start, End)).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Missing_bank_is_not_a_ledger_discovery_fallback()
    {
        await using var db = Database();
        (await Service(db).GetBankBookAsync(1, Start, End)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Posted_deposits_withdrawals_and_reversals_reconcile_without_drafts()
    {
        await using var db = Database();
        Seed(db);
        Post(db, 1, new DateOnly(2026, 9, 30), JournalState.Posted, 100m);
        Post(db, 2, new DateOnly(2026, 10, 3), JournalState.Posted, 50m);
        Post(db, 3, new DateOnly(2026, 10, 5), JournalState.Reversed, -20m);
        Post(db, 4, new DateOnly(2026, 10, 6), JournalState.Posted, -20m);
        Post(db, 5, new DateOnly(2026, 10, 7), JournalState.Draft, 500m);
        await db.SaveChangesAsync();
        var result = await Service(db).GetBankBookAsync(1, Start, End);
        result.StatusCode.Should().Be(200);
        result.Data!.OpeningBalance.Should().Be(100m);
        result.Data.TotalInflows.Should().Be(50m);
        result.Data.TotalOutflows.Should().Be(40m);
        result.Data.ClosingBalance.Should().Be(110m);
        result.Data.Entries.Should().HaveCount(3);
        result.Data.Entries.Last().RunningBalance.Should().Be(110m);
    }

    [Fact]
    public async Task Duplicate_bank_to_ledger_mapping_is_rejected()
    {
        await using var db = Database();
        Seed(db);
        db.BankAccounts.Add(new BankAccount { Id=2, TenantId=41, AccountId=1,
            BankName="Second Bank", AccountName="Shared Ledger", AccountNumber="X", CurrencyCode="BDT" });
        await db.SaveChangesAsync();
        (await Service(db).GetBankBookAsync(1, Start, End)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Unbalanced_posted_bank_journal_is_rejected()
    {
        await using var db = Database();
        Seed(db);
        Post(db, 1, Start, JournalState.Posted, 50m);
        db.JournalLines.Local.Single(x => x.JournalId == 1 && x.AccountId == 2).CreditAmount = 49m;
        await db.SaveChangesAsync();
        (await Service(db).GetBankBookAsync(1, Start, End)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Other_tenant_bank_cannot_be_read()
    {
        await using var db = Database();
        Seed(db);
        db.BankAccounts.Local.Single().TenantId = 99;
        await db.SaveChangesAsync();
        (await Service(db).GetBankBookAsync(1, Start, End)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Wrong_campus_attribution_is_rejected()
    {
        await using var db = Database();
        Seed(db);
        db.BankAccounts.Local.Single().CampusId = 7;
        Post(db, 1, Start, JournalState.Posted, 50m);
        db.Journals.Local.Single().CampusId = 8;
        await db.SaveChangesAsync();
        (await Service(db).GetBankBookAsync(1, Start, End)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Invalid_date_range_is_rejected()
    {
        await using var db = Database();
        (await Service(db).GetBankBookAsync(1, End, Start)).StatusCode.Should().Be(400);
    }

    private static void Seed(EduOSDbContext db)
    {
        db.Accounts.AddRange(
            new Account { Id=1, TenantId=41, Code="BANK", Name="Bank ledger", AccountType=AccountType.Asset },
            new Account { Id=2, TenantId=41, Code="FEE", Name="Fee income", AccountType=AccountType.Income });
        db.BankAccounts.Add(new BankAccount { Id=1, TenantId=41, AccountId=1,
            BankName="Test Bank", AccountName="Operating", AccountNumber="001", CurrencyCode="BDT" });
    }
    private static void Post(EduOSDbContext db, long id, DateOnly date, JournalState state, decimal amount)
    {
        db.Journals.Add(new Journal { Id=id, TenantId=41, JournalNumber="J-"+id,
            JournalDate=date, AccountingPeriodId=1, State=state, CurrencyCode="BDT" });
        db.JournalLines.Add(new JournalLine { Id=id*10+1, TenantId=41, JournalId=id,
            LineNo=1, AccountId=1, DebitAmount=Math.Max(0m,amount), CreditAmount=Math.Max(0m,-amount) });
        db.JournalLines.Add(new JournalLine { Id=id*10+2, TenantId=41, JournalId=id,
            LineNo=2, AccountId=2, DebitAmount=Math.Max(0m,-amount), CreditAmount=Math.Max(0m,amount) });
    }
    private static AccountingReportingService Service(EduOSDbContext db, string role="TenantAdmin") =>
        new(db, new TestUser(role));
    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("bank-book-" + Guid.NewGuid().ToString("N")).Options, accessor);
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
