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

public sealed class BankReconciliationCandidateTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 28);

    [Fact]
    public async Task Non_administrator_cannot_probe_bank_candidates()
    {
        await using var db = Database();
        (await Service(db, false).GetBankReconciliationCandidatesAsync(1, From, To, null))
            .StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Guessed_or_other_tenant_bank_id_is_concealed()
    {
        await using var db = Database();
        Seed(db);
        await db.SaveChangesAsync();
        (await Service(db).GetBankReconciliationCandidatesAsync(999, From, To, null))
            .StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Only_posted_lines_are_paged_and_filtered_in_sql()
    {
        await using var db = Database();
        Seed(db);
        Post(db, 1, JournalState.Posted, "DEP-01", 100m);
        Post(db, 2, JournalState.Reversed, "DEP-02", 120m);
        Post(db, 3, JournalState.Draft, "DEP-03", 140m);
        await db.SaveChangesAsync();

        var service = Service(db);
        var first = await service.GetBankReconciliationCandidatesAsync(1, From, To, "DEP-", 1, 1);
        first.StatusCode.Should().Be(200);
        first.Data!.TotalCount.Should().Be(2);
        first.Data.Rows.Should().ContainSingle();
        first.Data.Rows[0].JournalLineId.Should().Be(11);
        first.Data.TotalDeposits.Should().Be(220m);

        var second = await service.GetBankReconciliationCandidatesAsync(1, From, To, "DEP-", 2, 1);
        second.Data!.Rows.Should().ContainSingle();
        second.Data.Rows[0].JournalLineId.Should().Be(21);
        (await service.GetBankReconciliationCandidatesAsync(1, From, To, "MISSING", 1, 25))
            .Data!.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Unbalanced_posting_fails_closed_before_candidate_disclosure()
    {
        await using var db = Database();
        Seed(db);
        Post(db, 1, JournalState.Posted, "DEP-01", 100m);
        db.JournalLines.Single(x => x.Id == 12).CreditAmount = 90m;
        await db.SaveChangesAsync();
        (await Service(db).GetBankReconciliationCandidatesAsync(1, From, To, null))
            .StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Invalid_or_unbounded_pages_are_rejected()
    {
        await using var db = Database();
        var service = Service(db);
        (await service.GetBankReconciliationCandidatesAsync(1, From, To, null, 1, 101))
            .StatusCode.Should().Be(400);
        (await service.GetBankReconciliationCandidatesAsync(1, From, To, null, 0, 25))
            .StatusCode.Should().Be(400);
        (await service.GetBankReconciliationCandidatesAsync(1, From, To, new string('X', 101)))
            .StatusCode.Should().Be(400);
    }

    private static void Seed(EduOSDbContext db)
    {
        db.Accounts.AddRange(
            new Account { Id = 1, TenantId = 41, Code = "BANK", Name = "Bank", AccountType = AccountType.Asset },
            new Account { Id = 2, TenantId = 41, Code = "INC", Name = "Income", AccountType = AccountType.Income });
        db.BankAccounts.Add(new BankAccount { Id = 1, TenantId = 41, AccountId = 1,
            BankName = "Institution Bank", AccountName = "Operating", AccountNumber = "001" });
    }

    private static void Post(EduOSDbContext db, long id, JournalState state, string reference, decimal amount)
    {
        db.Journals.Add(new Journal { Id = id, TenantId = 41, JournalNumber = "J-" + id,
            JournalDate = To, AccountingPeriodId = 1, State = state, ReferenceNumber = reference });
        db.JournalLines.AddRange(
            new JournalLine { Id = id * 10 + 1, TenantId = 41, JournalId = id,
                AccountId = 1, LineNo = 1, DebitAmount = amount },
            new JournalLine { Id = id * 10 + 2, TenantId = 41, JournalId = id,
                AccountId = 2, LineNo = 2, CreditAmount = amount });
    }

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("bank-candidates-" + Guid.NewGuid().ToString("N")).Options, accessor);
    }

    private static AccountingReportingService Service(EduOSDbContext db, bool admin = true) =>
        new(db, new TestUser(admin));

    private sealed class TestUser(bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 9;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Accountant"];
        public bool IsInRole(string value) => admin && value == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
