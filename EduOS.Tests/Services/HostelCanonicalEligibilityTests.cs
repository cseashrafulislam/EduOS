using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Hostel;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Hostel;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelCanonicalEligibilityTests
{
    [Fact]
    public async Task Eligible_students_excludes_transferred_noncurrent_and_other_tenant()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("hostel-eligibility-" + Guid.NewGuid().ToString("N")).Options;
        await using (var other = Context(202, options))
        {
            var foreign = Student(202, "Active", "foreign");
            other.Add(foreign);
            await other.SaveChangesAsync();
            other.Add(Enrollment(foreign, true));
            await other.SaveChangesAsync();
        }
        await using var db = Context(101, options);
        var eligible = Student(101, "Active", "eligible");
        var transferred = Student(101, "Transferred", "transferred");
        var previous = Student(101, "Active", "previous");
        db.AddRange(eligible, transferred, previous);
        await db.SaveChangesAsync();
        db.AddRange(Enrollment(eligible, true), Enrollment(transferred, true), Enrollment(previous, false));
        await db.SaveChangesAsync();

        var response = await Service(db).GetEligibleStudentsAsync(1, 25, null);

        response.Success.Should().BeTrue();
        response.Data!.TotalCount.Should().Be(1);
        response.Data.Items.Should().ContainSingle(x => x.StudentCode == eligible.StudentCode);
    }

    [Fact]
    public async Task Transferred_student_cannot_read_historical_active_allocation()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("hostel-self-" + Guid.NewGuid().ToString("N")).Options;
        await using var db = Context(101, options);
        var student = Student(101, "Transferred", "exited");
        student.UserId = 7;
        db.Add(student);
        await db.SaveChangesAsync();
        db.Add(new StudentHostelAllocation
        {
            TenantId = 101, StudentId = student.Id, StudentEnrollmentId = 99,
            HostelBedId = 99, ClientRequestId = Guid.NewGuid(),
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            State = HostelAllocationState.Active
        });
        await db.SaveChangesAsync();

        var response = await Service(db).GetMyAllocationAsync();

        response.Success.Should().BeTrue();
        response.Data.Should().BeNull();
    }

    private static Student Student(long tenant, string status, string code) => new()
    {
        TenantId = tenant, PublicId = Guid.NewGuid(), PersonId = 1,
        StudentCode = "HOSTEL-" + code, FullName = "Learner " + code,
        StatusCode = status, AdmissionDate = DateOnly.FromDateTime(DateTime.UtcNow)
    };

    private static StudentEnrollment Enrollment(Student student, bool current) => new()
    {
        TenantId = student.TenantId, StudentId = student.Id, CampusId = 1,
        AcademicYearId = 1, AcademicProgramId = 1, AcademicLevelId = 1,
        AcademicBatchId = 1, AcademicCurriculumId = 1,
        PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(),
        RollNo = student.StudentCode, EnrollmentDate = DateOnly.FromDateTime(DateTime.UtcNow),
        IsCurrent = current, State = EnrollmentState.Active
    };

    [Fact]
    public async Task Student_cannot_enumerate_hostel_rooms_occupancy_or_rent()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("hostel-read-denied-" + Guid.NewGuid().ToString("N")).Options;
        await using var db = Context(101, options);
        var response = await Service(db, false).GetRoomsAsync();
        response.Success.Should().BeFalse();
        response.StatusCode.Should().Be(403);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, true)]
    [InlineData(1, 1, false)]
    [InlineData(2, 1, true)]
    [InlineData(2, 2, false)]
    public async Task Available_beds_respect_active_allocation_count_and_room_capacity(
        int capacity, int activeCount, bool expectedAvailable)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("hostel-capacity-" + Guid.NewGuid().ToString("N")).Options;
        await using var db = Context(101, options);
        var hostel = new EduOS.Core.Entities.Hostel.Hostel
        {
            TenantId = 101, CampusId = 1, Name = "Main Hostel", Code = "MH"
        };
        db.Add(hostel);
        await db.SaveChangesAsync();
        var room = new HostelRoom
        {
            TenantId = 101, HostelId = hostel.Id, RoomNumber = "101",
            Capacity = capacity, IsActive = true, RentPerBed = 100m
        };
        db.Add(room);
        await db.SaveChangesAsync();
        var occupiedBeds = Enumerable.Range(0, activeCount).Select(i => new HostelBed
        {
            TenantId = 101, HostelRoomId = room.Id, BedNumber = "O" + i, IsActive = true
        }).ToList();
        var availableBed = new HostelBed
        {
            TenantId = 101, HostelRoomId = room.Id, BedNumber = "AVAILABLE", IsActive = true
        };
        db.AddRange(occupiedBeds);
        db.Add(availableBed);
        await db.SaveChangesAsync();
        for (var i = 0; i < occupiedBeds.Count; i++)
            db.Add(new StudentHostelAllocation
            {
                TenantId = 101, StudentId = i + 100, StudentEnrollmentId = i + 200,
                HostelBedId = occupiedBeds[i].Id, ClientRequestId = Guid.NewGuid(),
                StartDate = new DateOnly(2026, 10, 1), State = HostelAllocationState.Active
            });
        await db.SaveChangesAsync();

        var result = await Service(db).GetAvailableBedsAsync(1, 20, null);

        result.Success.Should().BeTrue();
        result.Data!.TotalCount.Should().Be(expectedAvailable ? 1 : 0);
        if (expectedAvailable)
            result.Data.Items.Should().ContainSingle(x => x.BedId == availableBed.Id);
        else
            result.Data.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HostelAllocationState.Closed, 0, true)]
    [InlineData(HostelAllocationState.Closed, 1, false)]
    [InlineData(HostelAllocationState.Cancelled, 0, false)]
    public async Task Close_replay_requires_matching_date_and_closed_state(
        HostelAllocationState state, int offsetDays, bool expectedSuccess)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("hostel-close-" + Guid.NewGuid().ToString("N")).Options;
        await using var db = Context(101, options);
        var closedOn = new DateOnly(2026, 10, 8);
        var allocation = new StudentHostelAllocation
        {
            TenantId = 101, StudentId = 99, StudentEnrollmentId = 99, HostelBedId = 99,
            ClientRequestId = Guid.NewGuid(), StartDate = closedOn.AddDays(-10),
            EndDate = state == HostelAllocationState.Closed ? closedOn : null,
            State = state, RowVersion = [1,2,3,4,5,6,7,8]
        };
        db.Add(allocation);
        await db.SaveChangesAsync();
        var originalEndDate = allocation.EndDate;
        var result = await Service(db).CloseAsync(allocation.Id, new CloseStudentHostelAllocationRequestDto
        {
            EndDate = closedOn.AddDays(offsetDays),
            RowVersion = Convert.ToBase64String([9,9,9,9,9,9,9,9])
        });
        result.Success.Should().Be(expectedSuccess);
        if (!expectedSuccess) result.StatusCode.Should().Be(409);
        var saved = await db.Set<StudentHostelAllocation>().SingleAsync(x => x.Id == allocation.Id);
        saved.State.Should().Be(state);
        saved.EndDate.Should().Be(originalEndDate);
    }

    [Fact]
    public async Task Tenant_admin_can_read_hostel_room_catalogue()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("hostel-read-allowed-" + Guid.NewGuid().ToString("N")).Options;
        await using var db = Context(101, options);
        (await Service(db).GetRoomsAsync()).Success.Should().BeTrue();
    }

    private static HostelService Service(EduOSDbContext db, bool canManage = true, string? role = null) => new(
        new GenericRepository<EduOS.Core.Entities.Hostel.Hostel>(db),
        new GenericRepository<HostelRoom>(db), new GenericRepository<HostelBed>(db),
        new GenericRepository<StudentHostelAllocation>(db),
        new GenericRepository<StudentEnrollment>(db), new GenericRepository<Student>(db),
        new GenericRepository<UserCampusAccess>(db), new GenericRepository<Campus>(db),
        new UnitOfWork(db), new CurrentUser(canManage, role), TimeProvider.System, NullLogger<HostelService>.Instance);

    private static EduOSDbContext Context(long tenant, DbContextOptions<EduOSDbContext> options)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "7"),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", tenant.ToString())
            }, "TestAuthentication"))
        };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed class UnitOfWork(EduOSDbContext db) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default) =>
            operation(cancellationToken);

        public void Dispose() { }
    }

    private sealed class CurrentUser(bool canManage, string? role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => 101;
        public string? FullName => "Hostel Test";
        public string? Email => "hostel@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => canManage && (role is null or "TenantAdmin");
        public IReadOnlyList<string> Roles => canManage ? new[] { role ?? "TenantAdmin" } : new[] { "Student" };
        public bool IsInRole(string requestedRole) => canManage && requestedRole == (role ?? "TenantAdmin");
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
