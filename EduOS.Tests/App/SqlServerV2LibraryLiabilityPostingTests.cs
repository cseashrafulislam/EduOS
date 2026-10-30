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
    [Fact]
    public async Task V2_library_sql_second_admin_posts_one_balanced_traceable_student_invoice()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Guid issueRef;
        long secondId, headId, periodId;
        const decimal value = 175.50m;
        await using (var seed = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var suffix = Guid.NewGuid().ToString("N")[..10];
            var next = new ApplicationUser
            {
                UserName = "lib-review-" + suffix,
                NormalizedUserName = ("lib-review-" + suffix).ToUpperInvariant(),
                Email = "lib-review-" + suffix + "@example.test",
                NormalizedEmail = ("lib-review-" + suffix + "@example.test").ToUpperInvariant(),
                FullName = "Library reviewer", EmailConfirmed = true, IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
            };
            seed.Users.Add(next);
            await seed.SaveChangesAsync();
            secondId = next.Id;
            var enrollment = await seed.StudentEnrollments.AsNoTracking().SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.PublicId == fixture.Enrollments[0]);
            var branch = new LibraryBranch { TenantId = fixture.TenantId,
                CampusId = enrollment.CampusId, Code = "LIB-" + suffix, Name = "Library test branch" };
            var book = new Book { TenantId = fixture.TenantId,
                Title = "Lost physics text", ReplacementPrice = value };
            var receivable = new Account { TenantId = fixture.TenantId,
                Code = "LIB-R-" + suffix, Name = "Library receivable", AccountType = AccountType.Asset };
            var income = new Account { TenantId = fixture.TenantId,
                Code = "LIB-I-" + suffix, Name = "Library loss income", AccountType = AccountType.Income };
            var period = new AccountingPeriod { TenantId = fixture.TenantId,
                Name = "Library period " + suffix, StartDate = today.AddDays(-3), EndDate = today.AddDays(3) };
            seed.AddRange(branch, book, receivable, income, period);
            await seed.SaveChangesAsync();
            periodId = period.Id;
            var head = new FeeHead { TenantId = fixture.TenantId,
                Code = "LIB-CHG-" + suffix, Name = "Library replacement charge",
                IncomeAccountId = income.Id, ReceivableAccountId = receivable.Id,
                DefaultFrequency = FeeFrequencyType.OneTime };
            seed.FeeHeads.Add(head);
            await seed.SaveChangesAsync();
            headId = head.Id;
            var copy = new BookCopy { TenantId = fixture.TenantId, BookId = book.Id,
                LibraryBranchId = branch.Id, AccessionNumber = "LIB-COPY-" + suffix,
                State = BookCopyState.Lost };
            seed.BookCopies.Add(copy);
            await seed.SaveChangesAsync();
            var issue = new BookIssue { TenantId = fixture.TenantId,
                ClientRequestId = Guid.NewGuid(), BookCopyId = copy.Id,
                StudentId = enrollment.StudentId, IssueDate = today.AddDays(-20),
                DueDate = today.AddDays(-6), State = BookIssueState.Lost,
                ReplacementPriceSnapshot = value, IssuedByUserId = fixture.UserId };
            seed.BookIssues.Add(issue);
            await seed.SaveChangesAsync();
            issueRef = issue.PublicId;
        }
        var key = Guid.NewGuid();
        Guid chargeRef;
        string version;
        await using (var maker = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var service = Liabilities(maker, fixture.TenantId, fixture.UserId);
            var assessed = await service.AssessAsync(new AssessLibraryLiabilityRequestDto
            {
                ClientRequestId = key, IssueReference = issueRef, Amount = value,
                Reason = "Librarian verified missing copy"
            });
            Assert.True(assessed.Success, assessed.Message);
            chargeRef = assessed.Data!.Reference;
            version = assessed.Data.RowVersion;
            Assert.Equal(409, (await service.AssessAsync(new AssessLibraryLiabilityRequestDto
            {
                ClientRequestId = Guid.NewGuid(), IssueReference = issueRef, Amount = value,
                Reason = "Duplicate loss"
            })).StatusCode);
            Assert.Equal(403, (await service.ReviewAsync(chargeRef,
                new ReviewLibraryLiabilityRequestDto { RowVersion = version,
                    AccountingPeriodId = periodId, FeeHeadId = headId })).StatusCode);
            Assert.Equal(403, (await Liabilities(maker, fixture.TenantId + 10, fixture.UserId)
                .RegisterAsync("all", null, 1, 10)).StatusCode);
        }
        await using (var reviewer = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var service = Liabilities(reviewer, fixture.TenantId, secondId);
            var posted = await service.ReviewAsync(chargeRef, new ReviewLibraryLiabilityRequestDto
            { RowVersion = version, AccountingPeriodId = periodId, FeeHeadId = headId });
            Assert.True(posted.Success, posted.Message);
            Assert.Equal(value, posted.Data!.DueAmount);
            Assert.Equal(LibraryLiabilityState.Posted, posted.Data.State);
            Assert.Equal(409, (await service.ReviewAsync(chargeRef, new ReviewLibraryLiabilityRequestDto
            { RowVersion = version, AccountingPeriodId = periodId, FeeHeadId = headId })).StatusCode);
        }
        await using var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var row = await verify.LibraryLiabilities.SingleAsync(x => x.TenantId == fixture.TenantId &&
            x.PublicId == chargeRef);
        Assert.Equal(fixture.UserId, row.AssessedByUserId);
        Assert.Equal(secondId, row.ReviewedByUserId);
        var invoice = await verify.StudentInvoices.SingleAsync(x => x.TenantId == fixture.TenantId &&
            x.Id == row.StudentInvoiceId);
        Assert.Equal(value, invoice.TotalAmount);
        Assert.Equal(value, invoice.DueAmount);
        Assert.Equal(0m, invoice.FineAmount);
        Assert.Single(await verify.StudentInvoiceLines.Where(x => x.TenantId == fixture.TenantId &&
            x.StudentInvoiceId == invoice.Id && x.FeeHeadId == headId).ToListAsync());
        var journal = await verify.Journals.SingleAsync(x => x.TenantId == fixture.TenantId &&
            x.Id == invoice.JournalId && x.SourceType == "StudentInvoice" && x.SourceId == invoice.Id);
        var lines = await verify.JournalLines.Where(x => x.TenantId == fixture.TenantId &&
            x.JournalId == journal.Id).ToListAsync();
        Assert.Equal(value, lines.Sum(x => x.DebitAmount));
        Assert.Equal(value, lines.Sum(x => x.CreditAmount));
        var chargeVersion = Convert.ToBase64String(row.RowVersion);
        Guid contraRef;
        string contraVersion;
        await using (var maker = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var request = await Liabilities(maker, fixture.TenantId, secondId).RequestReversalAsync(
                chargeRef, new RequestLibraryReversalDto
                {
                    ClientRequestId = Guid.NewGuid(), ReversalDate = today, AccountingPeriodId = periodId,
                    Reason = "Verified replacement charge correction", RowVersion = chargeVersion
                });
            Assert.True(request.Success, request.Message);
            contraRef = request.Data!.Reference;
            contraVersion = request.Data.RowVersion;
            var sameMaker = await Liabilities(maker, fixture.TenantId, secondId)
                .ReviewReversalAsync(chargeRef, contraRef, new ReviewLibraryReversalDto
                { Approve = true, RowVersion = contraVersion });
            Assert.Equal(403, sameMaker.StatusCode);
        }
        await using (var reviewer = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var response = await Liabilities(reviewer, fixture.TenantId, fixture.UserId)
                .ReviewReversalAsync(chargeRef, contraRef, new ReviewLibraryReversalDto
                { Approve = true, RowVersion = contraVersion });
            Assert.True(response.Success, response.Message);
            Assert.Equal((int)JournalState.Posted, response.Data!.State);
        }
        await using var reconcile = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var reversedCharge = await reconcile.LibraryLiabilities.AsNoTracking().SingleAsync(x =>
            x.TenantId == fixture.TenantId && x.PublicId == chargeRef);
        Assert.Equal(LibraryLiabilityState.Reversed, reversedCharge.State);
        Assert.Equal(InvoiceState.Cancelled, (await reconcile.StudentInvoices.AsNoTracking()
            .SingleAsync(x => x.TenantId == fixture.TenantId && x.Id == row.StudentInvoiceId)).State);
        var contra = await reconcile.Journals.AsNoTracking().SingleAsync(x =>
            x.TenantId == fixture.TenantId && x.PublicId == contraRef && x.SourceType == "LibraryLiabilityReversal");
        Assert.Equal(journal.Id, contra.ReversalOfJournalId);
        var contraLines = await reconcile.JournalLines.AsNoTracking().Where(x =>
            x.TenantId == fixture.TenantId && x.JournalId == contra.Id).ToArrayAsync();
        Assert.Equal(value, contraLines.Sum(x => x.DebitAmount));
        Assert.Equal(value, contraLines.Sum(x => x.CreditAmount));
    }

    private static LibraryLiabilityService Liabilities(
        EduOS.Persistence.Context.EduOSDbContext db, long tenant, long user) =>
        new(db, new DigitalReader(tenant, user, "TenantAdmin"), TimeProvider.System,
            NullLogger<LibraryLiabilityService>.Instance);
}
