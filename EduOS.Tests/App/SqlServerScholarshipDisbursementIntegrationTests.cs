using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Finance;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerScholarshipDisbursementIntegrationTests
{
    [Fact]
    public async Task Cash_grant_posts_exactly_once_and_separate_reviewer_reverses_with_new_balanced_GL()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        long tenantId, makerId, checkerId, outsiderId, expenseId, bankLedgerId, bankId, periodId;
        Guid applicationRef;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-CASH-" + suffix, Name = "Cash Scholarship QA",
                Email = "cash-" + suffix + "@example.test", State = TenantState.Active };
            var other = new Tenant { Code = "CI-CASO-" + suffix, Name = "Other School QA",
                Email = "other-cash-" + suffix + "@example.test", State = TenantState.Active };
            var person = new Person { FullName = "Cash Grant Student" };
            var actors = Enumerable.Range(0, 3).Select(i =>
            {
                var email = $"cash-{i}-{suffix}@example.test";
                return new ApplicationUser { UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                    Email = email, NormalizedEmail = email.ToUpperInvariant(), IsActive = true,
                    SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString() };
            }).ToArray();
            setup.AddRange(tenant, other, person); setup.Users.AddRange(actors);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; makerId = actors[0].Id; checkerId = actors[1].Id;
            outsiderId = actors[2].Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Name = "QA Campus", Code = "CASH-" + suffix };
                var year = new AcademicYear { TenantId = tenantId, Name = "2026", Code = "Y" + suffix,
                    StartDate = today.AddDays(-365), EndDate = today.AddDays(365) };
                var program = new AcademicProgram { TenantId = tenantId, Name = "Secondary", Code = "P" + suffix };
                var student = new Student { TenantId = tenantId, PersonId = person.Id,
                    FullName = "Cash Grant Student", StudentCode = "CASH-" + suffix,
                    StatusCode = "Active", AdmissionDate = today.AddDays(-20) };
                var expense = new Account { TenantId = tenantId, Code = "EXP-" + suffix,
                    Name = "Scholarship expense", AccountType = AccountType.Expense,
                    IsActive = true, AllowsPosting = true, CurrencyCode = "BDT" };
                var bankLedger = new Account { TenantId = tenantId, Code = "BANK-" + suffix,
                    Name = "Scholarship bank", AccountType = AccountType.Asset,
                    IsActive = true, AllowsPosting = true, CurrencyCode = "BDT" };
                var period = new AccountingPeriod { TenantId = tenantId, Name = "Cash QA " + suffix,
                    StartDate = today.AddDays(-15), EndDate = today.AddDays(15), IsClosed = false };
                var role = new ApplicationRole { TenantId = tenantId,
                    Name = "TenantAdmin", NormalizedName = "TENANTADMIN", IsActive = true };
                setup.AddRange(campus, year, program, student, expense, bankLedger, period, role);
                await setup.SaveChangesAsync();
                expenseId = expense.Id; bankLedgerId = bankLedger.Id; periodId = period.Id;
                var level = new AcademicLevel { TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Class IX", Code = "L" + suffix, LevelNo = 9 };
                var curriculum = new AcademicCurriculum { TenantId = tenantId,
                    AcademicProgramId = program.Id, Name = "2026", Code = "CUR" + suffix,
                    EffectiveFrom = today.AddDays(-365), IsCurrent = true };
                var bank = new BankAccount { TenantId = tenantId, CampusId = campus.Id,
                    AccountId = bankLedger.Id, BankName = "QA Bank", AccountName = "Education Aid",
                    AccountNumber = "TEST-" + suffix, IsActive = true, CurrencyCode = "BDT" };
                setup.AddRange(level, curriculum, bank);
                await setup.SaveChangesAsync();
                bankId = bank.Id;
                var batch = new AcademicBatch { TenantId = tenantId, CampusId = campus.Id,
                    AcademicYearId = year.Id, AcademicProgramId = program.Id,
                    AcademicLevelId = level.Id, Name = "IX", Code = "B" + suffix, Capacity = 40 };
                setup.AcademicBatches.Add(batch);
                await setup.SaveChangesAsync();
                var enrollment = new StudentEnrollment { TenantId = tenantId,
                    StudentId = student.Id, CampusId = campus.Id, AcademicYearId = year.Id,
                    AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                    AcademicCurriculumId = curriculum.Id, AcademicBatchId = batch.Id,
                    EnrollmentDate = today.AddDays(-15), RollNo = "01", IsCurrent = true,
                    State = EnrollmentState.Active, ClientRequestId = Guid.NewGuid() };
                setup.StudentEnrollments.Add(enrollment);
                await setup.SaveChangesAsync();
                var aid = new ScholarshipApplication { TenantId = tenantId,
                    StudentEnrollmentId = enrollment.Id, ClientRequestId = Guid.NewGuid(),
                    AwardMode = "CashGrant", CashAmount = 125.25m,
                    EffectiveFrom = today.AddDays(-2), EffectiveTo = today.AddDays(2),
                    State = "Approved", EligibilityStatement = "Verified financial need", 
                    SubmittedByUserId = outsiderId, ReviewedByUserId = checkerId,
                    ReviewedAtUtc = DateTime.UtcNow };
                setup.ScholarshipApplications.Add(aid);
                foreach (var actor in actors.Take(2))
                {
                    setup.TenantMemberships.Add(new TenantMembership { TenantId = tenantId,
                        UserId = actor.Id, Status = MembershipStatus.Active });
                    setup.UserRoles.Add(new IdentityUserRole<long> { UserId = actor.Id, RoleId = role.Id });
                }
                await setup.SaveChangesAsync();
                applicationRef = aid.PublicId;
            }
        }

        await using var db = new EduOSDbContext(options, Accessor(tenantId));
        var maker = new ScholarshipDisbursementService(db, new Actor(tenantId, makerId));
        var checker = new ScholarshipDisbursementService(db, new Actor(tenantId, checkerId));
        var denied = new ScholarshipDisbursementService(db, new Actor(tenantId + 1, outsiderId));
        var version = Convert.ToBase64String((await db.ScholarshipApplications.SingleAsync(x =>
            x.PublicId == applicationRef)).RowVersion);
        var attempt = new PrepareScholarshipDisbursementRequest
        {
            RequestId = Guid.NewGuid(), RowVersion = version, Method = "Bank",
            ExpenseAccountId = expenseId, PaymentAccountId = expenseId, BankAccountId = bankId,
            AccountingPeriodId = periodId, PaymentDate = today,
            ExternalPaymentReference = "CI-BANK-" + suffix
        };
        Assert.Equal(403, (await denied.PrepareAsync(applicationRef, attempt)).StatusCode);
        Assert.Equal(409, (await maker.PrepareAsync(applicationRef, attempt)).StatusCode);
        Assert.Equal(0, await db.Journals.CountAsync());

        attempt.PaymentAccountId = bankLedgerId;
        var rejectedInstruction = await maker.PrepareAsync(applicationRef, attempt);
        Assert.True(rejectedInstruction.Success, rejectedInstruction.Message);
        var rejectRequest = new ReviewScholarshipDisbursementRequest
        {
            RequestId = Guid.NewGuid(), RowVersion = rejectedInstruction.Data!.RowVersion,
            ConfirmPaid = false, Reason = "Bank transfer reference could not be verified"
        };
        var rejected = await checker.ReviewAsync(applicationRef, rejectRequest);
        Assert.True(rejected.Success, rejected.Message);
        Assert.Equal("Rejected", rejected.Data!.State);
        var rejectedReplay = await checker.ReviewAsync(applicationRef, rejectRequest);
        Assert.True(rejectedReplay.Success, rejectedReplay.Message);
        db.ChangeTracker.Clear();
        Assert.Equal("Rejected", (await db.ScholarshipDisbursements.AsNoTracking()
            .SingleAsync(x => x.PublicId == rejected.Data.Reference)).State);
        Assert.Equal(0, await db.Journals.CountAsync());
        attempt.RequestId = Guid.NewGuid();
        attempt.ExternalPaymentReference = "CI-BANK-VERIFIED-" + suffix;
        var prepared = await maker.PrepareAsync(applicationRef, attempt);
        Assert.True(prepared.Success, prepared.Message);
        Assert.Equal("Submitted", prepared.Data!.State);
        Assert.True((await maker.PrepareAsync(applicationRef, attempt)).Success);
        var review = new ReviewScholarshipDisbursementRequest { RequestId = Guid.NewGuid(),
            RowVersion = prepared.Data.RowVersion, ConfirmPaid = true,
            Reason = "Independent bank reference and payee verified" };
        Assert.Equal(409, (await maker.ReviewAsync(applicationRef, review)).StatusCode);
        Assert.Equal(0, await db.Journals.CountAsync());
        var posted = await checker.ReviewAsync(applicationRef, review);
        Assert.True(posted.Success, posted.Message);
        Assert.Equal("Posted", posted.Data!.State);
        db.ChangeTracker.Clear();
        Assert.Equal("Posted", (await db.ScholarshipDisbursements.AsNoTracking()
            .SingleAsync(x => x.PublicId == posted.Data.Reference)).State);
        Assert.Equal("Awarded", (await db.ScholarshipApplications.AsNoTracking()
            .SingleAsync(x => x.PublicId == applicationRef)).State);
        var postedReplay = await checker.ReviewAsync(applicationRef, review);
        Assert.True(postedReplay.Success, postedReplay.Message);
        db.ChangeTracker.Clear();
        var journal = Assert.Single(await db.Journals.Where(x => x.SourceType == "ScholarshipDisbursement").ToListAsync());
        var lines = await db.JournalLines.Where(x => x.JournalId == journal.Id).OrderBy(x => x.LineNo).ToArrayAsync();
        Assert.Equal(2, lines.Length);
        Assert.Equal((125.25m, 0m), (lines[0].DebitAmount, lines[0].CreditAmount));
        Assert.Equal((0m, 125.25m), (lines[1].DebitAmount, lines[1].CreditAmount));
        Assert.Equal(expenseId, lines[0].AccountId);
        Assert.Equal(bankLedgerId, lines[1].AccountId);
        Assert.Equal("Awarded", (await db.ScholarshipApplications.SingleAsync(x =>
            x.PublicId == applicationRef)).State);

        var reversal = new ReverseScholarshipDisbursementRequest
        {
            RequestId = Guid.NewGuid(), RowVersion = posted.Data.RowVersion,
            AccountingPeriodId = periodId, ReversalDate = today,
            ExternalReversalReference = "CI-BANK-REFUND-" + suffix,
            Reason = "External bank return was received"
        };
        var requested = await maker.RequestReversalAsync(applicationRef, reversal);
        Assert.True(requested.Success, requested.Message);
        Assert.Equal("ReversalRequested", requested.Data!.State);
        db.ChangeTracker.Clear();
        Assert.Equal("ReversalRequested", (await db.ScholarshipDisbursements.AsNoTracking()
            .SingleAsync(x => x.PublicId == posted.Data.Reference)).State);
        var confirm = new ReviewScholarshipReversalRequest { RequestId = Guid.NewGuid(),
            RowVersion = requested.Data.RowVersion, ConfirmReversed = true,
            Reason = "Verified bank reversal reference" };
        Assert.Equal(409, (await maker.ReviewReversalAsync(applicationRef, confirm)).StatusCode);
        var reversed = await checker.ReviewReversalAsync(applicationRef, confirm);
        Assert.True(reversed.Success, reversed.Message);
        Assert.Equal("Reversed", reversed.Data!.State);
        db.ChangeTracker.Clear();
        var reversalJournal = Assert.Single(await db.Journals.Where(x =>
            x.SourceType == "ScholarshipDisbursementReversal").ToListAsync());
        Assert.Equal(journal.Id, reversalJournal.ReversalOfJournalId);
        var allLines = await db.JournalLines.Where(x =>
            x.JournalId == journal.Id || x.JournalId == reversalJournal.Id).ToArrayAsync();
        Assert.Equal(250.50m, allLines.Sum(x => x.DebitAmount));
        Assert.Equal(250.50m, allLines.Sum(x => x.CreditAmount));
        Assert.Equal("Cancelled", (await db.ScholarshipApplications.SingleAsync(x =>
            x.PublicId == applicationRef)).State);
        Assert.Equal(0, await db.StudentDiscounts.CountAsync());
        Assert.Equal(0, await db.StudentPayments.CountAsync());
    }

    private static IHttpContextAccessor Accessor(long tenantId)
    {
        var result = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        result.HttpContext.Items["TenantId"] = tenantId;
        return result;
    }
    private sealed class Actor(long tenant, long actor) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => actor;
        public long TenantId => tenant;
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
