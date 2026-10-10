using EduOS.Core.DTOs.Library;
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

public sealed class LibraryArchiveGuardTests
{
    [Fact]
    public async Task Outstanding_issue_prevents_archive_and_metadata_deactivation()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("library-guard-" + Guid.NewGuid()).Options;
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", "101")
        ], "Test")) };
        http.Items["TenantId"] = 101L;
        await using var db = new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
        var book = new Book { TenantId = 101, Title = "Integrity" };
        var student = new Student { TenantId = 101, PersonId = 1, StudentCode = "S-101", FullName = "Learner" };
        db.AddRange(book, student);
        await db.SaveChangesAsync();
        book.RowVersion = [1, 2, 3, 4];
        var copy = new BookCopy { TenantId = 101, BookId = book.Id, AccessionNumber = "A-101", State = BookCopyState.Issued };
        db.BookCopies.Add(copy);
        await db.SaveChangesAsync();
        var issue = new BookIssue { TenantId = 101, BookCopyId = copy.Id, StudentId = student.Id,
            ClientRequestId = Guid.NewGuid(), IssuedByUserId = 7, State = BookIssueState.Issued,
            IssueDate = new DateOnly(2026, 10, 1), DueDate = new DateOnly(2026, 10, 20) };
        db.BookIssues.Add(issue);
        await db.SaveChangesAsync();
        var service = new LibraryService(new GenericRepository<Book>(db), new GenericRepository<BookCopy>(db),
            new GenericRepository<BookIssue>(db), new GenericRepository<BookCategory>(db),
            new GenericRepository<Student>(db), new GenericRepository<StudentEnrollment>(db),
            db, new AdminUser(), TimeProvider.System, NullLogger<LibraryService>.Instance);
        var version = Convert.ToBase64String(book.RowVersion);
        (await service.SaveBookAsync(new SaveBookRequestDto {
            Reference = book.PublicId, RowVersion = version, Title = book.Title, IsActive = false
        })).StatusCode.Should().Be(409);
        (await service.ArchiveBookAsync(book.PublicId, version)).StatusCode.Should().Be(409);
        book.IsActive.Should().BeTrue();
        issue.State = BookIssueState.Returned;
        issue.ReturnDate = new DateOnly(2026, 10, 10);
        copy.State = BookCopyState.Available;
        await db.SaveChangesAsync();
        (await service.ArchiveBookAsync(book.PublicId, version)).Success.Should().BeTrue();
        book.IsActive.Should().BeFalse();
    }

    private sealed class AdminUser : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => 101;
        public string? FullName => "Test";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string value) => value == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
