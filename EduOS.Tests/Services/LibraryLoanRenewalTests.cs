using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Library;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LibraryLoanRenewalTests
{
    [Fact]
    public async Task Renewal_preserves_issued_copy_and_records_audited_due_date_history()
    {
        await using var db = Context(101);
        var (issue, copy) = await Seed(db);
        var oldDue = issue.DueDate;
        var service = Service(db);
        var request = new RenewBookRequestDto
        { NewDueDate = oldDue.AddDays(7), RowVersion = Convert.ToBase64String(issue.RowVersion) };
        var first = await service.RenewAsync(issue.PublicId, request);
        first.Success.Should().BeTrue(first.Message);
        first.Data!.DueDate.Should().Be(request.NewDueDate);
        copy.State.Should().Be(BookCopyState.Issued);
        var history = await service.GetRenewalHistoryAsync(issue.PublicId, 1, 20);
        history.Success.Should().BeTrue(history.Message);
        history.Data!.Items.Should().ContainSingle();
        history.Data.Items.Single().PreviousDueDate.Should().Be(oldDue);
        history.Data.Items.Single().NewDueDate.Should().Be(request.NewDueDate);
        (await service.RenewAsync(issue.PublicId, request)).Success.Should().BeTrue();
        (await service.GetRenewalHistoryAsync(issue.PublicId, 1, 20)).Data!.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Stale_version_and_active_waitlist_cannot_extend_loan()
    {
        await using var db = Context(101);
        var (issue, copy) = await Seed(db);
        var service = Service(db);
        var oldDue = issue.DueDate;
        var next = oldDue.AddDays(7);
        (await service.RenewAsync(issue.PublicId, new RenewBookRequestDto
        { NewDueDate = next, RowVersion = Convert.ToBase64String([9, 9, 9]) })).StatusCode.Should().Be(409);
        var waiting = new Student { TenantId = 101, PersonId = 99, StudentCode = "W",
            FullName = "Waiting", StatusCode = "Active" };
        db.Students.Add(waiting);
        await db.SaveChangesAsync();
        db.StudentEnrollments.Add(new StudentEnrollment { TenantId = 101, StudentId = waiting.Id,
            CampusId = 1, IsCurrent = true, State = EnrollmentState.Active,
            RollNo = "W", EnrollmentDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30) });
        db.BookReservations.Add(new BookReservation { TenantId = 101, BookId = copy.BookId,
            StudentId = waiting.Id, ClientRequestId = Guid.NewGuid(),
            State = BookReservationState.Pending, ExpiresAt = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();
        (await service.RenewAsync(issue.PublicId, new RenewBookRequestDto
        { NewDueDate = next, RowVersion = Convert.ToBase64String(issue.RowVersion) })).StatusCode.Should().Be(409);
        issue.DueDate.Should().Be(oldDue);
        (await service.GetRenewalHistoryAsync(issue.PublicId, 1, 20)).Data!.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Foreign_tenant_direct_issue_reference_is_denied()
    {
        var root = new InMemoryDatabaseRoot();
        const string database = "library-renew-isolation";
        await using var owner = Context(101, database, root);
        var (issue, _) = await Seed(owner);
        await using var foreign = Context(202, database, root);
        var service = Service(foreign, 202);
        (await service.GetRenewalHistoryAsync(issue.PublicId, 1, 20)).StatusCode.Should().Be(404);
        (await service.RenewAsync(issue.PublicId, new RenewBookRequestDto
        { NewDueDate = issue.DueDate.AddDays(7), RowVersion = Convert.ToBase64String(issue.RowVersion) }))
            .StatusCode.Should().Be(404);
    }

    private static async Task<(BookIssue Issue, BookCopy Copy)> Seed(EduOSDbContext db)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var book = new Book { TenantId = 101, Title = "Renewal" };
        var student = new Student { TenantId = 101, PersonId = 1, StudentCode = "S",
            FullName = "Borrower", StatusCode = "Active" };
        db.AddRange(book, student);
        await db.SaveChangesAsync();
        var copy = new BookCopy { TenantId = 101, BookId = book.Id,
            AccessionNumber = "L-1", State = BookCopyState.Issued };
        db.BookCopies.Add(copy);
        db.StudentEnrollments.Add(new StudentEnrollment { TenantId = 101, StudentId = student.Id,
            CampusId = 1, IsCurrent = true, State = EnrollmentState.Active,
            RollNo = "1", EnrollmentDate = today.AddDays(-30) });
        await db.SaveChangesAsync();
        var issue = new BookIssue { TenantId = 101, BookCopyId = copy.Id, StudentId = student.Id,
            ClientRequestId = Guid.NewGuid(), IssuedByUserId = 7,
            IssueDate = today, DueDate = today.AddDays(7),
            RowVersion = [1, 2, 3], State = BookIssueState.Issued };
        db.BookIssues.Add(issue);
        await db.SaveChangesAsync();
        return (issue, copy);
    }

    private static EduOSDbContext Context(long tenant, string? name = null, InMemoryDatabaseRoot? root = null)
    {
        var builder = new DbContextOptionsBuilder<EduOSDbContext>();
        if (root == null) builder.UseInMemoryDatabase(name ?? "renew-" + Guid.NewGuid().ToString("N"));
        else builder.UseInMemoryDatabase(name!, root);
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", tenant.ToString())
        ], "Test")) };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(builder.Options, new HttpContextAccessor { HttpContext = http });
    }

    private static LibraryService Service(EduOSDbContext db, long tenant = 101) => new(
        new GenericRepository<Book>(db), new GenericRepository<BookCopy>(db),
        new GenericRepository<BookIssue>(db), new GenericRepository<BookCategory>(db),
        new GenericRepository<Student>(db), new GenericRepository<StudentEnrollment>(db),
        db, new TestUser(tenant), TimeProvider.System, NullLogger<LibraryService>.Instance,
        reservations: new GenericRepository<BookReservation>(db),
        renewalAudits: new GenericRepository<AuditLog>(db));

    private sealed class TestUser(long tenant) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
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
