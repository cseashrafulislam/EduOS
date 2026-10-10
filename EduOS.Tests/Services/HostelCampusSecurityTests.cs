using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Hostel;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Entities.Academic;
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

public sealed class HostelCampusSecurityTests
{
    [Fact]
    public async Task Warden_rooms_require_active_campus_grant()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("hostel-warden-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "7"),
                new Claim(ClaimTypes.Role, "HostelWarden"),
                new Claim("TenantId", "101")
            ], "TestAuthentication"))
        };
        http.Items["TenantId"] = 101L;
        await using var db = new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
        var north = new Campus { TenantId = 101, Name = "North", Code = "N" };
        var south = new Campus { TenantId = 101, Name = "South", Code = "S" };
        db.AddRange(north, south);
        await db.SaveChangesAsync();
        var grant = new UserCampusAccess { TenantId = 101, UserId = 7, CampusId = north.Id };
        db.Add(grant);
        var hostels = new[]
        {
            new Hostel { TenantId = 101, CampusId = north.Id, Name = "North hostel", Code = "NH" },
            new Hostel { TenantId = 101, CampusId = south.Id, Name = "South hostel", Code = "SH" }
        };
        db.AddRange(hostels);
        await db.SaveChangesAsync();
        var rooms = hostels.Select(h => new HostelRoom { TenantId = 101, HostelId = h.Id, RoomNumber = "101", Capacity = 1 }).ToArray();
        db.AddRange(rooms);
        await db.SaveChangesAsync();
        db.AddRange(rooms.Select(room => new HostelBed { TenantId = 101, HostelRoomId = room.Id, BedNumber = "A" }));
        var students = new[]
        {
            new Student { TenantId = 101, PersonId = 1, StudentCode = "N", FullName = "North student", StatusCode = "Active" },
            new Student { TenantId = 101, PersonId = 2, StudentCode = "S", FullName = "South student", StatusCode = "Active" }
        };
        db.AddRange(students);
        await db.SaveChangesAsync();
        db.AddRange(students.Select((student, index) => new StudentEnrollment
        {
            TenantId = 101, StudentId = student.Id, CampusId = index == 0 ? north.Id : south.Id,
            PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(), IsCurrent = true, State = EnrollmentState.Active,
            AcademicYearId = 1, AcademicProgramId = 1, AcademicLevelId = 1, AcademicBatchId = 1, AcademicCurriculumId = 1,
            RollNo = student.StudentCode
        }));
        await db.SaveChangesAsync();
        var service = CreateService(db);
        (await service.GetRoomsAsync()).Data!.Select(x => x.HostelName).Should().Equal("North hostel");
        (await service.GetAvailableBedsAsync(1, 25, null)).Data!.Items.Select(x => x.HostelName).Should().Equal("North hostel");
        (await service.GetEligibleStudentsAsync(1, 25, null)).Data!.Items.Select(x => x.StudentCode).Should().Equal("N");
        grant.IsActive = false;
        await db.SaveChangesAsync();
        (await service.GetRoomsAsync()).Data.Should().BeEmpty();
        (await service.GetAvailableBedsAsync(1, 25, null)).Data!.Items.Should().BeEmpty();
        (await service.GetEligibleStudentsAsync(1, 25, null)).Data!.Items.Should().BeEmpty();
    }


    [Fact]
    public async Task Warden_cannot_replay_allocate_close_or_list_other_campus_allocation()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("hostel-warden-mutation-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "HostelWarden"),
                new Claim("TenantId", "101")
            ], "TestAuthentication"))
        };
        http.Items["TenantId"] = 101L;
        await using var db = new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
        var north = new Campus { TenantId = 101, Name = "North", Code = "N" };
        var south = new Campus { TenantId = 101, Name = "South", Code = "S" };
        db.AddRange(north, south);
        await db.SaveChangesAsync();
        db.Add(new UserCampusAccess { TenantId = 101, UserId = 7, CampusId = north.Id });
        var hostel = new Hostel { TenantId = 101, CampusId = south.Id, Name = "South hostel", Code = "SH" };
        db.Add(hostel);
        await db.SaveChangesAsync();
        var room = new HostelRoom { TenantId = 101, HostelId = hostel.Id, RoomNumber = "101", Capacity = 1 };
        db.Add(room);
        await db.SaveChangesAsync();
        var student = new Student { TenantId = 101, PersonId = 1, StudentCode = "S", FullName = "South student", StatusCode = "Active" };
        var bed = new HostelBed { TenantId = 101, HostelRoomId = room.Id, BedNumber = "A" };
        db.AddRange(student, bed);
        await db.SaveChangesAsync();
        var enrollment = new StudentEnrollment
        {
            TenantId = 101, StudentId = student.Id, CampusId = south.Id,
            PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(), IsCurrent = true, State = EnrollmentState.Active,
            AcademicYearId = 1, AcademicProgramId = 1, AcademicLevelId = 1, AcademicBatchId = 1, AcademicCurriculumId = 1,
            RollNo = "S"
        };
        db.Add(enrollment);
        await db.SaveChangesAsync();
        var allocation = new StudentHostelAllocation
        {
            TenantId = 101, ClientRequestId = Guid.NewGuid(), StudentId = student.Id,
            StudentEnrollmentId = enrollment.Id, HostelBedId = bed.Id,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            State = HostelAllocationState.Active, RowVersion = [1, 2, 3, 4]
        };
        db.Add(allocation);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        (await service.GetActiveAllocationsAsync(1, 25, null)).Data!.Items.Should().BeEmpty();
        var request = new AllocateStudentHostelRequestDto
        {
            ClientRequestId = allocation.ClientRequestId, StudentEnrollmentReference = enrollment.PublicId,
            HostelBedId = bed.Id, StartDate = allocation.StartDate
        };
        (await service.AllocateAsync(request)).StatusCode.Should().Be(403);
        request.ClientRequestId = Guid.NewGuid();
        (await service.AllocateAsync(request)).StatusCode.Should().Be(403);
        var close = await service.CloseAsync(allocation.Id, new CloseStudentHostelAllocationRequestDto
        {
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow), RowVersion = Convert.ToBase64String(allocation.RowVersion)
        });
        close.StatusCode.Should().Be(403);
        (await db.Set<StudentHostelAllocation>().SingleAsync()).State.Should().Be(HostelAllocationState.Active);
    }

    private static HostelService CreateService(EduOSDbContext db) => new(
        new GenericRepository<Hostel>(db), new GenericRepository<HostelRoom>(db),
        new GenericRepository<HostelBed>(db), new GenericRepository<StudentHostelAllocation>(db),
        new GenericRepository<StudentEnrollment>(db), new GenericRepository<Student>(db),
        new GenericRepository<UserCampusAccess>(db), new GenericRepository<Campus>(db),
        new TestUnitOfWork(db), new Warden(), TimeProvider.System, NullLogger<HostelService>.Instance);

    private sealed class TestUnitOfWork(EduOSDbContext db) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default) =>
            operation(cancellationToken);

        public void Dispose() { }
    }
    private sealed class Warden : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => 101;
        public string? FullName => "Warden";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => ["HostelWarden"];
        public bool IsInRole(string role) => role == "HostelWarden";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
