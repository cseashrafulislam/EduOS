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

public sealed class InstitutionalCashMovementsTests
{
    private static readonly DateOnly Start = new(2026, 10, 1);
    private static readonly DateOnly End = new(2026, 10, 31);

    [Fact]
    public async Task Cash_and_bank_consolidation_excludes_internal_transfers_and_reconciles_opening()
    {
        await using var db = Database();
        SeedAccounts(db);
        db.BankAccounts.Add(new BankAccount { Id=5, TenantId=41, AccountId=2,
            BankName="Bank A", AccountName="Institution", AccountNumber="1234" });
        AddJournal(db, 10, Start.AddDays(-1), (1,100m,0m), (3,0m,100m));
        AddJournal(db, 11, Start, (1,60m,0m), (3,0m,60m));
        AddJournal(db, 12, Start.AddDays(1), (2,20m,0m), (1,0m,20m));
        AddJournal(db, 13, Start.AddDays(2), (4,15m,0m), (2,0m,15m));
        await db.SaveChangesAsync();

        var report = await Service(db).GetInstitutionalCashMovementsAsync(Start, End);
        report.StatusCode.Should().Be(200);
        report.Data!.Accounts.Should().HaveCount(2);
        report.Data.OpeningBalance.Should().Be(100m);
        report.Data.Receipts.Should().Be(60m);
        report.Data.Payments.Should().Be(15m);
        report.Data.TransfersIn.Should().Be(20m);
        report.Data.TransfersOut.Should().Be(20m);
        report.Data.ClosingBalance.Should().Be(145m);
        report.Data.Accounts.Single(x => x.Kind == "Cash").ClosingBalance.Should().Be(140m);
        report.Data.Accounts.Single(x => x.Kind == "Bank").ClosingBalance.Should().Be(5m);
        report.Data.IsActivityClassified.Should().BeFalse();
    }

    [Fact]
    public async Task Cash_book_requires_explicit_designation_and_tenant_admin()
    {
        await using var db = Database();
        SeedAccounts(db);
        AddJournal(db, 11, Start, (1,25m,0m), (3,0m,25m));
        await db.SaveChangesAsync();

        (await Service(db, false).GetCashBookAsync(1, Start, End)).StatusCode.Should().Be(403);
        (await Service(db).GetCashBookAsync(3, Start, End)).StatusCode.Should().Be(409);
        (await Service(db).GetCashBookAsync(1, End, Start)).StatusCode.Should().Be(400);
        var book = await Service(db).GetCashBookAsync(1, Start, End);
        book.StatusCode.Should().Be(200);
        book.Data!.OpeningBalance.Should().Be(0m);
        book.Data.ClosingBalance.Should().Be(25m);
    }

    [Fact]
    public async Task Mixed_external_cash_journal_is_rejected_instead_of_guessing_allocation()
    {
        await using var db = Database();
        SeedAccounts(db);
        AddJournal(db, 11, Start, (1,50m,0m), (1,0m,20m), (3,0m,30m));
        await db.SaveChangesAsync();
        var report = await Service(db).GetInstitutionalCashMovementsAsync(Start, End);
        report.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Foreign_tenant_cash_movements_never_appear_in_institution_totals()
    {
        var store = "cash-movement-" + Guid.NewGuid().ToString("N");
        await using (var outside = Database(store, 42))
        {
            outside.Accounts.Add(new Account { Id=99, TenantId=42, Code="FOREIGN",
                Name="Other cash", AccountType=AccountType.Asset, IsCashInHand=true });
            AddJournal(outside, 90, Start, (99,900m,0m), (100,0m,900m), tenant:42);
            outside.Accounts.Add(new Account { Id=100, TenantId=42, Code="OTHER",
                Name="Other income", AccountType=AccountType.Income });
            await outside.SaveChangesAsync();
        }
        await using var db = Database(store);
        SeedAccounts(db);
        AddJournal(db, 11, Start, (1,5m,0m), (3,0m,5m));
        await db.SaveChangesAsync();
        var result = await Service(db).GetInstitutionalCashMovementsAsync(Start, End);
        result.StatusCode.Should().Be(200);
        result.Data!.Receipts.Should().Be(5m);
        result.Data.ClosingBalance.Should().Be(5m);
        result.Data.Accounts.Should().ContainSingle();
    }

    private static void SeedAccounts(EduOSDbContext db) => db.Accounts.AddRange(
        new Account { Id=1, TenantId=41, Code="CASH", Name="Cash in hand", AccountType=AccountType.Asset, IsCashInHand=true },
        new Account { Id=2, TenantId=41, Code="BANK", Name="Bank", AccountType=AccountType.Asset },
        new Account { Id=3, TenantId=41, Code="FEE", Name="Fee income", AccountType=AccountType.Income },
        new Account { Id=4, TenantId=41, Code="COST", Name="Cost", AccountType=AccountType.Expense });

    private static void AddJournal(EduOSDbContext db, long id, DateOnly date,
        (long Account, decimal Debit, decimal Credit) first,
        (long Account, decimal Debit, decimal Credit) second,
        (long Account, decimal Debit, decimal Credit)? third = null, long tenant = 41)
    {
        db.Journals.Add(new Journal { Id=id, TenantId=tenant, PublicId=Guid.NewGuid(),
            AccountingPeriodId=1, JournalNumber="J-"+id, JournalDate=date, State=JournalState.Posted });
        var entries = third.HasValue ? new[] { first, second, third.Value } : new[] { first, second };
        for (var i=0; i<entries.Length; i++)
            db.JournalLines.Add(new JournalLine { Id=id*10+i+1, TenantId=tenant,
                JournalId=id, LineNo=i+1, AccountId=entries[i].Account,
                DebitAmount=entries[i].Debit, CreditAmount=entries[i].Credit });
    }

    private static AccountingReportingService Service(EduOSDbContext db, bool admin=true) => new(db, new User(admin));
    private static EduOSDbContext Database(string? name=null, long tenant=41)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenant;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase(name ?? "cash-movement-"+Guid.NewGuid().ToString("N")).Options, accessor);
    }

    private sealed class User(bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Accountant"];
        public bool IsInRole(string role) => admin ? role == "TenantAdmin" : role == "Accountant";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
