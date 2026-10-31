using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Hostel;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Hostel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelMyHistoryIsolationTests
{
    [Fact]
    public async Task Own_archived_stay_survives_retired_bed_without_leaking_other_students_or_tenants()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("hostel-my-history-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "41"),
                new Claim(ClaimTypes.Role, "Student"),
                new Claim("TenantId", "101")
            ], "TestAuthentication"))
        };
        http.Items["TenantId"] = 101L;
        await using var db = new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
        var campus = new Campus { TenantId = 101, Name = "My campus", Code = "MY" };
        db.Add(campus);
        await db.SaveChangesAsync();
        var hostel = new Hostel { TenantId = 101, CampusId = campus.Id, Name = "My hostel", Code = "MYH" };
        var mine = new Student { TenantId = 101, UserId = 41, PersonId = 1,
            FullName = "My student", StudentCode = "OWN", StatusCode = "Inactive" };
        var other = new Student { TenantId = 101, UserId = 42, PersonId = 2,
            FullName = "Other student", StudentCode = "OTHER" };
        db.AddRange(hostel, mine, other);
        await db.SaveChangesAsync();
        var room = new HostelRoom { TenantId = 101, HostelId = hostel.Id, RoomNumber = "A", Capacity = 2 };
        db.Add(room);
        await db.SaveChangesAsync();
        var bed = new HostelBed { TenantId = 101, HostelRoomId = room.Id, BedNumber = "1" };
        var otherBed = new HostelBed { TenantId = 101, HostelRoomId = room.Id, BedNumber = "2" };
        db.AddRange(bed, otherBed);
        await db.SaveChangesAsync();
        StudentEnrollment Enrol(long tenant, Student student, Campus place) => new()
        {
            TenantId = tenant, StudentId = student.Id, CampusId = place.Id,
            ClientRequestId = Guid.NewGuid(), IsCurrent = false, State = EnrollmentState.Active,
            AcademicYearId = 1, AcademicProgramId = 1, AcademicLevelId = 1,
            AcademicBatchId = 1, AcademicCurriculumId = 1, RollNo = student.StudentCode
        };
        var ownEnrollment = Enrol(101, mine, campus);
        var otherEnrollment = Enrol(101, other, campus);
        db.AddRange(ownEnrollment, otherEnrollment);
        await db.SaveChangesAsync();
        StudentHostelAllocation Stay(long tenant, Student student, StudentEnrollment enrollment, HostelBed slot) => new()
        {
            TenantId = tenant, ClientRequestId = Guid.NewGuid(),
            StudentId = student.Id, StudentEnrollmentId = enrollment.Id, HostelBedId = slot.Id,
            StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 2, 1),
            State = HostelAllocationState.Closed
        };
        db.AddRange(Stay(101, mine, ownEnrollment, bed),
            Stay(101, other, otherEnrollment, otherBed));
        await db.SaveChangesAsync();
        hostel.IsDeleted = true; room.IsDeleted = true; bed.IsDeleted = true;
        await db.SaveChangesAsync();

        // The write guard intentionally rejects cross-tenant inserts through the
        // 101-scoped context. Seed the other institute using its own authenticated
        // tenant context against the same in-memory store.
        var foreignHttp = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "41"),
                new Claim(ClaimTypes.Role, "Student"),
                new Claim("TenantId", "202")
            ], "TestAuthentication"))
        };
        foreignHttp.Items["TenantId"] = 202L;
        await using (var foreignDb = new EduOSDbContext(options,
            new HttpContextAccessor { HttpContext = foreignHttp }))
        {
            var foreignCampus = new Campus { TenantId = 202, Name = "Foreign campus", Code = "FOREIGN" };
            foreignDb.Add(foreignCampus);
            await foreignDb.SaveChangesAsync();
            var foreignHostel = new Hostel { TenantId = 202, CampusId = foreignCampus.Id,
                Name = "Foreign hostel", Code = "FH" };
            var foreign = new Student { TenantId = 202, UserId = 41, PersonId = 3,
                FullName = "Foreign student", StudentCode = "FOREIGN" };
            foreignDb.AddRange(foreignHostel, foreign);
            await foreignDb.SaveChangesAsync();
            var foreignRoom = new HostelRoom { TenantId = 202, HostelId = foreignHostel.Id,
                RoomNumber = "X", Capacity = 1 };
            var foreignEnrollment = Enrol(202, foreign, foreignCampus);
            foreignDb.AddRange(foreignRoom, foreignEnrollment);
            await foreignDb.SaveChangesAsync();
            var foreignBed = new HostelBed { TenantId = 202, HostelRoomId = foreignRoom.Id, BedNumber = "9" };
            foreignDb.Add(foreignBed);
            await foreignDb.SaveChangesAsync();
            foreignDb.Add(Stay(202, foreign, foreignEnrollment, foreignBed));
            await foreignDb.SaveChangesAsync();
        }

        var service = new HostelService(
            new GenericRepository<Hostel>(db), new GenericRepository<HostelRoom>(db),
            new GenericRepository<HostelBed>(db), new GenericRepository<StudentHostelAllocation>(db),
            new GenericRepository<StudentEnrollment>(db), new GenericRepository<Student>(db),
            new GenericRepository<UserCampusAccess>(db), new GenericRepository<Campus>(db),
            new TestUnitOfWork(db), new StudentUser(), TimeProvider.System,
            NullLogger<HostelService>.Instance,
            new EduOS.Service.Services.Finance.ReservationFinancePolicy(db),
            new EduOS.Service.Services.Platform.ReservationCommandReceiptStore(db, TimeProvider.System),
            new GenericRepository<HostelApplication>(db), new GenericRepository<HostelApplicationEvent>(db),
            new GenericRepository<HostelApplicationAuthority>(db), new GenericRepository<StudentGuardian>(db),
            new GenericRepository<Guardian>(db), new GenericRepository<EduOS.Core.Entities.System.AuditLog>(db),
            new GenericRepository<EduOS.Core.Entities.Finance.HostelFinanceClearance>(db));
        var result = await service.GetMyAllocationHistoryAsync(1, 20, null);
        Assert.True(result.Success, result.Message);
        var own = Assert.Single(result.Data!.Items);
        Assert.Equal("OWN", own.StudentCode);
        Assert.Equal("My hostel", own.HostelName);
        Assert.Equal("1", own.BedNumber);
        Assert.Equal(1, result.Data.TotalCount);

        var csv = await service.ExportMyAllocationHistoryPageCsvAsync(1, HostelAllocationState.Closed);
        Assert.True(csv.Success, csv.Message);
        Assert.Contains("My hostel", csv.Data);
        Assert.DoesNotContain("Other student", csv.Data);
        Assert.DoesNotContain("Foreign hostel", csv.Data);
        Assert.Equal(400, (await service.GetMyAllocationHistoryAsync(0, 20, null)).StatusCode);
        Assert.Equal(400, (await service.GetMyAllocationHistoryAsync(1, 51, null)).StatusCode);
        Assert.Equal(400, (await service.GetMyAllocationHistoryAsync(1, 20, (HostelAllocationState)99)).StatusCode);
    }

    private sealed class TestUnitOfWork(EduOSDbContext db) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation, System.Data.IsolationLevel isolationLevel,
            CancellationToken cancellationToken = default) => operation(cancellationToken);
        public void Dispose() { }
    }

    private sealed class StudentUser : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 41;
        public long TenantId => 101;
        public string? FullName => "Student";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => ["Student"];
        public bool IsInRole(string role) => role == "Student";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
