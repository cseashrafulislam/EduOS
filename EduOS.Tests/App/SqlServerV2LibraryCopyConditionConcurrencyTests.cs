using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>Real SQL Server rowversion and serializable transaction acceptance for physical copy incidents.</summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    private static LibraryService ConditionService(
        EduOS.Persistence.Context.EduOSDbContext db, long tenant, long user) =>
        new(new GenericRepository<Book>(db), new GenericRepository<BookCopy>(db),
            new GenericRepository<BookIssue>(db), new GenericRepository<BookCategory>(db),
            new GenericRepository<EduOS.Core.Entities.Students.Student>(db),
            new GenericRepository<EduOS.Core.Entities.Students.StudentEnrollment>(db),
            db, new DigitalReader(tenant, user, "TenantAdmin"), TimeProvider.System,
            NullLogger<LibraryService>.Instance,
            branches: new GenericRepository<LibraryBranch>(db),
            renewalAudits: new GenericRepository<AuditLog>(db));

    [Fact]
    public async Task V2_library_sql_competing_copy_incidents_have_one_winner_and_one_audit()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        long copyId;
        string version;
        await using (var setup = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var book = new Book { TenantId = fixture.TenantId, Title = "Condition concurrency" };
            setup.Books.Add(book);
            await setup.SaveChangesAsync();
            var copy = new BookCopy { TenantId = fixture.TenantId, BookId = book.Id,
                AccessionNumber = "COND-" + Guid.NewGuid().ToString("N"),
                State = BookCopyState.Available };
            setup.BookCopies.Add(copy);
            await setup.SaveChangesAsync();
            copyId = copy.Id;
            version = Convert.ToBase64String(copy.RowVersion);
        }
        async Task<EduOS.Core.Common.ApiResponse<LibraryCopyConditionDto>> Change(BookCopyState target)
        {
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            return await ConditionService(db, fixture.TenantId, fixture.UserId)
                .ChangeCopyConditionAsync(copyId, new ChangeLibraryCopyConditionRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), TargetState = target,
                    RowVersion = version, Reason = "Physical inventory incident"
                });
        }
        var results = await Task.WhenAll(Change(BookCopyState.Lost), Change(BookCopyState.Damaged));
        Assert.Single(results.Where(x => x.Success));
        Assert.Single(results.Where(x => x.StatusCode == 409));
        await using var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var state = await verify.BookCopies.AsNoTracking().Where(x =>
            x.TenantId == fixture.TenantId && x.Id == copyId).Select(x => x.State).SingleAsync();
        Assert.Equal(results.Single(x => x.Success).Data!.State, state);
        Assert.Equal(1, await verify.AuditLogs.CountAsync(x =>
            x.TenantId == fixture.TenantId && x.EntityName == "BookCopy" &&
            x.EntityId == copyId && x.Action == "LibraryCopy.Condition"));
    }

    [Fact]
    public async Task V2_library_sql_lost_loan_preserves_borrower_history_without_creating_receivable()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        long copyId, loanId;
        string copyVersion, loanVersion;
        await using (var setup = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var studentId = await setup.StudentEnrollments.Where(x =>
                x.TenantId == fixture.TenantId && x.PublicId == fixture.Enrollments[0])
                .Select(x => x.StudentId).SingleAsync();
            var book = new Book { TenantId = fixture.TenantId, Title = "Loan condition history" };
            setup.Books.Add(book); await setup.SaveChangesAsync();
            var copy = new BookCopy { TenantId = fixture.TenantId, BookId = book.Id,
                AccessionNumber = "LOST-" + Guid.NewGuid().ToString("N"),
                State = BookCopyState.Issued };
            setup.BookCopies.Add(copy); await setup.SaveChangesAsync();
            var loan = new BookIssue
            {
                TenantId = fixture.TenantId, BookCopyId = copy.Id, StudentId = studentId,
                ClientRequestId = Guid.NewGuid(), IssuedByUserId = fixture.UserId,
                IssueDate = new DateOnly(2026, 10, 1), DueDate = new DateOnly(2026, 10, 20),
                State = BookIssueState.Issued
            };
            setup.BookIssues.Add(loan); await setup.SaveChangesAsync();
            copyId = copy.Id; loanId = loan.Id;
            copyVersion = Convert.ToBase64String(copy.RowVersion);
            loanVersion = Convert.ToBase64String(loan.RowVersion);
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var result = await ConditionService(db, fixture.TenantId, fixture.UserId)
                .ChangeCopyConditionAsync(copyId, new ChangeLibraryCopyConditionRequestDto
                {
                    ClientRequestId = Guid.NewGuid(), TargetState = BookCopyState.Lost,
                    RowVersion = copyVersion, OpenIssueRowVersion = loanVersion,
                    Reason = "Inventory and borrower check completed"
                });
            Assert.True(result.Success, result.Message);
        }
        await using var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var historical = await verify.BookIssues.AsNoTracking().SingleAsync(x =>
            x.TenantId == fixture.TenantId && x.Id == loanId);
        Assert.Equal(BookIssueState.Lost, historical.State);
        Assert.Null(historical.ReturnDate);
        Assert.Equal(0m, historical.FineAmount);
        Assert.Equal(BookCopyState.Lost, await verify.BookCopies.AsNoTracking()
            .Where(x => x.Id == copyId).Select(x => x.State).SingleAsync());
        Assert.Equal(1, await verify.AuditLogs.CountAsync(x =>
            x.TenantId == fixture.TenantId && x.EntityId == copyId &&
            x.Action == "LibraryCopy.Condition"));
    }
}
