using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Library;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LibraryOverdueNoticeServiceTests
{
    private sealed class User(string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => 101;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private static EduOSDbContext Context()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("library-overdue-notice-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", "101")
        ], "Test")) };
        http.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static LibraryOverdueNoticeService Service(EduOSDbContext db, string role) =>
        new(db, new User(role), TimeProvider.System,
            NullLogger<LibraryOverdueNoticeService>.Instance);

    [Fact]
    public async Task Notice_batch_is_campus_scoped_idempotent_and_never_posts_finance()
    {
        await using var db = Context();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var a = new Campus { TenantId = 101, Name = "North", Code = "N", IsActive = true };
        var b = new Campus { TenantId = 101, Name = "South", Code = "S", IsActive = true };
        var book = new Book { TenantId = 101, Title = "Reference book", IsActive = true };
        var student = new Student { TenantId = 101, PersonId = 10, StudentCode = "N1",
            FullName = "Reader", UserId = 22, StatusCode = "Active", PreferredLanguage = "bn-BD" };
        db.AddRange(a, b, book, student);
        await db.SaveChangesAsync();
        var branchA = new LibraryBranch { TenantId = 101, CampusId = a.Id,
            Name = "North Library", Code = "NL", IsActive = true };
        var branchB = new LibraryBranch { TenantId = 101, CampusId = b.Id,
            Name = "South Library", Code = "SL", IsActive = true };
        db.AddRange(branchA, branchB,
            new StudentEnrollment { TenantId = 101, StudentId = student.Id, CampusId = a.Id,
                IsCurrent = true, State = EnrollmentState.Active,
                RollNo = "1", EnrollmentDate = today.AddDays(-30) });
        await db.SaveChangesAsync();
        var copy = new BookCopy { TenantId = 101, BookId = book.Id,
            LibraryBranchId = branchA.Id, AccessionNumber = "N-1", State = BookCopyState.Issued };
        var other = new BookCopy { TenantId = 101, BookId = book.Id,
            LibraryBranchId = branchB.Id, AccessionNumber = "S-1", State = BookCopyState.Issued };
        db.AddRange(copy, other);
        await db.SaveChangesAsync();
        db.BookIssues.AddRange(
            new BookIssue { TenantId = 101, BookCopyId = copy.Id, StudentId = student.Id,
                ClientRequestId = Guid.NewGuid(), IssueDate = today.AddDays(-20),
                DueDate = today.AddDays(-2), State = BookIssueState.Issued },
            new BookIssue { TenantId = 101, BookCopyId = other.Id, StudentId = student.Id,
                ClientRequestId = Guid.NewGuid(), IssueDate = today.AddDays(-20),
                DueDate = today.AddDays(-2), State = BookIssueState.Issued });
        db.UserCampusAccesses.Add(new UserCampusAccess
        { TenantId = 101, UserId = 7, CampusId = a.Id, IsActive = true });
        await db.SaveChangesAsync();
        var principal = Service(db, "Principal");
        (await principal.PreviewAsync(b.Id)).StatusCode.Should().Be(403);
        // A borrower can opt out of this optional in-app event without
        // suppressing any legally required finance or account notice.
        var preference = new NotificationPreference
        {
            TenantId = 101, UserId = student.UserId!.Value,
            Channel = NotificationChannelType.InApp, EventCode = "LIBRARY_OVERDUE",
            IsEnabled = false
        };
        db.NotificationPreferences.Add(preference);
        await db.SaveChangesAsync();
        (await principal.PreviewAsync(a.Id)).Data!.PendingCount.Should().Be(0);
        preference.IsEnabled = true;
        await db.SaveChangesAsync();
        var preview = await principal.PreviewAsync(a.Id);
        preview.Success.Should().BeTrue(preview.Message);
        preview.Data!.PendingCount.Should().Be(1);
        var id = Guid.NewGuid();
        var request = new SendLibraryOverdueNoticesRequestDto
        { ClientRequestId = id, CampusId = a.Id, AsOfDate = today };
        var first = await principal.SendBatchAsync(request);
        first.Success.Should().BeTrue(first.Message);
        first.Data!.SentCount.Should().Be(1);
        (await principal.SendBatchAsync(request)).Data!.AlreadyProcessed.Should().BeTrue();
        (await principal.SendBatchAsync(new SendLibraryOverdueNoticesRequestDto
        { ClientRequestId = Guid.NewGuid(), CampusId = a.Id, AsOfDate = today }))
            .Data!.SentCount.Should().Be(0);
        (await db.Notifications.CountAsync()).Should().Be(1);
        (await db.Notifications.SingleAsync()).Body.Should().Contain("বই");
        (await db.AuditLogs.CountAsync(x => x.Action == "LibraryOverdueNotice.Send")).Should().Be(2);
        (await principal.PreviewAsync(a.Id)).Data!.PendingCount.Should().Be(0);
        (await Service(db, "Student").PreviewAsync(a.Id)).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Revoked_grant_and_stale_date_reject_notice_writes()
    {
        await using var db = Context();
        var campus = new Campus { TenantId = 101, Name = "North", Code = "N", IsActive = true };
        var grant = new UserCampusAccess { TenantId = 101, UserId = 7,
            CampusId = 1, IsActive = true };
        db.Campuses.Add(campus);
        await db.SaveChangesAsync();
        grant.CampusId = campus.Id;
        db.UserCampusAccesses.Add(grant);
        await db.SaveChangesAsync();
        var principal = Service(db, "Principal");
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        (await principal.SendBatchAsync(new SendLibraryOverdueNoticesRequestDto
        { ClientRequestId = Guid.NewGuid(), CampusId = campus.Id, AsOfDate = yesterday }))
            .StatusCode.Should().Be(409);
        grant.IsActive = false;
        await db.SaveChangesAsync();
        (await principal.PreviewAsync(campus.Id)).StatusCode.Should().Be(403);
        (await principal.SendBatchAsync(new SendLibraryOverdueNoticesRequestDto
        { ClientRequestId = Guid.NewGuid(), CampusId = campus.Id, AsOfDate = yesterday.AddDays(1) }))
            .StatusCode.Should().Be(403);
    }
}
