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

public sealed class LibraryServiceTests
{
    [Fact]
    public async Task Catalog_is_tenant_scoped_and_stably_paged()
    {
        var options = Options();
        await using (var seed = Context(options, 101))
        {
            seed.Books.AddRange(new Book { TenantId = 101, Title = "Alpha" },
                new Book { TenantId = 101, Title = "Bravo" },
                new Book { TenantId = 101, Title = "Charlie" });
            await seed.SaveChangesAsync();
        }
        await using (var other = Context(options, 202))
        {
            other.Books.Add(new Book { TenantId = 202, Title = "Private" });
            await other.SaveChangesAsync();
        }
        await using var db = Context(options, 101);
        var service = Service(db, 101);
        var first = await service.GetCatalogAsync(null, 1, 2);
        var second = await service.GetCatalogAsync(null, 2, 2);
        first.Success.Should().BeTrue();
        first.Data!.TotalCount.Should().Be(3);
        first.Data.Items.Select(x => x.Title).Should().Equal("Alpha", "Bravo");
        second.Data!.Items.Should().ContainSingle().Which.Title.Should().Be("Charlie");
        (await service.GetCatalogAsync(null, 1, 500)).Data!.PageSize.Should().Be(100);
        (await service.GetCatalogAsync(null, int.MaxValue, 100)).Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Canonical_book_save_request_persists_and_appears_in_paged_catalog()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var service = Service(db, 101);
        var saved = await service.SaveBookAsync(new SaveBookRequestDto
        {
            Title = "  Data Structures  ", ISBN = "9780000000001", ReplacementPrice = 125.50m
        });
        saved.Success.Should().BeTrue();
        saved.Data!.Title.Should().Be("Data Structures");
        saved.Data.Reference.Should().NotBeEmpty();
        var catalog = await service.GetCatalogAsync("Data Structures", 1, 20);
        catalog.Success.Should().BeTrue();
        catalog.Data!.Items.Should().ContainSingle().Which.Reference.Should().Be(saved.Data.Reference);
        (await db.Books.SingleAsync()).ReplacementPrice.Should().Be(125.50m);
    }

