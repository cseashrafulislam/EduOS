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
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;
namespace EduOS.Tests.Services;
public sealed class LibraryReservationServiceTests
{
    private static EduOSDbContext Context(long tenant, string? databaseName = null, InMemoryDatabaseRoot? databaseRoot = null)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase(databaseName ?? "reservation-" + Guid.NewGuid().ToString("N"), databaseRoot).Options;
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", tenant.ToString())
        ], "Test")) };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
    private static LibraryReservationService Service(EduOSDbContext db, long tenant, string role = "TenantAdmin") => new(
        new GenericRepository<BookReservation>(db), new GenericRepository<Book>(db),
        new GenericRepository<BookCopy>(db), new GenericRepository<BookIssue>(db),
        new GenericRepository<LibraryBranch>(db), new GenericRepository<Student>(db),
        new GenericRepository<StudentEnrollment>(db), db, new User(tenant, role),
        TimeProvider.System, NullLogger<LibraryReservationService>.Instance);
    private sealed class User(long tenant, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => "Test";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string name) => role == name;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
    private static async Task<(Student Student, Book Book, BookCopy Copy)> Seed(EduOSDbContext db, long tenant)
    {
        var student = new Student { TenantId = tenant, UserId = 7, PersonId = 1,
            StudentCode = "ST-" + tenant, FullName = "Learner", StatusCode = "Active" };
        var book = new Book { TenantId = tenant, Title = "Algorithms", IsActive = true };
        db.AddRange(student, book); await db.SaveChangesAsync();
        var copy = new BookCopy { TenantId = tenant, BookId = book.Id, AccessionNumber = "COPY-" + tenant };
        db.BookCopies.Add(copy);
        db.StudentEnrollments.Add(new StudentEnrollment { TenantId = tenant, StudentId = student.Id,
            IsCurrent = true, State = EnrollmentState.Active, RollNo = "1",
            EnrollmentDate = new DateOnly(2026, 1, 1) });
        await db.SaveChangesAsync();
        return (student, book, copy);
    }
    [Fact]
    public async Task Staff_student_search_is_tenant_scoped_enrollment_filtered_and_paged()
    {
        var databaseName = "reservation-search-" + Guid.NewGuid().ToString("N");
        var databaseRoot = new InMemoryDatabaseRoot();
        var otherTenant = new Student { TenantId = 202, PersonId = 5,
            StudentCode = "ST-105", FullName = "Other Tenant", StatusCode = "Active" };
        await using (var otherDb = Context(202, databaseName, databaseRoot))
        {
            otherDb.Students.Add(otherTenant);
            await otherDb.SaveChangesAsync();
            otherDb.StudentEnrollments.Add(new StudentEnrollment { TenantId = 202, StudentId = otherTenant.Id,
                IsCurrent = true, State = EnrollmentState.Active, RollNo = "4",
                EnrollmentDate = new DateOnly(2026, 1, 1) });
            await otherDb.SaveChangesAsync();
        }
        await using var db = Context(101, databaseName, databaseRoot);
        var (student, _, _) = await Seed(db, 101);
        var eligible = new Student { TenantId = 101, PersonId = 2,
            StudentCode = "ST-102", FullName = "Eligible Two", StatusCode = "Active" };
        var unenrolled = new Student { TenantId = 101, PersonId = 3,
            StudentCode = "ST-103", FullName = "No Enrollment", StatusCode = "Active" };
        var inactive = new Student { TenantId = 101, PersonId = 4,
            StudentCode = "ST-104", FullName = "Inactive", StatusCode = "Inactive" };
        db.AddRange(eligible, unenrolled, inactive);
        await db.SaveChangesAsync();
        db.StudentEnrollments.AddRange(
            new StudentEnrollment { TenantId = 101, StudentId = eligible.Id, IsCurrent = true,
                State = EnrollmentState.Active, RollNo = "2", EnrollmentDate = new DateOnly(2026, 1, 1) },
            new StudentEnrollment { TenantId = 101, StudentId = inactive.Id, IsCurrent = true,
                State = EnrollmentState.Active, RollNo = "3", EnrollmentDate = new DateOnly(2026, 1, 1) });
        await db.SaveChangesAsync();
        // Tenant-wide paging is covered here; campus-granted Librarian/Principal cases
        // are exercised separately by LibraryReservationCampusScopeTests.
        var service = Service(db, 101, "TenantAdmin");
        var first = await service.SearchEligibleStudentsAsync(" ST- ", 1, 1);
        first.Success.Should().BeTrue();
        first.Data!.TotalCount.Should().Be(2);
        first.Data.Items.Should().ContainSingle(x => x.StudentReference == student.PublicId);
        var second = await service.SearchEligibleStudentsAsync("ST-", 2, 1);
        second.Data!.Items.Should().ContainSingle(x => x.StudentReference == eligible.PublicId);
        (await service.SearchEligibleStudentsAsync("S", 1, 10)).StatusCode.Should().Be(400);
        (await service.SearchEligibleStudentsAsync("ST-", 1, 51)).StatusCode.Should().Be(400);
        (await Service(db, 101, "Student").SearchEligibleStudentsAsync("ST-", 1, 10))
            .StatusCode.Should().Be(403);
    }
    [Fact]
    public async Task Student_reservation_is_idempotent_and_duplicate_is_rejected()
    {
        await using var db = Context(101);
        var (_, book, _) = await Seed(db, 101);
        var service = Service(db, 101, "Student");
        var request = new ReserveBookRequestDto { ClientRequestId = Guid.NewGuid(), BookReference = book.PublicId };
        var first = await service.ReserveBookAsync(request);
        first.Success.Should().BeTrue();
        (await service.ReserveBookAsync(request)).Data!.Id.Should().Be(first.Data!.Id);
        (await service.ReserveBookAsync(new ReserveBookRequestDto {
            ClientRequestId = Guid.NewGuid(), BookReference = book.PublicId
        })).StatusCode.Should().Be(409);
        (await db.BookReservations.CountAsync()).Should().Be(1);
    }
    [Fact]
    public async Task Student_cannot_reserve_for_another_student()
    {
        await using var db = Context(101);
        var (_, book, _) = await Seed(db, 101);
        var other = new Student { TenantId = 101, UserId = 8, PersonId = 2,
            StudentCode = "ST-OTHER", FullName = "Other", StatusCode = "Active" };
        db.Students.Add(other); await db.SaveChangesAsync();
        var result = await Service(db, 101, "Student").ReserveBookAsync(new ReserveBookRequestDto {
            ClientRequestId = Guid.NewGuid(), BookReference = book.PublicId, StudentReference = other.PublicId
        });
        result.StatusCode.Should().Be(403);
        (await db.BookReservations.CountAsync()).Should().Be(0);
    }
    [Fact]
    public async Task Fulfillment_issues_copy_once_and_cancellation_cannot_reverse_it()
    {
        await using var db = Context(101);
        var (_, book, copy) = await Seed(db, 101);
        var service = Service(db, 101);
        var request = new ReserveBookRequestDto { ClientRequestId = Guid.NewGuid(), BookReference = book.PublicId };
        var created = await service.ReserveBookAsync(request);
        created.Success.Should().BeTrue();
        var row = await db.BookReservations.SingleAsync();
        row.RowVersion = [1, 2, 3]; await db.SaveChangesAsync();
        var version = Convert.ToBase64String(row.RowVersion);
        (await service.FulfillReservationAsync(row.Id, copy.Id, "stale")).StatusCode.Should().Be(409);
        var fulfilled = await service.FulfillReservationAsync(row.Id, copy.Id, version);
        fulfilled.Success.Should().BeTrue();
        (await service.FulfillReservationAsync(row.Id, copy.Id, "stale")).Success.Should().BeTrue();
        (await service.CancelReservationAsync(row.Id, version)).StatusCode.Should().Be(409);
        (await db.BookIssues.CountAsync()).Should().Be(1);
        (await db.BookCopies.SingleAsync()).State.Should().Be(BookCopyState.Issued);
    }
    [Fact]
    public async Task Self_service_reservation_status_filters_are_tenant_and_user_scoped()
    {
        await using var db = Context(101);
        var (student, book, _) = await Seed(db, 101);
        var other = new Student { TenantId = 101, UserId = 8, PersonId = 22,
            StudentCode = "ST-OTHER", FullName = "Other borrower", StatusCode = "Active" };
        db.Students.Add(other);
        await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        db.BookReservations.AddRange(
            new BookReservation { TenantId = 101, StudentId = student.Id, BookId = book.Id,
                ClientRequestId = Guid.NewGuid(), State = BookReservationState.Pending,
                ReservedAt = now.AddDays(-1), ExpiresAt = now.AddDays(1) },
            new BookReservation { TenantId = 101, StudentId = student.Id, BookId = book.Id,
                ClientRequestId = Guid.NewGuid(), State = BookReservationState.Pending,
                ReservedAt = now.AddDays(-10), ExpiresAt = now.AddDays(-3) },
            new BookReservation { TenantId = 101, StudentId = student.Id, BookId = book.Id,
                ClientRequestId = Guid.NewGuid(), State = BookReservationState.Fulfilled,
                ReservedAt = now.AddDays(-12) },
            new BookReservation { TenantId = 101, StudentId = student.Id, BookId = book.Id,
                ClientRequestId = Guid.NewGuid(), State = BookReservationState.Cancelled,
                ReservedAt = now.AddDays(-15) },
            new BookReservation { TenantId = 101, StudentId = other.Id, BookId = book.Id,
                ClientRequestId = Guid.NewGuid(), State = BookReservationState.Pending,
                ReservedAt = now, ExpiresAt = now.AddDays(2) });
        await db.SaveChangesAsync();
        var service = Service(db, 101, "Student");
        (await service.GetMyReservationsAsync(1, 20)).Data!.TotalCount.Should().Be(4);
        (await service.GetMyReservationsAsync(1, 20, default, "pending"))
            .Data!.Items.Should().ContainSingle().Which.State.Should().Be(BookReservationState.Pending);
        (await service.GetMyReservationsAsync(1, 20, default, "expired"))
            .Data!.Items.Should().ContainSingle().Which.State.Should().Be(BookReservationState.Expired);
        (await service.GetMyReservationsAsync(1, 20, default, "fulfilled"))
            .Data!.TotalCount.Should().Be(1);
        (await service.GetMyReservationsAsync(1, 20, default, "cancelled"))
            .Data!.TotalCount.Should().Be(1);
        (await service.GetMyReservationsAsync(1, 20, default, "invalid"))
            .StatusCode.Should().Be(400);
        (await service.GetMyReservationsAsync(1, 1)).Data!.Items.Should().HaveCount(1);
    }

}
