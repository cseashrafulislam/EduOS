using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Employees;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Academic;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public class AcademicRoutineServiceTests
{
    [Fact]
    public async Task Routine_creation_is_retry_safe_and_derives_assignment_data()
    {
        var options = CreateOptions();
        var seeded = await SeedAsync(options);
        await using var context = CreateContext(options, 101, "TenantAdmin", 7);
        var service = CreateService(context, new TestCurrentUser(101, 7, "TenantAdmin"));

        var slot = await service.CreateTimeSlotAsync(new CreateRoutineTimeSlotDto { Name = "Period 1", StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(9, 45, 0) });
        var assignment = await service.AssignInstructorAsync(new AssignInstructorDto { AcademicBatchId = seeded.BatchId, SubjectId = seeded.SubjectId, EmployeeId = seeded.TeacherId, IsPrimary = true });
        var request = new CreateRoutineEntryDto { InstructorAssignmentId = assignment.Data!.Id, RoutineTimeSlotId = slot.Data!.Id, DayOfWeek = DayOfWeek.Sunday, RoomId = seeded.RoomId };

        var first = await service.CreateEntryAsync(request);
        var replay = await service.CreateEntryAsync(request);

        first.Success.Should().BeTrue();
        first.StatusCode.Should().Be(201);
        first.Data!.AcademicYearId.Should().Be(seeded.AcademicYearId);
        first.Data.AcademicBatchId.Should().Be(seeded.BatchId);
        first.Data.SubjectId.Should().Be(seeded.SubjectId);
        first.Data.EmployeeId.Should().Be(seeded.TeacherId);
        replay.Success.Should().BeTrue();
        replay.Data!.Id.Should().Be(first.Data.Id);
        (await context.RoutineEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Teacher_collision_is_rejected_across_batches()
    {
        var options = CreateOptions();
        var seeded = await SeedAsync(options);
        await using var context = CreateContext(options, 101, "TenantAdmin", 7);
        var secondBatch = new AcademicBatch { TenantId = 101, CampusId = 1, AcademicYearId = seeded.AcademicYearId, AcademicProgramId = seeded.ProgramId, AcademicLevelId = seeded.LevelId, Name = "Batch B", Code = "B-B", Capacity = 25, IsActive = true };
        context.AcademicBatches.Add(secondBatch);
        await context.SaveChangesAsync();
        var service = CreateService(context, new TestCurrentUser(101, 7, "TenantAdmin"));
        var slot = (await service.CreateTimeSlotAsync(new CreateRoutineTimeSlotDto { Name = "Period 1", StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(9, 45, 0) })).Data!;
        var firstAssignment = (await service.AssignInstructorAsync(new AssignInstructorDto { AcademicBatchId = seeded.BatchId, SubjectId = seeded.SubjectId, EmployeeId = seeded.TeacherId })).Data!;
        var secondAssignment = (await service.AssignInstructorAsync(new AssignInstructorDto { AcademicBatchId = secondBatch.Id, SubjectId = seeded.SubjectId, EmployeeId = seeded.TeacherId })).Data!;
        (await service.CreateEntryAsync(new CreateRoutineEntryDto { InstructorAssignmentId = firstAssignment.Id, RoutineTimeSlotId = slot.Id, DayOfWeek = DayOfWeek.Monday, RoomId = seeded.RoomId })).Success.Should().BeTrue();

        var conflict = await service.CreateEntryAsync(new CreateRoutineEntryDto { InstructorAssignmentId = secondAssignment.Id, RoutineTimeSlotId = slot.Id, DayOfWeek = DayOfWeek.Monday });

        conflict.Success.Should().BeFalse();
        conflict.StatusCode.Should().Be(409);
        conflict.Message.Should().Contain("Instructor");
        (await context.RoutineEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Cross_tenant_batch_is_neutral_not_found()
    {
        var options = CreateOptions();
        var seeded = await SeedAsync(options);
        await using var context = CreateContext(options, 202, "TenantAdmin", 8);
        var service = CreateService(context, new TestCurrentUser(202, 8, "TenantAdmin"));

        var response = await service.AssignInstructorAsync(new AssignInstructorDto { AcademicBatchId = seeded.BatchId, SubjectId = seeded.SubjectId, EmployeeId = seeded.TeacherId });

        response.Success.Should().BeFalse();
        response.StatusCode.Should().Be(404);
        (await context.InstructorAssignments.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    private static AcademicRoutineService CreateService(EduOSDbContext context, ICurrentUserService currentUser) => new(
        new GenericRepository<RoutineTimeSlot>(context),
        new GenericRepository<InstructorAssignment>(context),
        new GenericRepository<RoutineEntry>(context),
        new GenericRepository<AcademicBatch>(context),
        new GenericRepository<AcademicTerm>(context),
        new GenericRepository<AcademicCurriculum>(context),
        new GenericRepository<CurriculumSubject>(context),
        new GenericRepository<Subject>(context),
        new GenericRepository<Employee>(context),
        new GenericRepository<Room>(context),
        context,
        currentUser,
        TimeProvider.System,
        NullLogger<AcademicRoutineService>.Instance);

    private static async Task<SeededAcademic> SeedAsync(DbContextOptions<EduOSDbContext> options)
    {
        await using var context = CreateContext(options, 101, "TenantAdmin", 7);
        var year = new AcademicYear { TenantId = 101, Name = "2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsCurrent = true, IsActive = true };
        var legacyClass = new Class { TenantId = 101, Name = "Class Nine", NumericValue = 9, IsActive = true };
        var program = new AcademicProgram { TenantId = 101, CampusId = 1, Name = "Secondary", Code = "SEC", IsActive = true };
        context.AddRange(year, legacyClass, program);
        await context.SaveChangesAsync();
        var level = new AcademicLevel { TenantId = 101, AcademicProgramId = program.Id, AcademicProgram = program, Name = "Class Nine", Code = "C9", LevelNo = 9, IsActive = true };
        var subject = new Subject { TenantId = 101, ClassId = legacyClass.Id, Class = legacyClass, Name = "Mathematics", Code = "MATH", IsActive = true };
        var teacher = new Employee { TenantId = 101, UserId = 70, EmployeeCode = "T-001", FullName = "Teacher One", Phone = "01700000000", DesignationId = 1, JoiningDate = new DateTime(2020, 1, 1), IsTeacher = true, IsActive = true };
        var room = new Room { TenantId = 101, CampusId = 1, Name = "Room 101", Code = "R101", Capacity = 40, IsActive = true };
        context.AddRange(level, subject, teacher, room);
        await context.SaveChangesAsync();
        var batch = new AcademicBatch { TenantId = 101, CampusId = 1, AcademicYearId = year.Id, AcademicProgramId = program.Id, AcademicLevelId = level.Id, Name = "Batch A", Code = "B-A", Capacity = 30, IsActive = true };
        var curriculum = new AcademicCurriculum { TenantId = 101, AcademicProgramId = program.Id, Name = "Secondary 2026", Code = "SEC-2026", EffectiveFromAcademicYearId = year.Id, IsCurrent = true, IsActive = true };
        context.AddRange(batch, curriculum);
        await context.SaveChangesAsync();
        context.CurriculumSubjects.Add(new CurriculumSubject { TenantId = 101, AcademicCurriculumId = curriculum.Id, AcademicLevelId = level.Id, SubjectId = subject.Id, FullMarks = 100, PassMarks = 33, IsActive = true });
        await context.SaveChangesAsync();
        return new SeededAcademic(year.Id, program.Id, level.Id, batch.Id, subject.Id, teacher.Id, room.Id);
    }

    private static DbContextOptions<EduOSDbContext> CreateOptions() => new DbContextOptionsBuilder<EduOSDbContext>().UseInMemoryDatabase($"academic-routine-{Guid.NewGuid():N}").Options;

    private static EduOSDbContext CreateContext(DbContextOptions<EduOSDbContext> options, long tenantId, string role, long userId)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role)], "TestAuthentication")) };
        http.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed record SeededAcademic(long AcademicYearId, long ProgramId, long LevelId, long BatchId, long SubjectId, long TeacherId, long RoomId);

    private sealed class TestCurrentUser(long tenantId, long userId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => "Academic User";
        public string? Email => "academic@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "EduOS tests";
    }
}