    [Fact]
    public async Task Student_cannot_modify_catalog()
    {
        var options = Options();
        await using var db = Context(options, 101, "Student");
        var result = await Service(db, 101, "Student").SaveBookAsync(new SaveBookRequestDto { Title = "Denied" });
        result.StatusCode.Should().Be(403);
        (await db.Books.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Student_can_only_see_own_issue_history()
    {
        var options = Options();
        await using (var db = Context(options, 101))
        {
            var book = new Book { TenantId = 101, Title = "Library" };
            var mine = new Student { TenantId = 101, UserId = 7, PersonId = 1, StudentCode = "S1", FullName = "Mine" };
            var other = new Student { TenantId = 101, UserId = 8, PersonId = 2, StudentCode = "S2", FullName = "Other" };
            db.AddRange(book, mine, other); await db.SaveChangesAsync();
            var a = new BookCopy { TenantId = 101, BookId = book.Id, AccessionNumber = "A" };
            var b = new BookCopy { TenantId = 101, BookId = book.Id, AccessionNumber = "B" };
            db.AddRange(a, b); await db.SaveChangesAsync();
            db.BookIssues.AddRange(
                new BookIssue { TenantId = 101, BookCopyId = a.Id, StudentId = mine.Id, ClientRequestId = Guid.NewGuid(), IssuedByUserId = 7, IssueDate = new DateOnly(2026, 10, 1), DueDate = new DateOnly(2026, 10, 20) },
                new BookIssue { TenantId = 101, BookCopyId = b.Id, StudentId = other.Id, ClientRequestId = Guid.NewGuid(), IssuedByUserId = 7, IssueDate = new DateOnly(2026, 10, 2), DueDate = new DateOnly(2026, 10, 21) });
            await db.SaveChangesAsync();
        }
        await using var reader = Context(options, 101, "Student");
        var response = await Service(reader, 101, "Student").GetMyIssuesAsync(1, 20);
        response.Success.Should().BeTrue();
        response.Data!.TotalCount.Should().Be(1);
        response.Data.Items.Should().ContainSingle().Which.StudentName.Should().Be("Mine");
    }

    [Fact]
    public async Task Issue_requires_current_active_enrollment()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var (student, copy) = await SeedBorrower(db, 101, false);
        var result = await Service(db, 101).IssueAsync(Request(student, copy, Guid.NewGuid()));
        result.StatusCode.Should().Be(409);
        (await db.BookIssues.CountAsync()).Should().Be(0);
        (await db.BookCopies.SingleAsync()).State.Should().Be(BookCopyState.Available);
    }

    [Fact]
    public async Task Issue_is_retry_safe_and_rejects_changed_payload_or_double_issue()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var (student, copy) = await SeedBorrower(db, 101, true);
        var service = Service(db, 101);
        var key = Guid.NewGuid();
        var first = await service.IssueAsync(Request(student, copy, key));
        first.Success.Should().BeTrue();
        first.Data!.Reference.Should().NotBeEmpty();
        var replay = await service.IssueAsync(Request(student, copy, key));
        replay.Success.Should().BeTrue();
        replay.Data!.Reference.Should().Be(first.Data.Reference);
        var changed = Request(student, copy, key); changed.DueDate = new DateOnly(2026, 11, 1);
        (await service.IssueAsync(changed)).StatusCode.Should().Be(409);
        (await service.IssueAsync(Request(student, copy, Guid.NewGuid()))).StatusCode.Should().Be(409);
        (await db.BookIssues.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Return_uses_public_reference_and_rejects_stale_version_without_stock_change()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var (student, copy) = await SeedBorrower(db, 101, true);
        var service = Service(db, 101);
        var issueResult = await service.IssueAsync(Request(student, copy, Guid.NewGuid()));
        issueResult.Success.Should().BeTrue();
        var issue = await db.BookIssues.SingleAsync();
        issue.RowVersion = [1, 2, 3, 4];
        await db.SaveChangesAsync();
        var stale = await service.CloseAsync(issue.PublicId, new ReturnBookRequestDto { RowVersion = Convert.ToBase64String([4, 3, 2, 1]) });
        stale.StatusCode.Should().Be(409);
        issue.State.Should().Be(BookIssueState.Issued);
        copy.State.Should().Be(BookCopyState.Issued);
        var closed = await service.CloseAsync(issue.PublicId, new ReturnBookRequestDto { RowVersion = Convert.ToBase64String(issue.RowVersion) });
        closed.Success.Should().BeTrue();
        closed.Data!.State.Should().Be(BookIssueState.Returned);
        copy.State.Should().Be(BookCopyState.Available);
        (await service.CloseAsync(issue.PublicId, new ReturnBookRequestDto { RowVersion = "stale" })).Success.Should().BeTrue();
        (await db.BookIssues.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Foreign_tenant_student_reference_is_not_issuable()
    {
        var options = Options();
        Guid foreignReference;
        await using (var foreign = Context(options, 202))
        {
            var (student, _) = await SeedBorrower(foreign, 202, true);
            foreignReference = student.PublicId;
        }
        await using var local = Context(options, 101);
        var (_, copy) = await SeedBorrower(local, 101, true);
        var response = await Service(local, 101).IssueAsync(new IssueBookRequestDto
        {
            ClientRequestId = Guid.NewGuid(), BookCopyId = copy.Id, StudentReference = foreignReference,
            IssueDate = new DateOnly(2026, 10, 10), DueDate = new DateOnly(2026, 10, 20)
        });
        response.Success.Should().BeFalse();
        (await local.BookIssues.CountAsync()).Should().Be(0);
    }

    private static IssueBookRequestDto Request(Student student, BookCopy copy, Guid key) => new()
    {
        ClientRequestId = key, BookCopyId = copy.Id, StudentReference = student.PublicId,
        IssueDate = new DateOnly(2026, 10, 10), DueDate = new DateOnly(2026, 10, 20)
    };

    private static async Task<(Student Student, BookCopy Copy)> SeedBorrower(EduOSDbContext db, long tenant, bool active)
    {
        var student = new Student { TenantId = tenant, PersonId = 1, StudentCode = "S-" + tenant, FullName = "Learner" };
        var book = new Book { TenantId = tenant, Title = "Test Book" };
        db.AddRange(student, book); await db.SaveChangesAsync();
        var copy = new BookCopy { TenantId = tenant, BookId = book.Id, AccessionNumber = "A-" + tenant };
        db.BookCopies.Add(copy);
        if (active) db.StudentEnrollments.Add(new StudentEnrollment
        {
            TenantId = tenant, StudentId = student.Id, IsCurrent = true, State = EnrollmentState.Active,
            RollNo = "1", EnrollmentDate = new DateOnly(2026, 1, 1)
        });
        await db.SaveChangesAsync();
        return (student, copy);
    }

    private static LibraryService Service(EduOSDbContext db, long tenant, string role = "TenantAdmin") => new(
        new GenericRepository<Book>(db), new GenericRepository<BookCopy>(db),
        new GenericRepository<BookIssue>(db), new GenericRepository<BookCategory>(db),
        new GenericRepository<Student>(db), new GenericRepository<StudentEnrollment>(db),
        db, new TestUser(tenant, role), TimeProvider.System, NullLogger<LibraryService>.Instance);

    private static DbContextOptions<EduOSDbContext> Options() => new DbContextOptionsBuilder<EduOSDbContext>()
        .UseInMemoryDatabase("library-" + Guid.NewGuid().ToString("N")).Options;

    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant, string role = "TenantAdmin")
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, role), new Claim("TenantId", tenant.ToString())
        ], "Test")) };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed class TestUser(long tenant, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => "Test";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
