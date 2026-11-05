using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Accounting;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class InstitutionalClassifiedCashFlowTests
{
    private static readonly DateOnly From = new(2026,10,1);
    private static readonly DateOnly To = new(2026,10,31);
    private const string Reason = "Finance committee approved GL cash-flow mapping after reviewing supporting bank evidence.";

    [Fact]
    public async Task Approved_effective_dated_rules_classify_cash_without_editing_original_journals()
    {
        await using var db = Database();
        Seed(db);
        db.BankAccounts.Add(new BankAccount { Id=5, TenantId=41, AccountId=2,
            BankName="Bank", AccountName="Institute", AccountNumber="ABCD" });
        Journal(db, 10, From.AddDays(-1), (1,100m,0m), (3,0m,100m));
        Journal(db, 11, From, (1,60m,0m), (3,0m,60m));
        Journal(db, 12, From.AddDays(1), (4,40m,0m), (1,0m,40m));
        Journal(db, 13, From.AddDays(2), (2,10m,0m), (1,0m,10m));
        await db.SaveChangesAsync();
        var report = new AccountingReportingService(db, User(20,"TenantAdmin"));
        var unclassified = await report.GetInstitutionalCashFlowStatementAsync(From, To);
        unclassified.StatusCode.Should().Be(200);
        unclassified.Data!.IsComplete.Should().BeFalse();
        unclassified.Data.OpeningCashAndBank.Should().Be(100m);
        unclassified.Data.ClosingCashAndBank.Should().Be(120m);
        unclassified.Data.UnclassifiedInflows.Should().Be(60m);
        unclassified.Data.UnclassifiedOutflows.Should().Be(40m);
        unclassified.Data.UnclassifiedJournalCount.Should().Be(2);

        var setup = new AccountingSetupService(db, User(10,"Principal"), TimeProvider.System);
        var feeRequest = new SubmitCashFlowRuleDto { AccountId=3, ActivityCode="Operating",
            EffectiveFromDate=From, ClientRequestId=Guid.NewGuid(), Reason=Reason };
        var fee = await setup.SubmitCashFlowRuleAsync(feeRequest);
        fee.StatusCode.Should().Be(200);
        (await setup.SubmitCashFlowRuleAsync(feeRequest)).Data!.Id.Should().Be(fee.Data!.Id);
        (await setup.SubmitCashFlowRuleAsync(new() { AccountId=3, ActivityCode="Investing",
            EffectiveFromDate=From, ClientRequestId=Guid.NewGuid(), Reason=Reason })).StatusCode.Should().Be(409);
        var policy = db.CashFlowActivityRules.Single(x => x.Id == fee.Data.Id);
        policy.RowVersion = [1,2,3];
        var approval = new ReviewCashFlowRuleDto { Approve=true, ReviewRequestId=Guid.NewGuid(),
            RowVersion=Convert.ToBase64String(policy.RowVersion) };
        (await new AccountingSetupService(db, User(10,"TenantAdmin"), TimeProvider.System)
            .ReviewCashFlowRuleAsync(policy.Id, approval)).StatusCode.Should().Be(403);
        var reviewer = new AccountingSetupService(db, User(20,"TenantAdmin"), TimeProvider.System);
        var ok = await reviewer.ReviewCashFlowRuleAsync(policy.Id, approval);
        ok.StatusCode.Should().Be(200);
        (await reviewer.ReviewCashFlowRuleAsync(policy.Id, approval)).StatusCode.Should().Be(200);
        var firstCutoff = ok.Data!.ReviewedAtUtc!.Value.AddTicks(-1);
        (await report.GetInstitutionalCashFlowStatementAsync(From, To, firstCutoff))
            .Data!.UnclassifiedInflows.Should().Be(60m);

        var assetRequest = await setup.SubmitCashFlowRuleAsync(new() { AccountId=4,
            ActivityCode="Investing", EffectiveFromDate=From, ClientRequestId=Guid.NewGuid(), Reason=Reason });
        assetRequest.StatusCode.Should().Be(200);
        var asset = db.CashFlowActivityRules.Single(x => x.Id == assetRequest.Data!.Id);
        asset.RowVersion = [3,4,5];
        (await reviewer.ReviewCashFlowRuleAsync(asset.Id, new ReviewCashFlowRuleDto {
            Approve=true, ReviewRequestId=Guid.NewGuid(), RowVersion=Convert.ToBase64String(asset.RowVersion)
        })).StatusCode.Should().Be(200);
        var final = await report.GetInstitutionalCashFlowStatementAsync(From, To);
        final.StatusCode.Should().Be(200);
        final.Data!.IsComplete.Should().BeTrue();
        final.Data.UnclassifiedJournalCount.Should().Be(0);
        final.Data.Activities.Single(x => x.ActivityCode == "Operating").Inflows.Should().Be(60m);
        final.Data.Activities.Single(x => x.ActivityCode == "Investing").Outflows.Should().Be(40m);
        final.Data.Activities.Single(x => x.ActivityCode == "Financing").Net.Should().Be(0m);
        final.Data.NetCashChange.Should().Be(20m);
        db.Journals.Count().Should().Be(4);
        db.JournalLines.Sum(x => x.DebitAmount).Should().Be(db.JournalLines.Sum(x => x.CreditAmount));
        (await new AccountingReportingService(db, User(10,"Principal"))
            .GetInstitutionalCashFlowStatementAsync(From,To)).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Mixed_counterparty_and_cash_bank_accounts_remain_unclassified_or_rejected()
    {
        await using var db = Database();
        Seed(db);
        db.BankAccounts.Add(new BankAccount { Id=5,TenantId=41,AccountId=2,
            BankName="Bank",AccountName="Institute",AccountNumber="ABCD" });
        Journal(db, 11, From, (1,100m,0m), (3,0m,120m), (4,20m,0m));
        await db.SaveChangesAsync();
        var setup = new AccountingSetupService(db, User(10,"Principal"), TimeProvider.System);
        (await setup.SubmitCashFlowRuleAsync(new() { AccountId=1, ActivityCode="Operating",
            EffectiveFromDate=From, ClientRequestId=Guid.NewGuid(), Reason=Reason })).StatusCode.Should().Be(409);
        (await setup.SubmitCashFlowRuleAsync(new() { AccountId=2, ActivityCode="Investing",
            EffectiveFromDate=From, ClientRequestId=Guid.NewGuid(), Reason=Reason })).StatusCode.Should().Be(409);
        var report = await new AccountingReportingService(db,User(20,"TenantAdmin"))
            .GetInstitutionalCashFlowStatementAsync(From,To);
        report.StatusCode.Should().Be(200);
        report.Data!.IsComplete.Should().BeFalse();
        report.Data.UnclassifiedInflows.Should().Be(100m);
        report.Data.UnclassifiedJournalCount.Should().Be(1);
    }

    [Fact]
    public async Task Compound_journal_needs_exact_idempotent_independently_approved_allocation()
    {
        await using var db = Database();
        Seed(db);
        Journal(db, 31, From, (1,100m,0m), (3,0m,120m), (4,20m,0m));
        await db.SaveChangesAsync();
        var report = new AccountingReportingService(db, User(20,"TenantAdmin"));
        var before = await report.GetInstitutionalCashFlowStatementAsync(From,To);
        before.Data!.UnclassifiedJournalCount.Should().Be(1);
        before.Data.Exceptions[0].CounterpartyAccountIds.Should().BeEquivalentTo(new long[] {3,4});
        var maker = new AccountingSetupService(db, User(10,"Principal"), TimeProvider.System);
        var request = new SubmitCashFlowJournalAllocationDto { JournalId=31, OperatingAmount=70m,
            InvestingAmount=30m, FinancingAmount=0m, ClientRequestId=Guid.NewGuid(), Reason=Reason };
        (await maker.SubmitCashFlowJournalAllocationAsync(new() { JournalId=31, OperatingAmount=99m,
            ClientRequestId=Guid.NewGuid(), Reason=Reason })).StatusCode.Should().Be(409);
        var submitted = await maker.SubmitCashFlowJournalAllocationAsync(request);
        submitted.StatusCode.Should().Be(200);
        (await maker.SubmitCashFlowJournalAllocationAsync(request)).Data!.Id.Should().Be(submitted.Data!.Id);
        (await maker.SubmitCashFlowJournalAllocationAsync(new() { JournalId=31, OperatingAmount=100m,
            ClientRequestId=Guid.NewGuid(), Reason=Reason })).StatusCode.Should().Be(409);
        (await report.GetInstitutionalCashFlowStatementAsync(From,To)).Data!.IsComplete.Should().BeFalse();
        var pending = db.CashFlowJournalAllocations.Single();
        pending.RowVersion = [7,8,9];
        var review = new ReviewCashFlowRuleDto { Approve=true, ReviewRequestId=Guid.NewGuid(),
            RowVersion=Convert.ToBase64String(pending.RowVersion) };
        (await new AccountingSetupService(db, User(10,"TenantAdmin"), TimeProvider.System)
            .ReviewCashFlowJournalAllocationAsync(pending.Id, review)).StatusCode.Should().Be(403);
        var checker = new AccountingSetupService(db, User(20,"TenantAdmin"), TimeProvider.System);
        var approved = await checker.ReviewCashFlowJournalAllocationAsync(pending.Id,review);
        approved.StatusCode.Should().Be(200);
        (await checker.ReviewCashFlowJournalAllocationAsync(pending.Id,review)).StatusCode.Should().Be(200);
        var beforeApproval = approved.Data!.ReviewedAtUtc!.Value.AddTicks(-1);
        (await report.GetInstitutionalCashFlowStatementAsync(From,To,beforeApproval)).Data!.IsComplete.Should().BeFalse();
        var after = await report.GetInstitutionalCashFlowStatementAsync(From,To);
        after.StatusCode.Should().Be(200);
        after.Data!.IsComplete.Should().BeTrue();
        after.Data.Activities.Single(x => x.ActivityCode == "Operating").Inflows.Should().Be(70m);
        after.Data.Activities.Single(x => x.ActivityCode == "Investing").Inflows.Should().Be(30m);
        after.Data.NetCashChange.Should().Be(100m);
        db.Journals.Count().Should().Be(1);
        db.JournalLines.Count().Should().Be(3);
        db.JournalLines.Single(x => x.Id == 312).CreditAmount = 121m;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        (await report.GetInstitutionalCashFlowStatementAsync(From,To)).Data!.IsComplete.Should().BeTrue();
    }

    private static void Seed(EduOSDbContext db)
    {
        db.Accounts.AddRange(
            new Account { Id=1,TenantId=41,Code="CASH",Name="Cash",AccountType=AccountType.Asset,IsCashInHand=true },
            new Account { Id=2,TenantId=41,Code="BANK",Name="Bank",AccountType=AccountType.Asset },
            new Account { Id=3,TenantId=41,Code="FEE",Name="Fee income",AccountType=AccountType.Income },
            new Account { Id=4,TenantId=41,Code="ASSET",Name="Asset",AccountType=AccountType.Asset });
        db.Users.AddRange(new ApplicationUser { Id=10,IsActive=true,UserName="maker" },
            new ApplicationUser { Id=20,IsActive=true,UserName="reviewer" });
        db.TenantMemberships.AddRange(new TenantMembership { TenantId=41,UserId=10,Status=MembershipStatus.Active },
            new TenantMembership { TenantId=41,UserId=20,Status=MembershipStatus.Active });
        db.Roles.AddRange(new ApplicationRole { Id=101,TenantId=41,Name="Principal",NormalizedName="PRINCIPAL",IsActive=true },
            new ApplicationRole { Id=102,TenantId=41,Name="TenantAdmin",NormalizedName="TENANTADMIN",IsActive=true });
        db.UserRoles.AddRange(new IdentityUserRole<long> { UserId=10, RoleId=101 },
            new IdentityUserRole<long> { UserId=20, RoleId=102 });
    }

    private static void Journal(EduOSDbContext db, long id, DateOnly date,
        (long Account,decimal Debit,decimal Credit) first,
        (long Account,decimal Debit,decimal Credit) second,
        (long Account,decimal Debit,decimal Credit)? third=null)
    {
        db.Journals.Add(new Journal { Id=id,TenantId=41,AccountingPeriodId=1,
            JournalNumber="J-"+id,JournalDate=date,State=JournalState.Posted });
        var lines = third.HasValue ? new[] {first,second,third.Value} : new[] {first,second};
        for (var i=0;i<lines.Length;i++)
            db.JournalLines.Add(new JournalLine { Id=id*10+i+1,TenantId=41,JournalId=id,
                LineNo=i+1,AccountId=lines[i].Account,DebitAmount=lines[i].Debit,CreditAmount=lines[i].Credit });
    }

    private static EduOSDbContext Database()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 41L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("cash-flow-"+Guid.NewGuid().ToString("N")).Options, accessor);
    }

    private static UserContext User(long id,string role) => new(id,role);
    private sealed class UserContext(long id,string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => id;
        public long TenantId => 41;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string name) => name == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
