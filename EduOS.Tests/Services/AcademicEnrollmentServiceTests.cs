using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
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

public sealed class AcademicEnrollmentServiceTests
{
    [Fact]
    public async Task Enrollment_is_idempotent_and_capacity_is_enforced()
    {
        var options = Options();
        var seed = await SeedAsync(options, 1);
        await using var db = Context(options, 101, 7, "TenantAdmin");
        var service = Service(db, new TestUser(101, 7, "TenantAdmin"));
        var request = new CreateStudentEnrollmentRequestDto
        {
            ClientRequestId = Guid.NewGuid(), StudentReference = seed.StudentRef,
            AcademicBatchId = seed.BatchId, AcademicCurriculumId = seed.CurriculumId,
            RollNo = " 01 ", EnrollmentDate = new DateOnly(2026, 9, 21)
        };
        var created = await service.EnrollAsync(request);
        var replay = await service.EnrollAsync(request);
        var overflow = await service.EnrollAsync(new CreateStudentEnrollmentRequestDto
        {
            ClientRequestId = Guid.NewGuid(), StudentReference = seed.OtherStudentRef,
            AcademicBatchId = seed.BatchId, AcademicCurriculumId = seed.CurriculumId,
            RollNo = "02", EnrollmentDate = request.EnrollmentDate
        });
        created.Success.Should().BeTrue();
        created.Data!.RollNo.Should().Be("01");
        replay.Success.Should().BeTrue();
        replay.Data!.Reference.Should().Be(created.Data.Reference);
        overflow.Success.Should().BeFalse();
        overflow.StatusCode.Should().Be(409);
        (await db.StudentEnrollments.CountAsync()).Should().Be(1);
        var registrations = await db.StudentSubjectRegistrations.ToListAsync();
        registrations.Should().ContainSingle(x => x.State == SubjectRegistrationState.Approved);
        registrations[0].CreditHoursSnapshot.Should().Be(4m);
    }

