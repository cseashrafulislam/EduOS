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

public class AcademicInstructionServiceTests
{
    [Fact]
    public async Task Substitution_is_retry_safe_visible_to_participants_and_prevents_overlap()
    {
        var options = CreateOptions();
        var seed = await SeedAsync(options);
        long substitutionId;
        await using (var managerContext = CreateContext(options, 101, 7, "TenantAdmin"))
        {
            var manager = CreateService(managerContext, new TestCurrentUser(101, 7, "TenantAdmin"));
            var request = new CreateRoutineSubstitutionDto
            {
                ClientRequestId = Guid.NewGuid(),
                RoutineEntryId = seed.RoutineEntryId,
                Date = new DateTime(2026, 9, 20),
                SubstituteTeacherId = seed.SubstituteTeacherId,
                Reason = "Original instructor on approved leave"
            };
            var created = await manager.CreateSubstitutionAsync(request);
            var replay = await manager.CreateSubstitutionAsync(request);
            var collision = await manager.CreateSubstitutionAsync(new CreateRoutineSubstitutionDto
            {
                ClientRequestId = Guid.NewGuid(),
                RoutineEntryId = seed.OverlappingRoutineEntryId,
                Date = new DateTime(2026, 9, 20),
                SubstituteTeacherId = seed.SubstituteTeacherId,
                Reason = "Coverage needed"
            });

            created.StatusCode.Should().Be(201);
            replay.Data!.Id.Should().Be(created.Data!.Id);
            collision.Success.Should().BeFalse();
            collision.StatusCode.Should().Be(409);
            collision.Message.Should().Contain("another class");
            substitutionId = created.Data.Id;
            (await managerContext.Substitutions.CountAsync()).Should().Be(1);
        }

        await using (var substituteContext = CreateContext(options, 101, 71, "Teacher"))
        {
            var substitute = CreateService(substituteContext, new TestCurrentUser(101, 71, "Teacher"));
            var own = await substitute.GetSubstitutionsAsync(new DateTime(2026, 9, 20), new DateTime(2026, 9, 20), seed.BatchId);
            own.Data.Should().ContainSingle();
            own.Data!.Single().Id.Should().Be(substitutionId);
        }

        await using (var unrelatedContext = CreateContext(options, 101, 72, "Teacher"))
        {
            var unrelated = CreateService(unrelatedContext, new TestCurrentUser(101, 72, "Teacher"));
            var rows = await unrelated.GetSubstitutionsAsync(new DateTime(2026, 9, 20), new DateTime(2026, 9, 20), seed.BatchId);
            rows.Data.Should().BeEmpty();
        }

        var cancellationVersion = await SetSubstitutionVersionAsync(options, substitutionId, [8, 7, 6, 5, 4, 3, 2, 1]);
        await using (var managerContext = CreateContext(options, 101, 7, "TenantAdmin"))
        {
            var manager = CreateService(managerContext, new TestCurrentUser(101, 7, "TenantAdmin"));
            var request = new CancelRoutineSubstitutionDto { Reason = "Leave was withdrawn", RowVersion = cancellationVersion };
            var cancelled = await manager.CancelSubstitutionAsync(substitutionId, request);
            var replay = await manager.CancelSubstitutionAsync(substitutionId, request);
            var conflictingReplay = await manager.CancelSubstitutionAsync(substitutionId, new CancelRoutineSubstitutionDto { Reason = "Different reason", RowVersion = cancellationVersion });

            cancelled.Data!.IsActive.Should().BeFalse();
            cancelled.Data.CancellationReason.Should().Be(request.Reason);
            replay.Success.Should().BeTrue();
            conflictingReplay.StatusCode.Should().Be(409);
        }
    }

