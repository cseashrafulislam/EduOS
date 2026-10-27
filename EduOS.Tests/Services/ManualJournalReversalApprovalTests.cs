using EduOS.Core.DTOs.Accounting;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Accounting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ManualJournalReversalApprovalTests
{
    [Fact]
    public async Task Reversal_request_creates_submitted_unposted_compensating_lines()
    {
        await using var db = Database();
        var original = await SeedPostedManualAsync(db);
        var request = Request(original);
        var result = await Service(db, 9).ReverseJournalAsync(original.PublicId, request);

        Assert.True(result.Success, result.Message);
        Assert.Equal(JournalState.Submitted, result.Data!.State);
        Assert.Null(result.Data.ApprovedAt);
        Assert.Null(result.Data.PostedAt);
        Assert.Equal(original.Id, result.Data.ReversalOfJournalId);
        Assert.Equal(2, await db.Journals.CountAsync());
        Assert.Equal(JournalState.Posted, original.State);

        var lines = await db.JournalLines.Where(x => x.JournalId == result.Data.Id)
            .OrderBy(x => x.LineNo).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Equal(125.50m, lines[0].CreditAmount);
        Assert.Equal(125.50m, lines[1].DebitAmount);
        Assert.Equal(0m, lines.Sum(x => x.DebitAmount - x.CreditAmount));
    }

    [Fact]
    public async Task Maker_cannot_approve_and_checker_must_approve_before_posting()
    {
        await using var db = Database();
        var original = await SeedPostedManualAsync(db);
        var proposed = (await Service(db, 9).ReverseJournalAsync(original.PublicId, Request(original))).Data!;
        var pending = await db.Journals.SingleAsync(x => x.Id == proposed.Id);
        pending.RowVersion = [2];
        await db.SaveChangesAsync();

        var version = Convert.ToBase64String(pending.RowVersion);
        var selfReview = await Service(db, 9).ReviewJournalAsync(pending.PublicId,
            new ReviewJournalRequestDto { Approve = true, RowVersion = version });
        Assert.Equal(403, selfReview.StatusCode);

        var earlyPost = await Service(db, 10).PostJournalAsync(pending.PublicId,
            new PostJournalRequestDto { RowVersion = version });
        Assert.Equal(409, earlyPost.StatusCode);

        var approved = await Service(db, 10).ReviewJournalAsync(pending.PublicId,
            new ReviewJournalRequestDto { Approve = true, RowVersion = version });
        Assert.True(approved.Success, approved.Message);
        Assert.Equal(JournalState.Approved, approved.Data!.State);
        var posted = await Service(db, 10).PostJournalAsync(pending.PublicId,
            new PostJournalRequestDto { RowVersion = approved.Data.RowVersion });
        Assert.True(posted.Success, posted.Message);
        Assert.Equal(JournalState.Posted, posted.Data!.State);
        Assert.Equal(JournalState.Posted, original.State);
    }

    [Fact]
    public async Task Changed_reversal_amount_cannot_be_approved()
    {
        await using var db = Database();
        var original = await SeedPostedManualAsync(db);
        var proposed = (await Service(db, 9).ReverseJournalAsync(original.PublicId, Request(original))).Data!;
        var pending = await db.Journals.SingleAsync(x => x.Id == proposed.Id);
        pending.RowVersion = [2];
        var line = await db.JournalLines.FirstAsync(x => x.JournalId == proposed.Id && x.LineNo == 1);
        line.CreditAmount = 100m;
        await db.SaveChangesAsync();

        var result = await Service(db, 10).ReviewJournalAsync(pending.PublicId,
            new ReviewJournalRequestDto { Approve = true,
                RowVersion = Convert.ToBase64String(pending.RowVersion) });
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(JournalState.Submitted, pending.State);
    }

    [Fact]
    public async Task Exact_request_retry_returns_same_unposted_proposal()
    {
        await using var db = Database();
        var original = await SeedPostedManualAsync(db);
        var request = Request(original);
        var first = await Service(db, 9).ReverseJournalAsync(original.PublicId, request);
        var replay = await Service(db, 9).ReverseJournalAsync(original.PublicId, request);
        Assert.True(first.Success);
        Assert.True(replay.Success);
        Assert.Equal(first.Data!.Id, replay.Data!.Id);
        Assert.Equal(2, await db.Journals.CountAsync());
    }

    [Fact]
    public async Task Reversal_date_before_original_is_rejected()
    {
        await using var db = Database();
        var original = await SeedPostedManualAsync(db);
        var request = Request(original);
        request.ReversalDate = original.JournalDate.AddDays(-1);
        var result = await Service(db, 9).ReverseJournalAsync(original.PublicId, request);
        Assert.Equal(409, result.StatusCode);
        Assert.Single(db.Journals);
    }

    private static ReverseJournalRequestDto Request(Journal original) => new()
    {
        ClientRequestId = Guid.NewGuid(), ReversalDate = original.JournalDate.AddDays(1),
        Reason = "Correct documented accounting error",
        RowVersion = Convert.ToBase64String(original.RowVersion)
    };

    private static async Task<Journal> SeedPostedManualAsync(EduOSDbContext db)
    {
        var period = new AccountingPeriod { TenantId = 41, Name = "FY 2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
        var cash = new Account { TenantId = 41, Code = "1000", Name = "Cash", AccountType = AccountType.Asset };
        var income = new Account { TenantId = 41, Code = "4000", Name = "Income", AccountType = AccountType.Income };
        db.AccountingPeriods.Add(period);
        db.Accounts.AddRange(cash, income);
        await db.SaveChangesAsync();
        var journal = new Journal { TenantId = 41, PublicId = Guid.NewGuid(),
            JournalNumber = "JRN-ORIGINAL", AccountingPeriodId = period.Id,
            JournalDate = new DateOnly(2026, 3, 15), CurrencyCode = "BDT",
            SourceType = "Manual", State = JournalState.Posted, CreatedBy = 7,
            RowVersion = [1] };
        db.Journals.Add(journal);
        await db.SaveChangesAsync();
        db.JournalLines.AddRange(
            new JournalLine { TenantId = 41, JournalId = journal.Id, LineNo = 1,
                AccountId = cash.Id, DebitAmount = 125.50m },
            new JournalLine { TenantId = 41, JournalId = journal.Id, LineNo = 2,
                AccountId = income.Id, CreditAmount = 125.50m });
        await db.SaveChangesAsync();
        return journal;
    }

    private static AccountingPostingService Service(EduOSDbContext db, long id) =>
        new(db, new User(id), TimeProvider.System);

    private static EduOSDbContext Database()
    {
        var context = new DefaultHttpContext();
        context.Items["TenantId"] = 41L;
        // Match the authenticated request identity used by DbContext audit attribution.
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "9")], "TestAuthentication"));
        var accessor = new HttpContextAccessor { HttpContext = context };
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("manual-reversal-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options, accessor);
    }

    private sealed class User(long id) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => id;
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
