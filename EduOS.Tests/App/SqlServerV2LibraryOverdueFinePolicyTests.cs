using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Library;
using EduOS.Core.Enums.Domain;
using EduOS.Service.Services.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    private sealed class FutureLibraryClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }

    [Fact]
    public async Task V2_library_sql_overdue_policy_is_effective_at_issue_and_second_admin_posts_snapshot()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var clock = new FutureLibraryClock(new DateTimeOffset(today.AddDays(18).ToDateTime(TimeOnly.MinValue),
            TimeSpan.Zero));
        Guid issueReference;
        long campusId, secondUserId, headId, periodId, studentId, copyId;
        await using (var seed = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var suffix = Guid.NewGuid().ToString("N")[..10];
            var enrollment = await seed.StudentEnrollments.AsNoTracking().SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.PublicId == fixture.Enrollments[0]);
            campusId = enrollment.CampusId; studentId = enrollment.StudentId;
            var second = new ApplicationUser
            {
                UserName = "fine-review-" + suffix,
                NormalizedUserName = ("fine-review-" + suffix).ToUpperInvariant(),
                Email = "fine-review-" + suffix + "@example.test",
                NormalizedEmail = ("fine-review-" + suffix + "@example.test").ToUpperInvariant(),
                FullName = "Library fine reviewer", EmailConfirmed = true, IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            var branch = new LibraryBranch { TenantId = fixture.TenantId,
                CampusId = campusId, Code = "FIN-" + suffix, Name = "Fine branch" };
            var book = new Book { TenantId = fixture.TenantId, Title = "Overdue science textbook" };
            var receivable = new Account { TenantId = fixture.TenantId,
                Code = "FIN-R-" + suffix, Name = "Late-return receivable", AccountType = AccountType.Asset };
            var income = new Account { TenantId = fixture.TenantId,
                Code = "FIN-I-" + suffix, Name = "Late-return income", AccountType = AccountType.Income };
            var period = new AccountingPeriod { TenantId = fixture.TenantId,
                Name = "Fine period " + suffix, StartDate = today.AddDays(16), EndDate = today.AddDays(22) };
            seed.AddRange(second, branch, book, receivable, income, period);
            await seed.SaveChangesAsync();
            secondUserId = second.Id; periodId = period.Id;
            var head = new FeeHead { TenantId = fixture.TenantId,
                Code = "LIB-FINE-" + suffix, Name = "Overdue book income",
                IncomeAccountId = income.Id, ReceivableAccountId = receivable.Id,
                DefaultFrequency = FeeFrequencyType.OneTime };
            seed.FeeHeads.Add(head);
            await seed.SaveChangesAsync(); headId = head.Id;
            var copy = new BookCopy { TenantId = fixture.TenantId, BookId = book.Id,
                LibraryBranchId = branch.Id, AccessionNumber = "FIN-COPY-" + suffix,
                State = BookCopyState.Available };
            seed.BookCopies.Add(copy);
            await seed.SaveChangesAsync();
            copyId = copy.Id;
        }
        await using (var maker = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var service = new LibraryLiabilityService(maker,
                new DigitalReader(fixture.TenantId, fixture.UserId, "TenantAdmin"),
                TimeProvider.System, NullLogger<LibraryLiabilityService>.Instance);
            var policy = await service.CreateFinePolicyAsync(new SaveLibraryFinePolicyDto
            {
                ClientRequestId = Guid.NewGuid(), CampusId = campusId,
                EffectiveFrom = today, DailyRate = 5.50m, GraceDays = 2, MaximumCharge = 100m
            });
            Assert.True(policy.Success, policy.Message);
            var invalid = await service.CreateFinePolicyAsync(new SaveLibraryFinePolicyDto
            {
                ClientRequestId = Guid.NewGuid(), CampusId = campusId,
                EffectiveFrom = today.AddDays(-1), DailyRate = 10m
            });
            Assert.Equal(409, invalid.StatusCode);
            var issue = new BookIssue { TenantId = fixture.TenantId,
                ClientRequestId = Guid.NewGuid(), BookCopyId = copyId,
                StudentId = studentId, IssueDate = today.AddDays(1),
                DueDate = today.AddDays(5), ReturnDate = today.AddDays(15),
                State = BookIssueState.Returned, IssuedByUserId = fixture.UserId };
            maker.BookIssues.Add(issue);
            await maker.SaveChangesAsync(); issueReference = issue.PublicId;
        }
        Guid chargeReference;
        string chargeVersion;
        await using (var maker = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var service = new LibraryLiabilityService(maker,
                new DigitalReader(fixture.TenantId, fixture.UserId, "TenantAdmin"),
                clock, NullLogger<LibraryLiabilityService>.Instance);
            var candidates = await service.OverdueCandidatesAsync(null, campusId, 1, 20);
            Assert.True(candidates.Success, candidates.Message);
            var candidate = Assert.Single(candidates.Data!.Items, x => x.IssueReference == issueReference);
            Assert.Equal(44m, candidate.CalculatedAmount);
            Assert.Equal(10, candidate.OverdueDays);
            Assert.Equal(409, (await service.AssessAsync(new AssessLibraryLiabilityRequestDto
            {
                ClientRequestId = Guid.NewGuid(), IssueReference = issueReference,
                Amount = 45m, Reason = "Incorrect rounding"
            })).StatusCode);
            var assessed = await service.AssessAsync(new AssessLibraryLiabilityRequestDto
            {
                ClientRequestId = Guid.NewGuid(), IssueReference = issueReference,
                Amount = 44m, Reason = "Overdue 10 days under issue-date policy"
            });
            Assert.True(assessed.Success, assessed.Message);
            chargeReference = assessed.Data!.Reference;
            chargeVersion = assessed.Data.RowVersion;
            Assert.Equal(44m, assessed.Data.Amount);
            Assert.Equal(10, assessed.Data.OverdueDays);
            Assert.Equal(5.50m, assessed.Data.DailyRate);
        }
        await using (var reviewer = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var service = new LibraryLiabilityService(reviewer,
                new DigitalReader(fixture.TenantId, secondUserId, "TenantAdmin"),
                clock, NullLogger<LibraryLiabilityService>.Instance);
            var posted = await service.ReviewAsync(chargeReference, new ReviewLibraryLiabilityRequestDto
            { RowVersion = chargeVersion, AccountingPeriodId = periodId, FeeHeadId = headId });
            Assert.True(posted.Success, posted.Message);
            Assert.Equal(LibraryLiabilityState.Posted, posted.Data!.State);
        }
        await using var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var charge = await verify.LibraryLiabilities.SingleAsync(x => x.TenantId == fixture.TenantId &&
            x.PublicId == chargeReference);
        Assert.NotNull(charge.LibraryFinePolicyId);
        Assert.Equal(2, charge.GraceDaysSnapshot);
        Assert.Equal(10, charge.OverdueDaysSnapshot);
        var invoice = await verify.StudentInvoices.SingleAsync(x => x.TenantId == fixture.TenantId &&
            x.Id == charge.StudentInvoiceId);
        Assert.Equal(44m, invoice.TotalAmount);
        Assert.Equal(44m, invoice.DueAmount);
        var lines = await verify.JournalLines.Where(x => x.TenantId == fixture.TenantId &&
            x.JournalId == invoice.JournalId).ToArrayAsync();
        Assert.Equal(44m, lines.Sum(x => x.DebitAmount));
        Assert.Equal(44m, lines.Sum(x => x.CreditAmount));
    }
}
