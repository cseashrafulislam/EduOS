using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.SaaS;
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

public sealed class LibraryReservationCampusScopeTests
{
    private static EduOSDbContext Context()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("library-campus-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", "101")
        ], "Test")) };
        http.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
    private sealed class User(string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => 101;
        public string? FullName => "Test";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string name) => role == name;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
    private static LibraryReservationService Service(EduOSDbContext db, string role) => new(
        new GenericRepository<BookReservation>(db), new GenericRepository<Book>(db),
        new GenericRepository<BookCopy>(db), new GenericRepository<BookIssue>(db),
        new GenericRepository<LibraryBranch>(db), new GenericRepository<Student>(db),
        new GenericRepository<StudentEnrollment>(db), db, new User(role),
        TimeProvider.System, NullLogger<LibraryReservationService>.Instance,
        new GenericRepository<UserCampusAccess>(db), new GenericRepository<Campus>(db),
        new GenericRepository<Employee>(db), new GenericRepository<EmployeeCampusAssignment>(db));

    [Theory]
    [InlineData("Principal")]
    [InlineData("Librarian")]
    public async Task Staff_queue_search_and_copy_fulfillment_are_campus_scoped(string role)
    {
        await using var db = Context();
        var a = new Campus { TenantId = 101, Code = "A", Name = "Campus A" };
        var b = new Campus { TenantId = 101, Code = "B", Name = "Campus B" };
        var book = new Book { TenantId = 101, Title = "Campus stock", IsActive = true };
        var sa = new Student { TenantId = 101, UserId = 8, PersonId = 1, StudentCode = "ST-A", FullName = "Student A", StatusCode = "Active" };
        var sb = new Student { TenantId = 101, UserId = 9, PersonId = 2, StudentCode = "ST-B", FullName = "Student B", StatusCode = "Active" };
        db.AddRange(a, b, book, sa, sb);
        await db.SaveChangesAsync();
        var ba = new LibraryBranch { TenantId = 101, CampusId = a.Id, Name = "A Library", Code = "AL" };
        var bb = new LibraryBranch { TenantId = 101, CampusId = b.Id, Name = "B Library", Code = "BL" };
        db.AddRange(ba, bb,
            new StudentEnrollment { TenantId = 101, StudentId = sa.Id, CampusId = a.Id, IsCurrent = true,
                State = EnrollmentState.Active, RollNo = "1", EnrollmentDate = new DateOnly(2026, 1, 1) },
            new StudentEnrollment { TenantId = 101, StudentId = sb.Id, CampusId = b.Id, IsCurrent = true,
                State = EnrollmentState.Active, RollNo = "2", EnrollmentDate = new DateOnly(2026, 1, 1) });
        await db.SaveChangesAsync();
        var ca = new BookCopy { TenantId = 101, BookId = book.Id, LibraryBranchId = ba.Id, AccessionNumber = "A-1" };
        var cb = new BookCopy { TenantId = 101, BookId = book.Id, LibraryBranchId = bb.Id, AccessionNumber = "B-1" };
        db.AddRange(ca, cb, new UserCampusAccess { TenantId = 101, UserId = 7, CampusId = a.Id, IsActive = true });
        var employee = new Employee { TenantId = 101, UserId = 7, PersonId = 3, DesignationId = 1,
            EmployeeCode = "LIB-7", FullName = "Librarian", State = EmployeeState.Active };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        db.Add(new EmployeeCampusAssignment { TenantId = 101, EmployeeId = employee.Id,
            CampusId = a.Id, IsCurrent = true, EffectiveFrom = new DateOnly(2026, 1, 1) });
        await db.SaveChangesAsync();

        var admin = Service(db, "TenantAdmin");
        var other = await admin.ReserveBookAsync(new ReserveBookRequestDto {
            ClientRequestId = Guid.NewGuid(), BookReference = book.PublicId, StudentReference = sb.PublicId });
        other.Success.Should().BeTrue();
        var own = await admin.ReserveBookAsync(new ReserveBookRequestDto {
            ClientRequestId = Guid.NewGuid(), BookReference = book.PublicId, StudentReference = sa.PublicId });
        own.Success.Should().BeTrue();
        var scoped = Service(db, role);
        var students = await scoped.SearchEligibleStudentsAsync("ST-", 1, 20);
        students.Success.Should().BeTrue();
        students.Data!.Items.Should().ContainSingle(x => x.StudentReference == sa.PublicId);
        var queue = await scoped.GetActiveReservationsForBookAsync(book.PublicId, 1, 20);
        queue.Success.Should().BeTrue();
        queue.Data!.Items.Should().ContainSingle(x => x.StudentReference == sa.PublicId);
        queue.Data.Items[0].CampusId.Should().Be(a.Id);
        (await scoped.ReserveBookAsync(new ReserveBookRequestDto {
            ClientRequestId = Guid.NewGuid(), BookReference = book.PublicId,
            StudentReference = sb.PublicId })).StatusCode.Should().Be(403);
        (await scoped.CancelReservationAsync(other.Data!.Id, "AQID")).StatusCode.Should().Be(403);
        (await scoped.FulfillReservationAsync(other.Data.Id, cb.Id, "AQID")).StatusCode.Should().Be(403);
        (await scoped.GetEligibleCopiesAsync(other.Data.Id, 1, 20)).StatusCode.Should().Be(403);
        var copies = await scoped.GetEligibleCopiesAsync(own.Data!.Id, 1, 20);
        copies.Success.Should().BeTrue();
        copies.Data!.Items.Should().ContainSingle(x => x.Id == ca.Id);
        var reservation = await db.BookReservations.FirstAsync(x => x.Id == own.Data.Id);
        reservation.RowVersion = [1, 2, 3];
        await db.SaveChangesAsync();
        var fulfilled = await scoped.FulfillReservationAsync(own.Data.Id, ca.Id,
            Convert.ToBase64String(reservation.RowVersion));
        fulfilled.Success.Should().BeTrue();
        (await db.BookIssues.CountAsync()).Should().Be(1);
    }
}
