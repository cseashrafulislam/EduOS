using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.HR;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Academic;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AcademicRoutineServiceTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);

    [Fact]
    public async Task Time_slot_and_routine_entry_creation_are_retry_safe()
    {
        var options = Options();
        var scope = await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new TestUser(101));
        var slotRequest = new SaveRoutineTimeSlotRequestDto
        { Name = "First Period", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 45) };
        var slot = await service.CreateTimeSlotAsync(slotRequest);
        var duplicateSlot = await service.CreateTimeSlotAsync(slotRequest);
        var assignment = await service.AssignInstructorAsync(Assign(scope.OfferingReference, scope.TeacherReference));
        var request = Entry(scope.OfferingReference, slot.Data!.Id, assignment.Data!.Id, scope.RoomId);
        var entry = await service.CreateEntryAsync(request);
        var retry = await service.CreateEntryAsync(request);
        slot.Success.Should().BeTrue();
        duplicateSlot.Data!.Id.Should().Be(slot.Data.Id);
        assignment.Success.Should().BeTrue();
        entry.Success.Should().BeTrue(entry.Message);
        entry.Data!.SubjectOfferingReference.Should().Be(scope.OfferingReference);
        retry.Success.Should().BeTrue();
        retry.Data!.Id.Should().Be(entry.Data.Id);
        (await db.RoutineEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Instructor_collision_across_batches_is_rejected()
    {
        var options = Options();
        var scope = await SeedAsync(options);
        await using var db = Context(options, 101);
        var second = new AcademicBatch { TenantId = 101, CampusId = scope.CampusId,
            AcademicYearId = scope.YearId, AcademicProgramId = scope.ProgramId,
            AcademicLevelId = scope.LevelId, Name = "Batch B", Code = "B-B", Capacity = 25 };
        db.AcademicBatches.Add(second); await db.SaveChangesAsync();
        var offering = new SubjectOffering { TenantId = 101, PublicId = Guid.NewGuid(),
            AcademicBatchId = second.Id, AcademicYearId = scope.YearId,
            CurriculumSubjectId = scope.CurriculumSubjectId, Code = "MATH-B" };
        db.Add(offering); await db.SaveChangesAsync();
        var service = Service(db, new TestUser(101));
        var slot = (await service.CreateTimeSlotAsync(new SaveRoutineTimeSlotRequestDto
        { Name = "First Period", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 45) })).Data!;
        var first = await service.AssignInstructorAsync(Assign(scope.OfferingReference, scope.TeacherReference));
        var other = await service.AssignInstructorAsync(Assign(offering.PublicId, scope.TeacherReference));
        first.Success.Should().BeTrue();
        other.Success.Should().BeTrue();
        (await service.CreateEntryAsync(Entry(scope.OfferingReference, slot.Id, first.Data!.Id))).Success
            .Should().BeTrue();
        var collision = await service.CreateEntryAsync(Entry(offering.PublicId, slot.Id, other.Data!.Id));
        collision.StatusCode.Should().Be(409);
        (await db.RoutineEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Other_tenant_cannot_assign_instructor_to_foreign_offering()
    {
        var options = Options();
        var scope = await SeedAsync(options);
        await using var db = Context(options, 202);
        var response = await Service(db, new TestUser(202))
            .AssignInstructorAsync(Assign(scope.OfferingReference, scope.TeacherReference));
        response.StatusCode.Should().Be(404);
        (await db.InstructorAssignments.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }


    [Fact]
    public async Task Principal_cannot_read_write_or_cancel_routines_outside_active_campus_grants()
    {
        var options = Options();
        var scope = await SeedAsync(options);
        await using var db = Context(options, 101);
        var secondCampus = new Campus { TenantId = 101, Name = "Other", Code = "OTHER" };
        db.Campuses.Add(secondCampus);
        await db.SaveChangesAsync();
        var otherBatch = new AcademicBatch
        {
            TenantId = 101, CampusId = secondCampus.Id, AcademicYearId = scope.YearId,
            AcademicProgramId = scope.ProgramId, AcademicLevelId = scope.LevelId,
            Name = "Other Campus", Code = "OTHER-B", Capacity = 25
        };
        db.AcademicBatches.Add(otherBatch);
        await db.SaveChangesAsync();
        var otherOffering = new SubjectOffering
        {
            TenantId = 101, PublicId = Guid.NewGuid(), AcademicBatchId = otherBatch.Id,
            AcademicYearId = scope.YearId, CurriculumSubjectId = scope.CurriculumSubjectId,
            Code = "OTHER-MATH"
        };
        db.SubjectOfferings.Add(otherOffering);
        db.UserCampusAccesses.Add(new UserCampusAccess
        {
            TenantId = 101, UserId = 7, CampusId = scope.CampusId, IsActive = true
        });
        await db.SaveChangesAsync();

        var admin = Service(db, new TestUser(101));
        var slot = (await admin.CreateTimeSlotAsync(new SaveRoutineTimeSlotRequestDto
        {
            Name = "First Period", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 45)
        })).Data!;
        var otherAssignment = await admin.AssignInstructorAsync(Assign(otherOffering.PublicId, scope.TeacherReference));
        otherAssignment.Success.Should().BeTrue();
        var otherEntry = await admin.CreateEntryAsync(
            Entry(otherOffering.PublicId, slot.Id, otherAssignment.Data!.Id));
        otherEntry.Success.Should().BeTrue(otherEntry.Message);

        var principal = Service(db, new TestUser(101, "Principal"));
        (await principal.GetAssignmentsAsync(otherBatch.Id, null)).StatusCode.Should().Be(404);
        (await principal.GetBatchTimetableAsync(otherBatch.Id, null)).StatusCode.Should().Be(404);
        (await principal.AssignInstructorAsync(Assign(otherOffering.PublicId, scope.TeacherReference)))
            .Success.Should().BeFalse();
        (await principal.CreateEntryAsync(Entry(otherOffering.PublicId, slot.Id, otherAssignment.Data.Id)))
            .Success.Should().BeFalse();
        (await principal.DeactivateEntryAsync(otherEntry.Data!.Id, "AQ==")).StatusCode.Should().Be(404);

        var teacherId = await db.Employees.Where(x => x.PublicId == scope.TeacherReference)
            .Select(x => x.Id).SingleAsync();
        var restrictedTimetable = await principal.GetTeacherTimetableAsync(teacherId, scope.YearId, null);
        restrictedTimetable.Success.Should().BeTrue();
        restrictedTimetable.Data.Should().BeEmpty();
        var adminTimetable = await admin.GetTeacherTimetableAsync(teacherId, scope.YearId, null);
        adminTimetable.Data.Should().ContainSingle(x => x.Id == otherEntry.Data.Id);

        var ownBatchId = await db.AcademicBatches.Where(x => x.CampusId == scope.CampusId)
            .Select(x => x.Id).SingleAsync();
        (await principal.GetAssignmentsAsync(ownBatchId, null)).Success.Should().BeTrue();
        (await db.RoutineEntries.SingleAsync()).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Principal_grants_must_remain_active_with_active_campus()
    {
        var options = Options();
        var scope = await SeedAsync(options);
        await using var db = Context(options, 101);
        var batchId = await db.AcademicBatches.Select(x => x.Id).SingleAsync();
        var inactive = new UserCampusAccess
        {
            TenantId = 101, UserId = 7, CampusId = scope.CampusId, IsActive = false
        };
        db.UserCampusAccesses.Add(inactive);
        await db.SaveChangesAsync();
        var principal = Service(db, new TestUser(101, "VicePrincipal"));
        (await principal.GetBatchTimetableAsync(batchId, null)).StatusCode.Should().Be(404);

        inactive.IsActive = true;
        await db.SaveChangesAsync();
        (await principal.GetBatchTimetableAsync(batchId, null)).Success.Should().BeTrue();

        var campus = await db.Campuses.SingleAsync(x => x.Id == scope.CampusId);
        campus.IsActive = false;
        await db.SaveChangesAsync();
        (await principal.GetBatchTimetableAsync(batchId, null)).StatusCode.Should().Be(404);
        (await Service(db, new TestUser(101)).GetBatchTimetableAsync(batchId, null))
            .Success.Should().BeTrue();
    }

    private static SaveInstructorAssignmentRequestDto Assign(Guid offering, Guid teacher) => new()
    {
        ClientRequestId = Guid.NewGuid(), SubjectOfferingReference = offering,
        EmployeeReference = teacher, IsPrimary = true, EffectiveFrom = Start
    };

    private static SaveRoutineEntryRequestDto Entry(Guid offering, long slot, long instructor, long? room = null) => new()
    {
        ClientRequestId = Guid.NewGuid(), SubjectOfferingReference = offering,
        InstructorAssignmentId = instructor, RoutineTimeSlotId = slot, RoomId = room,
        DayOfWeek = DayOfWeek.Monday, EffectiveFrom = Start
    };

    private static AcademicRoutineService Service(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<RoutineTimeSlot>(db),
        new GenericRepository<InstructorAssignment>(db),
        new GenericRepository<RoutineEntry>(db),
        new GenericRepository<AcademicBatch>(db),
        new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicTerm>(db),
        new GenericRepository<SubjectOffering>(db),
        new GenericRepository<CurriculumSubject>(db),
        new GenericRepository<Subject>(db),
        new GenericRepository<Employee>(db),
        new GenericRepository<Room>(db),
        new GenericRepository<UserCampusAccess>(db),
        new GenericRepository<Campus>(db), db, user,
        TimeProvider.System, NullLogger<AcademicRoutineService>.Instance);

    private static async Task<Scope> SeedAsync(DbContextOptions<EduOSDbContext> options)
    {
        await using var db = Context(options, 101);
        var campus = new Campus { TenantId = 101, Name = "Main", Code = "MAIN" };
        var year = new AcademicYear { TenantId = 101, Name = "2026", Code = "2026",
            StartDate = Start, EndDate = new DateOnly(2026, 12, 31), IsCurrent = true };
        var program = new AcademicProgram { TenantId = 101, Name = "Secondary", Code = "SEC" };
        db.AddRange(campus, year, program); await db.SaveChangesAsync();
        var level = new AcademicLevel { TenantId = 101, AcademicProgramId = program.Id,
            Name = "Class Nine", Code = "C9", LevelNo = 9 };
        var subject = new Subject { TenantId = 101, Name = "Math", Code = "MATH" };
        var teacher = new Employee { TenantId = 101, EmployeeCode = "T-001",
            FullName = "Teacher One", UserId = 70, Phone = "01700000000",
            DesignationId = 1, JoiningDate = new DateOnly(2020, 1, 1),
            CanTeach = true, State = EmployeeState.Active };
        var room = new Room { TenantId = 101, CampusId = campus.Id, Name = "Room 101",
            Code = "R101", Capacity = 40 };
        db.AddRange(level, subject, teacher, room); await db.SaveChangesAsync();
        var batch = new AcademicBatch { TenantId = 101, CampusId = campus.Id,
            AcademicYearId = year.Id, AcademicProgramId = program.Id,
            AcademicLevelId = level.Id, Name = "Batch A", Code = "B-A", Capacity = 30 };
        var curriculum = new AcademicCurriculum { TenantId = 101,
            AcademicProgramId = program.Id, Name = "Secondary 2026", Code = "SEC-2026",
            EffectiveFrom = Start, IsCurrent = true };
        db.AddRange(batch, curriculum); await db.SaveChangesAsync();
        var item = new CurriculumSubject { TenantId = 101,
            AcademicCurriculumId = curriculum.Id, AcademicLevelId = level.Id,
            SubjectId = subject.Id, FullMarks = 100m, PassMarks = 33m };
        db.Add(item); await db.SaveChangesAsync();
        var offering = new SubjectOffering { TenantId = 101, PublicId = Guid.NewGuid(),
            AcademicBatchId = batch.Id, CurriculumSubjectId = item.Id,
            AcademicYearId = year.Id, Code = "MATH-A" };
        db.Add(offering); await db.SaveChangesAsync();
        return new Scope(campus.Id, year.Id, program.Id, level.Id, item.Id,
            offering.PublicId, teacher.PublicId, room.Id);
    }

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>().UseInMemoryDatabase(
            "academic-routine-" + Guid.NewGuid().ToString("N")).Options;
    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed record Scope(long CampusId, long YearId, long ProgramId, long LevelId,
        long CurriculumSubjectId, Guid OfferingReference, Guid TeacherReference, long RoomId);
    private sealed class TestUser(long tenant, string role = "TenantAdmin") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => role;
        public string? Email => "admin@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
