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
public sealed class LibraryReservationServiceTests
{
    private static EduOSDbContext Context(long tenant)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("reservation-" + Guid.NewGuid().ToString("N")).Options;
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
    public async Task Foreign_tenant_book_is_not_reservable()
    {
        await using var db = Context(101);
        await Seed(db, 101);
        var foreign = new Book { TenantId = 202, Title = "Foreign", IsActive = true };
        db.Books.Add(foreign); await db.SaveChangesAsync();
        var result = await Service(db, 101, "Student").ReserveBookAsync(new ReserveBookRequestDto {
            ClientRequestId = Guid.NewGuid(), BookReference = foreign.PublicId
        });
        result.StatusCode.Should().Be(404);
    }
}