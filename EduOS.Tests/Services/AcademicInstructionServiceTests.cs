using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
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
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AcademicInstructionServiceTests
{
    private static readonly DateOnly LessonDate = new(2026, 9, 20);

    [Fact]
    public async Task Lesson_plan_creation_is_tenant_scoped_and_exact_retry_is_idempotent()
    {
        var options = Options();
        var seed = await SeedAsync(options);
        await using var db = Context(options, 101, 7, "TenantAdmin");
        var service = Service(db, 101, 7, "TenantAdmin");
        var request = Lesson(seed.OfferingReference);
        var created = await service.CreateLessonPlanAsync(request);
        var replay = await service.CreateLessonPlanAsync(request);
        var changed = Lesson(seed.OfferingReference);
        changed.Title = request.Title; changed.Content = "Different lesson material";
        var conflict = await service.CreateLessonPlanAsync(changed);
        created.StatusCode.Should().Be(201);
        replay.Success.Should().BeTrue();
        replay.Data!.Id.Should().Be(created.Data!.Id);
        conflict.StatusCode.Should().Be(409);
        (await db.LessonPlans.CountAsync()).Should().Be(1);

        await using var otherDb = Context(options, 202, 8, "TenantAdmin");
        var other = Service(otherDb, 202, 8, "TenantAdmin");
        (await other.CreateLessonPlanAsync(Lesson(seed.OfferingReference))).StatusCode.Should().Be(404);
        var page = await other.GetLessonPlansAsync(null, null, null, 1, 25);
        page.Success.Should().BeTrue();
        page.Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Assigned_teacher_submits_and_manager_reviews_canonical_lesson_plan()
    {
        var options = Options();
        var seed = await SeedAsync(options);
        long id;
        await using (var db = Context(options, 101, 70, "Teacher"))
        {
            var teacher = Service(db, 101, 70, "Teacher");
            var created = await teacher.CreateLessonPlanAsync(Lesson(seed.OfferingReference));
            created.StatusCode.Should().Be(201);
            created.Data!.State.Should().Be(LessonPlanState.Draft);
            id = created.Data.Id;
        }
        var version = await VersionAsync(options, id, [1, 2, 3, 4, 5, 6, 7, 8]);
        await using (var db = Context(options, 101, 70, "Teacher"))
        {
            var teacher = Service(db, 101, 70, "Teacher");
            var submitted = await teacher.SubmitLessonPlanAsync(id, version);
            submitted.Success.Should().BeTrue();
            submitted.Data!.State.Should().Be(LessonPlanState.Submitted);
        }
        var reviewVersion = await VersionAsync(options, id, [2, 3, 4, 5, 6, 7, 8, 9]);
        await using (var db = Context(options, 101, 7, "TenantAdmin"))
        {
            var manager = Service(db, 101, 7, "TenantAdmin");
            var reviewed = await manager.ReviewLessonPlanAsync(id, new ReviewLessonPlanRequestDto
            {
                Approve = true, Note = "Ready for instruction", RowVersion = reviewVersion
            });
            reviewed.Success.Should().BeTrue();
            reviewed.Data!.State.Should().Be(LessonPlanState.Approved);
            reviewed.Data.ReviewedByUserId.Should().Be(7);
            var stale = await manager.UpdateLessonPlanAsync(id, new SaveLessonPlanRequestDto
            {
                ClientRequestId = Guid.NewGuid(), SubjectOfferingReference = seed.OfferingReference,
                LessonDate = LessonDate, Title = "Changed after approval",
                RowVersion = reviewVersion
            });
            stale.StatusCode.Should().Be(409);
        }
    }

    [Fact]
    public async Task Substitution_is_retry_safe_tenant_scoped_and_cancellable_with_rowversion()
    {
        var options = Options();
        var seed = await SeedAsync(options);
        long id;
        await using (var db = Context(options, 101, 7, "TenantAdmin"))
        {
            var manager = Service(db, 101, 7, "TenantAdmin");
            var request = new CreateSubstitutionRequestDto
            {
                ClientRequestId = Guid.NewGuid(), RoutineEntryId = seed.RoutineEntryId,
                Date = LessonDate, SubstituteEmployeeReference = seed.SubstituteReference,
                Reason = "Approved teacher absence"
            };
            var created = await manager.CreateSubstitutionAsync(request);
            var replay = await manager.CreateSubstitutionAsync(request);
            created.StatusCode.Should().Be(201);
            replay.Data!.Id.Should().Be(created.Data!.Id);
            id = created.Data.Id;
            (await db.Substitutions.CountAsync()).Should().Be(1);
        }
        await using (var db = Context(options, 101, 71, "Teacher"))
        {
            var teacher = Service(db, 101, 71, "Teacher");
            var rows = await teacher.GetSubstitutionsAsync(LessonDate, LessonDate, seed.BatchId);
            rows.Success.Should().BeTrue();
            rows.Data.Should().ContainSingle();
            rows.Data![0].Id.Should().Be(id);
        }
        var version = await VersionAsync(options, id, [7, 6, 5, 4, 3, 2, 1, 8], true);
        await using (var db = Context(options, 101, 7, "TenantAdmin"))
        {
            var manager = Service(db, 101, 7, "TenantAdmin");
            var cancelled = await manager.CancelSubstitutionAsync(id,
                new CancelSubstitutionRequestDto { RowVersion = version });
            cancelled.Success.Should().BeTrue();
            cancelled.Data!.IsCancelled.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Unauthorized_user_cannot_create_lesson_plan_or_substitution()
    {
        var options = Options();
        var seed = await SeedAsync(options);
        await using var db = Context(options, 101, 72, "Student");
        var service = Service(db, 101, 72, "Student");
        (await service.CreateLessonPlanAsync(Lesson(seed.OfferingReference))).StatusCode.Should().Be(403);
        (await service.CreateSubstitutionAsync(new CreateSubstitutionRequestDto
        {
            ClientRequestId = Guid.NewGuid(), RoutineEntryId = seed.RoutineEntryId,
            Date = LessonDate, SubstituteEmployeeReference = seed.SubstituteReference
        })).StatusCode.Should().Be(403);
    }

    private static SaveLessonPlanRequestDto Lesson(Guid offeringReference) => new()
    {
        ClientRequestId = Guid.NewGuid(), SubjectOfferingReference = offeringReference,
        LessonDate = LessonDate, Title = "Linear equations", Objectives = "Explain and solve equations",
        Content = "Examples with one variable", Resources = "Mathematics textbook"
    };

    private static AcademicInstructionService Service(EduOSDbContext db, long tenant, long user, string role) => new(
        new GenericRepository<Substitution>(db),
        new GenericRepository<LessonPlan>(db),
        new GenericRepository<RoutineEntry>(db),
        new GenericRepository<RoutineTimeSlot>(db),
        new GenericRepository<InstructorAssignment>(db),
        new GenericRepository<SubjectOffering>(db),
        new GenericRepository<CurriculumSubject>(db),
        new GenericRepository<Subject>(db),
        new GenericRepository<AcademicBatch>(db),
        new GenericRepository<Employee>(db),
        new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicTerm>(db),
        db, new TestUser(tenant, user, role), TimeProvider.System,
        NullLogger<AcademicInstructionService>.Instance);

    private static async Task<Seed> SeedAsync(DbContextOptions<EduOSDbContext> options)
    {
        await using var db = Context(options, 101, 7, "TenantAdmin");
        var campus = new EduOS.Core.Entities.SaaS.Campus { TenantId = 101, Name = "Main", Code = "MAIN" };
        var year = new AcademicYear
        {
            TenantId = 101, Name = "2026", Code = "AY-2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            IsCurrent = true
        };
        var program = new AcademicProgram { TenantId = 101, Name = "Secondary", Code = "SEC" };
        var subject = new Subject { TenantId = 101, Name = "Mathematics", Code = "MATH" };
        var teacher = Teacher(70, "T-70", "Original teacher");
        var substitute = Teacher(71, "T-71", "Substitute teacher");
        db.AddRange(campus, year, program, subject, teacher, substitute);
        await db.SaveChangesAsync();
        var level = new AcademicLevel
        {
            TenantId = 101, AcademicProgramId = program.Id, Name = "Nine",
            Code = "NINE", LevelNo = 9
        };
        var curriculum = new AcademicCurriculum
        {
            TenantId = 101, AcademicProgramId = program.Id,
            Name = "Secondary Curriculum", Code = "SEC2026",
            EffectiveFrom = new DateOnly(2026, 1, 1), IsCurrent = true
        };
        var term = new AcademicTerm
        {
            TenantId = 101, AcademicYearId = year.Id, Name = "Autumn",
            StartDate = new DateOnly(2026, 7, 1), EndDate = new DateOnly(2026, 12, 15)
        };
        db.AddRange(level, curriculum, term); await db.SaveChangesAsync();
        var batch = new AcademicBatch
        {
            TenantId = 101, CampusId = campus.Id, AcademicProgramId = program.Id,
            AcademicYearId = year.Id, AcademicTermId = term.Id,
            AcademicLevelId = level.Id, Name = "Batch A", Code = "BA", Capacity = 30
        };
        var curriculumSubject = new CurriculumSubject
        {
            TenantId = 101, AcademicCurriculumId = curriculum.Id,
            AcademicLevelId = level.Id, SubjectId = subject.Id
        };
        db.AddRange(batch, curriculumSubject); await db.SaveChangesAsync();
        var offering = new SubjectOffering
        {
            TenantId = 101, AcademicBatchId = batch.Id,
            CurriculumSubjectId = curriculumSubject.Id,
            AcademicYearId = year.Id, AcademicTermId = term.Id, Code = "MATH-NINE"
        };
        db.Add(offering); await db.SaveChangesAsync();
        var assignment = new InstructorAssignment
        {
            TenantId = 101, SubjectOfferingId = offering.Id,
            EmployeeId = teacher.Id, EffectiveFrom = new DateOnly(2026, 9, 1),
            IsPrimary = true, IsActive = true
        };
        var slot = new RoutineTimeSlot
        {
            TenantId = 101, Name = "Period One",
            StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0)
        };
        db.AddRange(assignment, slot); await db.SaveChangesAsync();
        var routine = new RoutineEntry
        {
            TenantId = 101, SubjectOfferingId = offering.Id,
            InstructorAssignmentId = assignment.Id, RoutineTimeSlotId = slot.Id,
            DayOfWeek = DayOfWeek.Sunday, EffectiveFrom = new DateOnly(2026, 9, 1)
        };
        db.Add(routine); await db.SaveChangesAsync();
        return new Seed(offering.PublicId, batch.Id, routine.Id, substitute.PublicId);
    }

    private static Employee Teacher(long userId, string code, string name) => new()
    {
        TenantId = 101, UserId = userId, EmployeeCode = code,
        FullName = name, PersonId = 1, DesignationId = 1,
        JoiningDate = new DateOnly(2020, 1, 1),
        CanTeach = true, State = EmployeeState.Active
    };

    private static async Task<string> VersionAsync(
        DbContextOptions<EduOSDbContext> options, long id, byte[] bytes, bool substitution = false)
    {
        await using var db = Context(options, 101, 7, "TenantAdmin");
        if (substitution)
            (await db.Substitutions.SingleAsync(x => x.Id == id)).RowVersion = bytes;
        else
            (await db.LessonPlans.SingleAsync(x => x.Id == id)).RowVersion = bytes;
        await db.SaveChangesAsync();
        return Convert.ToBase64String(bytes);
    }

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("academic-instruction-" + Guid.NewGuid().ToString("N")).Options;

    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant, long user, string role)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim("TenantId", tenant.ToString())
            }, "TestAuthentication"))
        };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed record Seed(Guid OfferingReference, long BatchId, long RoutineEntryId, Guid SubstituteReference);

    private sealed class TestUser(long tenant, long user, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenant;
        public long UserId => user;
        public string? FullName => "Instruction user";
        public string? Email => "instruction@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => new[] { role };
        public bool IsInRole(string requestedRole) => requestedRole == role;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
