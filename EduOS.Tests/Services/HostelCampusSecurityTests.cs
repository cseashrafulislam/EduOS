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
    public async Task Allocation_history_and_export_retain_retired_physical_inventory_without_leaking_other_campuses()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("hostel-retired-history-" + Guid.NewGuid().ToString("N")).Options;
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
        var campus = new Campus { TenantId = 101, Name = "Historic campus", Code = "HIS" };
        var other = new Campus { TenantId = 101, Name = "Other campus", Code = "OTH" };
        db.AddRange(campus, other);
        await db.SaveChangesAsync();
        db.Add(new UserCampusAccess { TenantId = 101, UserId = 7, CampusId = campus.Id });
        var hostel = new Hostel { TenantId = 101, CampusId = campus.Id, Name = "Historic hostel", Code = "HH" };
        var student = new Student { TenantId = 101, PersonId = 11, StudentCode = "HIST-STU",
            FullName = "Historical Student", StatusCode = "Active" };
        db.AddRange(hostel, student);
        await db.SaveChangesAsync();
        var room = new HostelRoom { TenantId = 101, HostelId = hostel.Id, RoomNumber = "101", Capacity = 1 };
        var enrollment = new StudentEnrollment
        {
            TenantId = 101, StudentId = student.Id, CampusId = campus.Id,
            ClientRequestId = Guid.NewGuid(), IsCurrent = false, State = EnrollmentState.Active,
            AcademicYearId = 1, AcademicProgramId = 1, AcademicLevelId = 1,
            AcademicBatchId = 1, AcademicCurriculumId = 1, RollNo = "11"
        };
        db.AddRange(room, enrollment);
        await db.SaveChangesAsync();
        var bed = new HostelBed { TenantId = 101, HostelRoomId = room.Id, BedNumber = "A" };
        db.Add(bed);
        await db.SaveChangesAsync();
        db.Add(new StudentHostelAllocation
        {
            TenantId = 101, ClientRequestId = Guid.NewGuid(), StudentId = student.Id,
            StudentEnrollmentId = enrollment.Id, HostelBedId = bed.Id,
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 2, 1),
            State = HostelAllocationState.Closed
        });
        await db.SaveChangesAsync();
        hostel.IsDeleted = true;
        room.IsDeleted = true;
        bed.IsDeleted = true;
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var history = await service.GetAllocationHistoryAsync(campus.Id, 1, 25, null, null, null, null);
        history.Success.Should().BeTrue(history.Message);
        history.Data!.Items.Should().ContainSingle(x => x.StudentCode == "HIST-STU" &&
            x.HostelName == "Historic hostel" && x.RoomNumber == "101" && x.BedNumber == "A");
        var export = await service.ExportAllocationHistoryCsvAsync(campus.Id, null, null, null, null);
        export.Success.Should().BeTrue(export.Message);
        export.Data.Should().Contain("Historic hostel").And.Contain("HIST-STU");
        export.Data.Should().NotContain("Historical Student");
        (await service.GetAllocationHistoryAsync(other.Id, 1, 25, null, null, null, null))
            .Data!.Items.Should().BeEmpty();
    }

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
        // A warden has 101 rooms in the permitted campus, plus an inaccessible south room.
        // The legacy compatibility endpoint must reject an oversized catalogue, not truncate it.
        db.AddRange(Enumerable.Range(0, 100).Select(i => new HostelRoom
        {
            TenantId = 101, HostelId = hostels[0].Id, RoomNumber = "EX-" + i, Capacity = 1
        }));
        await db.SaveChangesAsync();
        var legacyOverflow = await service.GetRoomsAsync();
        legacyOverflow.Success.Should().BeFalse();
        legacyOverflow.StatusCode.Should().Be(409);
        var pagedInventory = await service.GetRoomInventoryAsync(null, 1, 25, null);
        pagedInventory.Success.Should().BeTrue(pagedInventory.Message);
        pagedInventory.Data!.TotalCount.Should().Be(101);
        pagedInventory.Data.Items.Should().HaveCount(25);
        pagedInventory.Data.Items.Should().OnlyContain(x => x.HostelName == "North hostel");
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
        var reschedule = await service.RescheduleUpcomingAllocationStartAsync(allocation.Id,
            new RescheduleUpcomingHostelStartRequestDto
            {
                ClientRequestId = Guid.NewGuid(), StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(10)),
                RowVersion = Convert.ToBase64String(allocation.RowVersion)
            });
        reschedule.StatusCode.Should().Be(403);
        (await db.Set<StudentHostelAllocation>().SingleAsync()).State.Should().Be(HostelAllocationState.Active);
    }

    [Fact]
    public async Task No_charge_start_reschedule_is_durable_and_chargeable_stays_require_finance()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("hostel-prearrival-receipts-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "HostelWarden"),
                new Claim("TenantId", "101")
            ], "TestAuthentication"))
        };
        http.Items["TenantId"] = 101L;
        await using var db = new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        var campus = new Campus { TenantId = 101, Name = "Authorized campus", Code = "HC" };
        db.Campuses.Add(campus);
        await db.SaveChangesAsync();
        var hostel = new Hostel { TenantId = 101, CampusId = campus.Id, Name = "Hostel", Code = "H" };
        var grant = new UserCampusAccess { TenantId = 101, CampusId = campus.Id, UserId = 7 };
        var student = new Student { TenantId = 101, PersonId = 1,
            FullName = "Authorized student", StudentCode = "HC-ST", StatusCode = "Active" };
        db.AddRange(hostel, grant, student);
        await db.SaveChangesAsync();
        var room = new HostelRoom { TenantId = 101, HostelId = hostel.Id, RoomNumber = "R1", Capacity = 1 };
        var enrollment = new StudentEnrollment
        {
            TenantId = 101, StudentId = student.Id, CampusId = campus.Id,
            ClientRequestId = Guid.NewGuid(), IsCurrent = true, State = EnrollmentState.Active,
            AcademicYearId = 1, AcademicProgramId = 1, AcademicLevelId = 1,
            AcademicBatchId = 1, AcademicCurriculumId = 1, RollNo = "1"
        };
        db.AddRange(room, enrollment);
        await db.SaveChangesAsync();
        var bed = new HostelBed { TenantId = 101, HostelRoomId = room.Id, BedNumber = "A" };
        db.HostelBeds.Add(bed);
        await db.SaveChangesAsync();
        var allocation = new StudentHostelAllocation
        {
            TenantId = 101, ClientRequestId = Guid.NewGuid(), StudentId = student.Id,
            StudentEnrollmentId = enrollment.Id, HostelBedId = bed.Id,
            StartDate = today.AddDays(30), EndDate = today.AddDays(100),
            State = HostelAllocationState.Active, RowVersion = [1, 2, 3, 4, 5, 6, 7, 8]
        };
        db.StudentHostelAllocations.Add(allocation);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var version = Convert.ToBase64String(allocation.RowVersion);
        var key = Guid.NewGuid();
        var request = new RescheduleUpcomingHostelStartRequestDto
        {
            ClientRequestId = key, StartDate = today.AddDays(20), RowVersion = version
        };
        var first = await service.RescheduleUpcomingAllocationStartAsync(allocation.Id, request);
        first.Success.Should().BeTrue(first.Message);
        var updatedAt = allocation.UpdatedAt;
        var retry = await service.RescheduleUpcomingAllocationStartAsync(allocation.Id, request);
        retry.Success.Should().BeTrue(retry.Message);
        retry.Message.Should().Be(first.Message);
        allocation.UpdatedAt.Should().Be(updatedAt);
        (await db.IdempotencyRecords.CountAsync(x => x.Scope == "hostel.upcoming-start-reschedule"))
            .Should().Be(1);
        var changedPayload = await service.RescheduleUpcomingAllocationStartAsync(allocation.Id,
            new RescheduleUpcomingHostelStartRequestDto
            {
                ClientRequestId = key, StartDate = today.AddDays(18), RowVersion = version
            });
        changedPayload.StatusCode.Should().Be(409);

        allocation.MonthlyRent = 800m;
        await db.SaveChangesAsync();
        var chargeable = await service.RescheduleUpcomingAllocationStartAsync(allocation.Id,
            new RescheduleUpcomingHostelStartRequestDto
            {
                ClientRequestId = Guid.NewGuid(), StartDate = today.AddDays(18), RowVersion = version
            });
        chargeable.StatusCode.Should().Be(409);
        allocation.StartDate.Should().Be(today.AddDays(20));

        allocation.MonthlyRent = 0m;
        await db.SaveChangesAsync();
        var exactlyNinetyDays = await service.RescheduleUpcomingAllocationStartAsync(allocation.Id,
            new RescheduleUpcomingHostelStartRequestDto
            {
                ClientRequestId = Guid.NewGuid(), StartDate = today.AddDays(11), RowVersion = version
            });
        exactlyNinetyDays.Success.Should().BeTrue(exactlyNinetyDays.Message);
        var overNinetyDays = await service.RescheduleUpcomingAllocationStartAsync(allocation.Id,
            new RescheduleUpcomingHostelStartRequestDto
            {
                ClientRequestId = Guid.NewGuid(), StartDate = today.AddDays(5), RowVersion = version
            });
        overNinetyDays.StatusCode.Should().Be(409);

        // Separate zero-charge period corrections must also replay their original
        // successful result without changing the canonical audit timestamp.
        allocation.EndDate = today.AddDays(50);
        await db.SaveChangesAsync();
        var extensionKey = Guid.NewGuid();
        var extension = new ExtendNoChargeHostelStayRequestDto
        {
            ClientRequestId = extensionKey, EndDate = today.AddDays(55), RowVersion = version
        };
        var extended = await service.ExtendNoChargeStayAsync(allocation.Id, extension);
        extended.Success.Should().BeTrue(extended.Message);
        var extensionAudit = allocation.UpdatedAt;
        var extendedReplay = await service.ExtendNoChargeStayAsync(allocation.Id, extension);
        extendedReplay.Success.Should().BeTrue(extendedReplay.Message);
        extendedReplay.Message.Should().Be(extended.Message);
        allocation.UpdatedAt.Should().Be(extensionAudit);
        (await db.IdempotencyRecords.CountAsync(x => x.Scope == "hostel.no-charge-stay-extension"))
            .Should().Be(1);
        (await service.ExtendNoChargeStayAsync(allocation.Id, new ExtendNoChargeHostelStayRequestDto
        {
            ClientRequestId = extensionKey, EndDate = today.AddDays(56), RowVersion = version
        })).StatusCode.Should().Be(409);

        var shorteningKey = Guid.NewGuid();
        var shortening = new ShortenUpcomingNoChargeHostelStayRequestDto
        {
            ClientRequestId = shorteningKey, EndDate = today.AddDays(45), RowVersion = version
        };
        var shortened = await service.ShortenUpcomingNoChargeStayAsync(allocation.Id, shortening);
        shortened.Success.Should().BeTrue(shortened.Message);
        var shorteningAudit = allocation.UpdatedAt;
        var shortenedReplay = await service.ShortenUpcomingNoChargeStayAsync(allocation.Id, shortening);
        shortenedReplay.Success.Should().BeTrue(shortenedReplay.Message);
        shortenedReplay.Message.Should().Be(shortened.Message);
        allocation.UpdatedAt.Should().Be(shorteningAudit);
        (await db.IdempotencyRecords.CountAsync(x => x.Scope == "hostel.upcoming-no-charge-shortening"))
            .Should().Be(1);
        (await service.ShortenUpcomingNoChargeStayAsync(allocation.Id,
            new ShortenUpcomingNoChargeHostelStayRequestDto
            {
                ClientRequestId = shorteningKey, EndDate = today.AddDays(44), RowVersion = version
            })).StatusCode.Should().Be(409);
    }

    private static HostelService CreateService(EduOSDbContext db) => new(
        new GenericRepository<Hostel>(db), new GenericRepository<HostelRoom>(db),
        new GenericRepository<HostelBed>(db), new GenericRepository<StudentHostelAllocation>(db),
        new GenericRepository<StudentEnrollment>(db), new GenericRepository<Student>(db),
        new GenericRepository<UserCampusAccess>(db), new GenericRepository<Campus>(db),
        new TestUnitOfWork(db), new Warden(), TimeProvider.System,
        NullLogger<HostelService>.Instance, new EduOS.Service.Services.Finance.ReservationFinancePolicy(db),
        new EduOS.Service.Services.Platform.ReservationCommandReceiptStore(db, TimeProvider.System),
        new GenericRepository<HostelApplication>(db), new GenericRepository<HostelApplicationEvent>(db),
        new GenericRepository<HostelApplicationAuthority>(db), new GenericRepository<StudentGuardian>(db),
        new GenericRepository<Guardian>(db), new GenericRepository<EduOS.Core.Entities.System.AuditLog>(db),
        new GenericRepository<EduOS.Core.Entities.Finance.HostelFinanceClearance>(db));

    private sealed class TestUnitOfWork(EduOSDbContext db) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
        // Test-only EF InMemory provider has no relational isolation level.
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation, System.Data.IsolationLevel isolationLevel,
            CancellationToken cancellationToken = default) => operation(cancellationToken);

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
