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

public sealed class AccountingLegacyCashDesignationTests
{
    private const string Evidence = "Cash box register and signed cash-count vouchers confirm physical cash custody.";

    [Fact]
    public async Task Historic_cash_approval_requires_separate_checker_and_does_not_edit_journal()
    {
        await using var db = Database();
        Seed(db);
        await db.SaveChangesAsync();
        var command = new SubmitCashLedgerDesignationDto { AccountId=1,
            ClientRequestId=Guid.NewGuid(), Reason=Evidence };
        var maker = Service(db, 10, false);
        var submitted = await maker.SubmitHistoricCashDesignationAsync(command);
        submitted.StatusCode.Should().Be(200);
        (await maker.SubmitHistoricCashDesignationAsync(command)).Data!.Id.Should().Be(submitted.Data!.Id);
        db.Accounts.Single(x => x.Id == 1).IsCashInHand.Should().BeFalse();
        var proposal = db.CashLedgerDesignationRequests.Single();
        proposal.RowVersion = [1, 2, 3];
        var review = new ReviewCashLedgerDesignationDto { Approve=true, ReviewRequestId=Guid.NewGuid(),
            RowVersion=Convert.ToBase64String(proposal.RowVersion) };
        (await Service(db, 10, true).ReviewHistoricCashDesignationAsync(proposal.Id, review)).StatusCode.Should().Be(403);
        db.Accounts.Single(x => x.Id == 1).IsCashInHand.Should().BeFalse();
        var approved = await Service(db, 20, true).ReviewHistoricCashDesignationAsync(proposal.Id, review);
        approved.StatusCode.Should().Be(200);
        approved.Data!.StateCode.Should().Be("Approved");
        (await Service(db, 20, true).ReviewHistoricCashDesignationAsync(proposal.Id, review)).StatusCode.Should().Be(200);
        db.Accounts.Single(x => x.Id == 1).IsCashInHand.Should().BeTrue();
        db.JournalLines.Single().DebitAmount.Should().Be(50m);
        db.Journals.Single().State.Should().Be(JournalState.Posted);
        (await maker.SubmitHistoricCashDesignationAsync(new() { AccountId=1,
            ClientRequestId=Guid.NewGuid(), Reason=Evidence })).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Bank_mapped_account_and_duplicate_pending_designation_are_rejected()
    {
        await using var db = Database();
        Seed(db);
        db.BankAccounts.Add(new BankAccount { Id=8, TenantId=41, AccountId=2,
            BankName="Bank", AccountName="Institute", AccountNumber="42" });
        await db.SaveChangesAsync();
        var service = Service(db, 10, false);
        (await service.SubmitHistoricCashDesignationAsync(new() { AccountId=2,
            ClientRequestId=Guid.NewGuid(), Reason=Evidence })).StatusCode.Should().Be(409);
        var first = await service.SubmitHistoricCashDesignationAsync(new() { AccountId=1,
            ClientRequestId=Guid.NewGuid(), Reason=Evidence });
        first.StatusCode.Should().Be(200);
        (await service.SubmitHistoricCashDesignationAsync(new() { AccountId=1,
            ClientRequestId=Guid.NewGuid(), Reason=Evidence })).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Foreign_tenant_and_accountants_cannot_review_historical_cash()
    {
        await using var db = Database();
        Seed(db);
        await db.SaveChangesAsync();
        var command = new SubmitCashLedgerDesignationDto { AccountId=1,
            ClientRequestId=Guid.NewGuid(), Reason=Evidence };
        (await Service(db, 30, false, tenant:42).SubmitHistoricCashDesignationAsync(command)).StatusCode.Should().Be(403);
        (await Service(db, 30, false, accountant:true).SubmitHistoricCashDesignationAsync(command)).StatusCode.Should().Be(403);
        db.CashLedgerDesignationRequests.Should().BeEmpty();
    }

    private static void Seed(EduOSDbContext db)
    {
        db.Accounts.AddRange(
            new Account { Id=1, TenantId=41, Code="C1", Name="Old cash", AccountType=AccountType.Asset },
            new Account { Id=2, TenantId=41, Code="B1", Name="Bank ledger", AccountType=AccountType.Asset });
        db.Journals.Add(new Journal { Id=5, TenantId=41, AccountingPeriodId=1,
            JournalDate=new DateOnly(2026, 10, 1), JournalNumber="J5", State=JournalState.Posted });
        db.JournalLines.Add(new JournalLine { Id=6, TenantId=41, JournalId=5,
            AccountId=1, LineNo=1, DebitAmount=50m });
    }

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("cash-approval-"+Guid.NewGuid().ToString("N")).Options, accessor);
    }

    private static AccountingSetupService Service(EduOSDbContext db, long id, bool admin,
        long tenant=41, bool accountant=false) => new(db, new User(id, admin, tenant, accountant), TimeProvider.System);

    private sealed class User(long id, bool admin, long tenant, bool accountant) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => id;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : accountant ? ["Accountant"] : ["Principal"];
        public bool IsInRole(string role) => Roles.Contains(role);
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