    [Fact]
    public async Task Assigned_teacher_can_complete_approved_lesson_plan_lifecycle()
    {
        var options = CreateOptions();
        var seed = await SeedAsync(options);
        long planId;
        await using (var teacherContext = CreateContext(options, 101, 70, "Teacher"))
        {
            var teacher = CreateService(teacherContext, new TestCurrentUser(101, 70, "Teacher"));
            var created = await teacher.CreateLessonPlanAsync(new CreateLessonPlanDto
            {
                ClientRequestId = Guid.NewGuid(),
                InstructorAssignmentId = seed.AssignmentId,
                ChapterName = "Linear equations",
                Topic = "One-variable equations",
                StartDate = new DateTime(2026, 9, 20),
                EndDate = new DateTime(2026, 9, 24),
                LearningObjectives = "Solve and verify linear equations",
                Resources = "Textbook chapter 4"
            });
            created.StatusCode.Should().Be(201);
            created.Data!.Status.Should().Be(LessonPlanStatus.Draft);
            planId = created.Data.Id;
        }

        var submittedVersion = await SetLessonVersionAsync(options, planId, [1, 2, 3, 4, 5, 6, 7, 8]);
        await using (var teacherContext = CreateContext(options, 101, 70, "Teacher"))
        {
            var teacher = CreateService(teacherContext, new TestCurrentUser(101, 70, "Teacher"));
            var submitted = await teacher.SubmitLessonPlanAsync(planId, new AcademicRowVersionDto { RowVersion = submittedVersion });
            submitted.Data!.Status.Should().Be(LessonPlanStatus.Submitted);
        }

        var reviewVersion = await SetLessonVersionAsync(options, planId, [2, 3, 4, 5, 6, 7, 8, 9]);
        await using (var managerContext = CreateContext(options, 101, 7, "TenantAdmin"))
        {
            var manager = CreateService(managerContext, new TestCurrentUser(101, 7, "TenantAdmin"));
            var approved = await manager.ReviewLessonPlanAsync(planId, new LessonPlanReviewDto { Approve = true, Remarks = "Ready", RowVersion = reviewVersion });
            approved.Data!.Status.Should().Be(LessonPlanStatus.Approved);
            approved.Data.ReviewedBy.Should().Be(7);
        }

        var progressVersion = await SetLessonVersionAsync(options, planId, [3, 4, 5, 6, 7, 8, 9, 10]);
        await using (var teacherContext = CreateContext(options, 101, 70, "Teacher"))
        {
            var teacher = CreateService(teacherContext, new TestCurrentUser(101, 70, "Teacher"));
            var progress = await teacher.RecordLessonProgressAsync(planId, new LessonPlanProgressDto { ProgressPercent = 60, Notes = "Practice underway", RowVersion = progressVersion });
            progress.Data!.Status.Should().Be(LessonPlanStatus.InProgress);
        }

        var completionVersion = await SetLessonVersionAsync(options, planId, [4, 5, 6, 7, 8, 9, 10, 11]);
        await using (var teacherContext = CreateContext(options, 101, 70, "Teacher"))
        {
            var teacher = CreateService(teacherContext, new TestCurrentUser(101, 70, "Teacher"));
            var completed = await teacher.RecordLessonProgressAsync(planId, new LessonPlanProgressDto { ProgressPercent = 100, Notes = "Objectives met", RowVersion = completionVersion });
            completed.Data!.Status.Should().Be(LessonPlanStatus.Completed);
            completed.Data.ProgressPercent.Should().Be(100);
            completed.Data.CompletedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Lesson_plan_rejects_stale_version_and_cross_tenant_assignment()
    {
        var options = CreateOptions();
        var seed = await SeedAsync(options);
        long planId;
        await using (var managerContext = CreateContext(options, 101, 7, "TenantAdmin"))
        {
            var manager = CreateService(managerContext, new TestCurrentUser(101, 7, "TenantAdmin"));
            planId = (await manager.CreateLessonPlanAsync(Plan(seed.AssignmentId))).Data!.Id;
        }
        await SetLessonVersionAsync(options, planId, [1, 1, 1, 1, 1, 1, 1, 1]);
        await using (var managerContext = CreateContext(options, 101, 7, "TenantAdmin"))
        {
            var manager = CreateService(managerContext, new TestCurrentUser(101, 7, "TenantAdmin"));
            var update = PlanUpdate([9, 9, 9, 9, 9, 9, 9, 9]);
            var stale = await manager.UpdateLessonPlanAsync(planId, update);
            stale.Success.Should().BeFalse();
            stale.StatusCode.Should().Be(409);
        }

        await using var otherContext = CreateContext(options, 202, 8, "TenantAdmin");
        var other = CreateService(otherContext, new TestCurrentUser(202, 8, "TenantAdmin"));
        var crossTenant = await other.CreateLessonPlanAsync(Plan(seed.AssignmentId));
        crossTenant.Success.Should().BeFalse();
        crossTenant.StatusCode.Should().Be(404);
        (await otherContext.LessonPlans.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Exact_lesson_plan_retry_survives_later_assignment_deactivation()
    {
        var options = CreateOptions();
        var seed = await SeedAsync(options);
        var request = Plan(seed.AssignmentId);
        long createdId;

        await using (var teacherContext = CreateContext(options, 101, 70, "Teacher"))
        {
            var teacher = CreateService(teacherContext, new TestCurrentUser(101, 70, "Teacher"));
            createdId = (await teacher.CreateLessonPlanAsync(request)).Data!.Id;
        }

        await using (var managerContext = CreateContext(options, 101, 7, "TenantAdmin"))
        {
            var assignment = await managerContext.InstructorAssignments.SingleAsync(x => x.Id == seed.AssignmentId);
            assignment.IsActive = false;
            await managerContext.SaveChangesAsync();
        }

        await using (var teacherContext = CreateContext(options, 101, 70, "Teacher"))
        {
            var teacher = CreateService(teacherContext, new TestCurrentUser(101, 70, "Teacher"));
            var replay = await teacher.CreateLessonPlanAsync(request);
            replay.Success.Should().BeTrue();
            replay.Data!.Id.Should().Be(createdId);
        }
    }

    private static CreateLessonPlanDto Plan(long assignmentId) => new()
    {
        ClientRequestId = Guid.NewGuid(), InstructorAssignmentId = assignmentId, ChapterName = "Geometry", StartDate = new DateTime(2026, 9, 20), EndDate = new DateTime(2026, 9, 24)
    };

    private static UpdateLessonPlanDto PlanUpdate(byte[] version) => new()
    {
        ChapterName = "Geometry revised", StartDate = new DateTime(2026, 9, 20), EndDate = new DateTime(2026, 9, 25), RowVersion = Convert.ToBase64String(version)
    };

    private static async Task<string> SetLessonVersionAsync(DbContextOptions<EduOSDbContext> options, long id, byte[] version)
    {
        await using var context = CreateContext(options, 101, 7, "TenantAdmin");
        var row = await context.LessonPlans.SingleAsync(x => x.Id == id);
        row.RowVersion = version;
        await context.SaveChangesAsync();
        return Convert.ToBase64String(version);
    }

    private static async Task<string> SetSubstitutionVersionAsync(DbContextOptions<EduOSDbContext> options, long id, byte[] version)
    {
        await using var context = CreateContext(options, 101, 7, "TenantAdmin");
        var row = await context.Substitutions.SingleAsync(x => x.Id == id);
        row.RowVersion = version;
        await context.SaveChangesAsync();
        return Convert.ToBase64String(version);
    }

    private static AcademicInstructionService CreateService(EduOSDbContext context, ICurrentUserService currentUser) => new(
        new GenericRepository<Substitution>(context),
        new GenericRepository<LessonPlan>(context),
        new GenericRepository<RoutineEntry>(context),
        new GenericRepository<InstructorAssignment>(context),
        new GenericRepository<Employee>(context),
        new GenericRepository<AcademicYear>(context),
        new GenericRepository<AcademicTerm>(context),
        context,
        currentUser,
        TimeProvider.System,
        NullLogger<AcademicInstructionService>.Instance);

    private static async Task<InstructionSeed> SeedAsync(DbContextOptions<EduOSDbContext> options)
    {
        await using var context = CreateContext(options, 101, 7, "TenantAdmin");
        var year = new AcademicYear { TenantId = 101, Name = "2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsCurrent = true, IsActive = true };
        var legacyClass = new Class { TenantId = 101, Name = "Class Nine", NumericValue = 9, IsActive = true };
        var program = new AcademicProgram { TenantId = 101, CampusId = 1, Name = "Secondary", Code = "SEC", IsActive = true };
        context.AddRange(year, legacyClass, program);
        await context.SaveChangesAsync();
        var term = new AcademicTerm { TenantId = 101, AcademicYearId = year.Id, Name = "Autumn", StartDate = new DateTime(2026, 7, 1), EndDate = new DateTime(2026, 12, 15), IsActive = true };
        var level = new AcademicLevel { TenantId = 101, AcademicProgramId = program.Id, Name = "Class Nine", Code = "C9", LevelNo = 9, IsActive = true };
        var subject = new Subject { TenantId = 101, ClassId = legacyClass.Id, Name = "Mathematics", Code = "MATH", IsActive = true };
        var original = Teacher(70, "T-001", "Original Teacher");
        var substitute = Teacher(71, "T-002", "Substitute Teacher");
        var unrelated = Teacher(72, "T-003", "Unrelated Teacher");
        context.AddRange(term, level, subject, original, substitute, unrelated);
        await context.SaveChangesAsync();
        var batch = new AcademicBatch { TenantId = 101, CampusId = 1, AcademicYearId = year.Id, AcademicTermId = term.Id, AcademicProgramId = program.Id, AcademicLevelId = level.Id, Name = "Batch A", Code = "B-A", Capacity = 30, IsActive = true };
        var assignment = new InstructorAssignment { TenantId = 101, AcademicBatch = batch, Subject = subject, Employee = original, AcademicYearId = year.Id, AcademicTermId = term.Id, IsPrimary = true, IsActive = true };
        var slot = new RoutineTimeSlot { TenantId = 101, Name = "Period 1", StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(10, 0, 0), IsActive = true };
        var overlapSlot = new RoutineTimeSlot { TenantId = 101, Name = "Period overlap", StartTime = new TimeSpan(9, 30, 0), EndTime = new TimeSpan(10, 30, 0), IsActive = true };
        context.AddRange(batch, assignment, slot, overlapSlot);
        await context.SaveChangesAsync();
        var routine = new RoutineEntry { TenantId = 101, AcademicBatch = batch, RoutineTimeSlot = slot, DayOfWeek = DayOfWeek.Sunday, Subject = subject, Employee = original, AcademicYearId = year.Id, AcademicTermId = term.Id, IsActive = true };
        var overlapping = new RoutineEntry { TenantId = 101, AcademicBatch = batch, RoutineTimeSlot = overlapSlot, DayOfWeek = DayOfWeek.Sunday, Subject = subject, Employee = unrelated, AcademicYearId = year.Id, AcademicTermId = term.Id, IsActive = true };
        context.AddRange(routine, overlapping);
        await context.SaveChangesAsync();
        return new InstructionSeed(batch.Id, assignment.Id, routine.Id, overlapping.Id, substitute.Id);
    }

    private static Employee Teacher(long userId, string code, string name) => new()
    {
        TenantId = 101, UserId = userId, EmployeeCode = code, FullName = name, Phone = $"01700000{userId}", DesignationId = 1, JoiningDate = new DateTime(2020, 1, 1), IsTeacher = true, IsActive = true
    };

    private static DbContextOptions<EduOSDbContext> CreateOptions() => new DbContextOptionsBuilder<EduOSDbContext>().UseInMemoryDatabase($"academic-instruction-{Guid.NewGuid():N}").Options;

    private static EduOSDbContext CreateContext(DbContextOptions<EduOSDbContext> options, long tenantId, long userId, string role)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role)], "TestAuthentication")) };
        http.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed record InstructionSeed(long BatchId, long AssignmentId, long RoutineEntryId, long OverlappingRoutineEntryId, long SubstituteTeacherId);

    private sealed class TestCurrentUser(long tenantId, long userId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => "Instruction User";
        public string? Email => "instruction@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "EduOS tests";
    }
}
