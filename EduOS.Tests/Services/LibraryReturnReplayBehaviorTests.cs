using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Library;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LibraryReturnReplayBehaviorTests
{
    [Fact]
    public async Task Returned_issue_accepts_matching_retry_but_rejects_changed_return_date()
    {
        await using var db = Context();
        var issue = await Seed(db, BookIssueState.Returned, BookCopyState.Available);
        var service = Service(db);
        var matching = await service.CloseAsync(issue.PublicId, new ReturnBookRequestDto
        {
            ReturnDate = issue.ReturnDate!.Value, RowVersion = "stale"
        });
        matching.Success.Should().BeTrue();
        var conflict = await service.CloseAsync(issue.PublicId, new ReturnBookRequestDto
        {
            ReturnDate = issue.ReturnDate.Value.AddDays(1), RowVersion = "stale"
        });
        conflict.StatusCode.Should().Be(409);
        (await db.BookIssues.SingleAsync()).ReturnDate.Should().Be(issue.ReturnDate);
        (await db.BookCopies.SingleAsync()).State.Should().Be(BookCopyState.Available);
    }

    [Theory]
    [InlineData(BookIssueState.Lost, BookCopyState.Lost)]
    [InlineData(BookIssueState.Damaged, BookCopyState.Damaged)]
    public async Task Terminal_nonreturned_issue_cannot_be_returned(BookIssueState state, BookCopyState copyState)
    {
        await using var db = Context();
        var issue = await Seed(db, state, copyState);
        var response = await Service(db).CloseAsync(issue.PublicId, new ReturnBookRequestDto());
        response.StatusCode.Should().Be(409);
        (await db.BookIssues.SingleAsync()).State.Should().Be(state);
        (await db.BookCopies.SingleAsync()).State.Should().Be(copyState);
    }

    private static async Task<BookIssue> Seed(EduOSDbContext db, BookIssueState state, BookCopyState copyState)
    {
        var student = new Student { TenantId = 101, PersonId = 1, StudentCode = "S-101", FullName = "Learner" };
        var book = new Book { TenantId = 101, Title = "Circulation" };
        db.AddRange(student, book);
        await db.SaveChangesAsync();
        var copy = new BookCopy { TenantId = 101, BookId = book.Id, AccessionNumber = "COPY-101", State = copyState };
        db.Add(copy);
        await db.SaveChangesAsync();
        var issue = new BookIssue
        {
            TenantId = 101, BookCopyId = copy.Id, StudentId = student.Id,
            ClientRequestId = Guid.NewGuid(), IssuedByUserId = 7,
            IssueDate = new DateOnly(2026, 10, 1), DueDate = new DateOnly(2026, 10, 20),
            ReturnDate = state == BookIssueState.Returned ? new DateOnly(2026, 10, 12) : null,
            State = state
        };
        db.Add(issue);
        await db.SaveChangesAsync();
        return issue;
    }

    private static EduOSDbContext Context()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("library-return-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "7"),
            new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", "101")
        ], "Test")) };
        http.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static LibraryService Service(EduOSDbContext db) => new(
        new GenericRepository<Book>(db), new GenericRepository<BookCopy>(db),
        new GenericRepository<BookIssue>(db), new GenericRepository<BookCategory>(db),
        new GenericRepository<Student>(db), new GenericRepository<StudentEnrollment>(db),
        db, new TestUser(), TimeProvider.System, NullLogger<LibraryService>.Instance);

    private sealed class TestUser : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => 101;
        public string? FullName => "Test";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
