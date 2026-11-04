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

public sealed class AccountingBankLedgerIntegrityTests
{
    [Fact]
    public async Task Mapping_same_live_ledger_to_second_bank_is_rejected()
    {
        await using var db = Database();
        Seed(db);
        await db.SaveChangesAsync();
        var result = await Service(db).SaveBankAccountAsync(null, Request(1));
        result.StatusCode.Should().Be(409);
        result.Message.Should().Contain("already mapped");
        db.BankAccounts.Count().Should().Be(1);
    }

    [Fact]
    public async Task Bank_remapping_with_historical_gl_line_is_rejected_without_mutation()
    {
        await using var db = Database();
        Seed(db);
        db.Journals.Add(new Journal { Id=10, TenantId=41, AccountingPeriodId=1,
            JournalDate=new DateOnly(2026, 10, 1), JournalNumber="J-10", State=JournalState.Posted });
        db.JournalLines.Add(new JournalLine { Id=11, TenantId=41, JournalId=10, LineNo=1,
            AccountId=1, DebitAmount=100m });
        await db.SaveChangesAsync();
        var current = db.BankAccounts.Single();
        var result = await Service(db).SaveBankAccountAsync(current.Id, Request(2,
            Convert.ToBase64String(current.RowVersion)));
        result.StatusCode.Should().Be(409);
        result.Message.Should().Contain("ledger history");
        db.BankAccounts.Single().AccountId.Should().Be(1);
    }

    private static SaveBankAccountRequestDto Request(long accountId, string? version=null) => new()
    {
        AccountId=accountId, BankName="Bank", AccountName="School", AccountNumber="123",
        CurrencyCode="BDT", IsActive=true, RowVersion=version
    };

    private static void Seed(EduOSDbContext db)
    {
        db.Accounts.AddRange(
            new Account { Id=1, TenantId=41, Code="B1", Name="Bank A", AccountType=AccountType.Asset },
            new Account { Id=2, TenantId=41, Code="B2", Name="Bank B", AccountType=AccountType.Asset });
        db.BankAccounts.Add(new BankAccount { Id=5, TenantId=41, AccountId=1,
            BankName="Bank A", AccountName="School", AccountNumber="123", RowVersion=[1,2,3] });
    }

    private static AccountingSetupService Service(EduOSDbContext db) => new(db, new User(), TimeProvider.System);

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("bank-ledger-"+Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options, accessor);
    }

    private sealed class User : ICurrentUserService
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