    [Fact]
    public async Task Guardian_cannot_access_unlinked_student_and_approved_elective_controls_timetable()
    {
        var options = Options();
        var seed = await SeedAsync(options, 10);
        Guid enrollmentReference;
        await using (var db = Context(options, 101, 7, "TenantAdmin"))
        {
            var service = Service(db, new TestUser(101, 7, "TenantAdmin"));
            var enrolled = await service.EnrollAsync(new CreateStudentEnrollmentRequestDto
            {
                ClientRequestId = Guid.NewGuid(), StudentReference = seed.StudentRef,
                AcademicBatchId = seed.BatchId, AcademicCurriculumId = seed.CurriculumId,
                RollNo = "01", EnrollmentDate = new DateOnly(2026, 9, 21)
            });
            enrolled.Success.Should().BeTrue();
            enrollmentReference = enrolled.Data!.Reference;
        }
        await using (var db = Context(options, 101, 73, "Guardian"))
        {
            var foreignGuardian = Service(db, new TestUser(101, 73, "Guardian"));
            var denied = await foreignGuardian.RequestOptionalSubjectAsync(new RegisterStudentSubjectRequestDto
            {
                ClientRequestId = Guid.NewGuid(), StudentEnrollmentReference = enrollmentReference,
                SubjectOfferingReference = seed.OptionalOfferingRef
            });
            denied.StatusCode.Should().Be(404);
            (await foreignGuardian.GetCurrentAsync(seed.StudentRef)).StatusCode.Should().Be(404);
        }
        long registrationId;
        await using (var db = Context(options, 101, 72, "Guardian"))
        {
            var guardian = Service(db, new TestUser(101, 72, "Guardian"));
            var request = new RegisterStudentSubjectRequestDto
            {
                ClientRequestId = Guid.NewGuid(), StudentEnrollmentReference = enrollmentReference,
                SubjectOfferingReference = seed.OptionalOfferingRef, Remarks = "Elective choice"
            };
            var first = await guardian.RequestOptionalSubjectAsync(request);
            var replay = await guardian.RequestOptionalSubjectAsync(request);
            first.Success.Should().BeTrue();
            first.Data!.State.Should().Be(SubjectRegistrationState.Pending);
            replay.Data!.Id.Should().Be(first.Data.Id);
            var timetable = await guardian.GetTimetableAsync(seed.StudentRef);
            timetable.Data!.Should().ContainSingle();
            registrationId = first.Data.Id;
        }
        await using (var db = Context(options, 101, 7, "TenantAdmin"))
        {
            var record = await db.StudentSubjectRegistrations.SingleAsync(x => x.Id == registrationId);
            record.RowVersion = [1, 2, 3, 4, 5, 6, 7, 8];
            await db.SaveChangesAsync();
            var manager = Service(db, new TestUser(101, 7, "TenantAdmin"));
            var stale = await manager.DecideSubjectAsync(registrationId, new ChangeSubjectRegistrationStateRequestDto
            {
                State = SubjectRegistrationState.Approved,
                RowVersion = Convert.ToBase64String([8, 7, 6, 5, 4, 3, 2, 1])
            });
            stale.StatusCode.Should().Be(409);
            var approved = await manager.DecideSubjectAsync(registrationId, new ChangeSubjectRegistrationStateRequestDto
            {
                State = SubjectRegistrationState.Approved,
                RowVersion = Convert.ToBase64String(record.RowVersion), Remarks = "Approved"
            });
            approved.Success.Should().BeTrue();
            approved.Data!.State.Should().Be(SubjectRegistrationState.Approved);
            (await db.StudentSubjectRegistrations.SingleAsync(x => x.Id == registrationId))
                .ApprovedByUserId.Should().Be(7);
        }
        await using (var db = Context(options, 101, 72, "Guardian"))
        {
            var guardian = Service(db, new TestUser(101, 72, "Guardian"));
            var timetable = await guardian.GetTimetableAsync(seed.StudentRef);
            timetable.Data!.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task Tenant_isolation_rejects_foreign_student_and_batch()
    {
        var options = Options();
        var seed = await SeedAsync(options, 10);
        await using var db = Context(options, 202, 8, "TenantAdmin");
        var service = Service(db, new TestUser(202, 8, "TenantAdmin"));
        var result = await service.EnrollAsync(new CreateStudentEnrollmentRequestDto
        {
            ClientRequestId = Guid.NewGuid(), StudentReference = seed.StudentRef,
            AcademicBatchId = seed.BatchId, AcademicCurriculumId = seed.CurriculumId,
            RollNo = "01", EnrollmentDate = new DateOnly(2026, 9, 21)
        });
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        (await db.StudentEnrollments.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    private static AcademicEnrollmentService Service(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<StudentEnrollment>(db), new GenericRepository<StudentSubjectRegistration>(db),
        new GenericRepository<Student>(db), new GenericRepository<Guardian>(db),
        new GenericRepository<StudentGuardian>(db), new GenericRepository<AcademicBatch>(db),
        new GenericRepository<AcademicYear>(db), new GenericRepository<AcademicCurriculum>(db),
        new GenericRepository<CurriculumSubject>(db), new GenericRepository<SubjectOffering>(db),
        new GenericRepository<Subject>(db), new GenericRepository<RoutineEntry>(db),
        new GenericRepository<RoutineTimeSlot>(db), new GenericRepository<Room>(db),
        db, user, TimeProvider.System, NullLogger<AcademicEnrollmentService>.Instance);

    private static async Task<Seed> SeedAsync(DbContextOptions<EduOSDbContext> options, int capacity)
    {
        await using var db = Context(options, 101, 7, "TenantAdmin");
        var campus = new Campus { TenantId = 101, Name = "Main Campus", Code = "MAIN" };
        var year = new AcademicYear
        {
            TenantId = 101, Name = "2026", Code = "2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), IsCurrent = true
        };
        var program = new AcademicProgram { TenantId = 101, Name = "Secondary", Code = "SEC" };
        var first = new Student
        {
            TenantId = 101, PersonId = 301, UserId = 71, StudentCode = "S-001",
            FullName = "First Student", AdmissionDate = new DateOnly(2026, 1, 1)
        };
        var second = new Student
        {
            TenantId = 101, PersonId = 302, UserId = 82, StudentCode = "S-002",
            FullName = "Second Student", AdmissionDate = new DateOnly(2026, 1, 1)
        };
        var guardian = new Guardian { TenantId = 101, PersonId = 303, UserId = 72, FullName = "Guardian" };
        db.AddRange(campus, year, program, first, second, guardian);
        await db.SaveChangesAsync();
        var level = new AcademicLevel
        {
            TenantId = 101, AcademicProgramId = program.Id,
            Code = "G9", Name = "Grade Nine", LevelNo = 9
        };
        var required = new Subject { TenantId = 101, Code = "MATH", Name = "Mathematics" };
        var optional = new Subject { TenantId = 101, Code = "MUSIC", Name = "Music" };
        var curriculum = new AcademicCurriculum
        {
            TenantId = 101, AcademicProgramId = program.Id, Name = "Secondary 2026",
            Code = "SEC-2026", EffectiveFrom = new DateOnly(2026, 1, 1), IsCurrent = true
        };
        var batch = new AcademicBatch
        {
            TenantId = 101, CampusId = campus.Id, AcademicYearId = year.Id,
            AcademicProgramId = program.Id, AcademicLevelId = 0,
            Name = "Nine A", Code = "G9-A", Capacity = capacity,
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31)
        };
        var link = new StudentGuardian
        {
            TenantId = 101, StudentId = first.Id, GuardianId = guardian.Id,
            RelationCode = "Mother", IsPrimary = true
        };
        db.AddRange(level, required, optional, curriculum, link);
        await db.SaveChangesAsync();
        batch.AcademicLevelId = level.Id;
        db.AcademicBatches.Add(batch);
        await db.SaveChangesAsync();
        var core = new CurriculumSubject
        {
            TenantId = 101, AcademicCurriculumId = curriculum.Id, AcademicLevelId = level.Id,
            SubjectId = required.Id, CreditHours = 4m, IsOptional = false
        };
        var elective = new CurriculumSubject
        {
            TenantId = 101, AcademicCurriculumId = curriculum.Id, AcademicLevelId = level.Id,
            SubjectId = optional.Id, CreditHours = 2m, IsOptional = true
        };
        db.AddRange(core, elective);
        await db.SaveChangesAsync();
        var coreOffering = new SubjectOffering
        {
            TenantId = 101, AcademicBatchId = batch.Id, CurriculumSubjectId = core.Id,
            AcademicYearId = year.Id, Code = "G9-MATH"
        };
        var electiveOffering = new SubjectOffering
        {
            TenantId = 101, AcademicBatchId = batch.Id, CurriculumSubjectId = elective.Id,
            AcademicYearId = year.Id, Code = "G9-MUSIC"
        };
        var slot = new RoutineTimeSlot
        {
            TenantId = 101, Name = "Period One", StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(9, 45)
        };
        db.AddRange(coreOffering, electiveOffering, slot);
        await db.SaveChangesAsync();
        db.RoutineEntries.AddRange(
            new RoutineEntry
            {
                TenantId = 101, SubjectOfferingId = coreOffering.Id,
                RoutineTimeSlotId = slot.Id, DayOfWeek = DayOfWeek.Sunday,
                EffectiveFrom = new DateOnly(2026, 1, 1)
            },
            new RoutineEntry
            {
                TenantId = 101, SubjectOfferingId = electiveOffering.Id,
                RoutineTimeSlotId = slot.Id, DayOfWeek = DayOfWeek.Monday,
                EffectiveFrom = new DateOnly(2026, 1, 1)
            });
        await db.SaveChangesAsync();
        return new Seed(first.PublicId, second.PublicId, batch.Id, curriculum.Id, electiveOffering.PublicId);
    }

    private static DbContextOptions<EduOSDbContext> Options() => new DbContextOptionsBuilder<EduOSDbContext>()
        .UseInMemoryDatabase($"academic-enrollment-{Guid.NewGuid():N}").Options;

    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenantId, long userId, string role)
    {
        var context = new DefaultHttpContext();
        context.Items["TenantId"] = tenantId;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim("TenantId", tenantId.ToString())
        ], "TestAuthentication"));
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = context });
    }

    private sealed record Seed(Guid StudentRef, Guid OtherStudentRef, long BatchId, long CurriculumId, Guid OptionalOfferingRef);

    private sealed class TestUser(long tenantId, long userId, string role) : ICurrentUserService
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
