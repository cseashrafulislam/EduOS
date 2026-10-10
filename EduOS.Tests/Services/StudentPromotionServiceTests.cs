using EduOS.Core.DTOs.Student;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Students;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentPromotionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Promotion_creates_one_active_target_enrollment_and_retries_safely()
    {
        var options = Options();
        var fixture = await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101));
        var request = Request(fixture);
        var created = await service.PromoteAsync(fixture.StudentReference, request);
        var replay = await service.PromoteAsync(fixture.StudentReference, request);
        created.Success.Should().BeTrue(created.Message);
        created.Data!.AcademicYearId.Should().Be(fixture.TargetYearId);
        created.Data.AcademicBatchId.Should().Be(fixture.TargetBatchId);
        replay.Success.Should().BeTrue();
        replay.Data!.AlreadyProcessed.Should().BeTrue();
        (await db.StudentPromotionRecords.CountAsync()).Should().Be(1);
        var enrollments = await db.StudentEnrollments.ToListAsync();
        enrollments.Should().HaveCount(2);
        enrollments.Count(x => x.State == EnrollmentState.Active && x.IsCurrent).Should().Be(1);
        enrollments.Single(x => x.Id == fixture.SourceEnrollmentId)
            .State.Should().Be(EnrollmentState.Promoted);
        (await service.GetHistoryAsync(fixture.StudentReference)).Data.Should().ContainSingle();
    }

    [Fact]
    public async Task Repeat_decision_keeps_academic_level_without_advancing()
    {
        var options = Options();
        var fixture = await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101));
        var request = Request(fixture);
        request.TargetAcademicLevelId = fixture.SourceLevelId;
        request.TargetAcademicBatchId = fixture.RepeatBatchId;
        request.Decision = StudentProgressionDecisionType.Repeated;
        var repeated = await service.PromoteAsync(fixture.StudentReference, request);
        repeated.Success.Should().BeTrue(repeated.Message);
        repeated.Data!.Decision.Should().Be(StudentProgressionDecisionType.Repeated);
        repeated.Data.AcademicLevelId.Should().Be(fixture.SourceLevelId);
        (await db.StudentEnrollments.SingleAsync(x => x.Id == fixture.SourceEnrollmentId))
            .State.Should().Be(EnrollmentState.Completed);
    }

    [Fact]
    public async Task Wrong_rowversion_fails_before_creating_enrollment()
    {
        var options = Options();
        var fixture = await SeedAsync(options);
        await using var db = Context(options, 101);
        var service = Service(db, new User(101));
        var request = Request(fixture);
        request.StudentRowVersion = Convert.ToBase64String([9]);
        (await service.PromoteAsync(fixture.StudentReference, request)).StatusCode.Should().Be(409);
        (await db.StudentPromotionRecords.CountAsync()).Should().Be(0);
        (await db.StudentEnrollments.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Unrelated_tenant_and_teacher_cannot_promote_student()
    {
        var options = Options();
        var fixture = await SeedAsync(options);
        await using var foreign = Context(options, 202);
        (await Service(foreign, new User(202)).PromoteAsync(fixture.StudentReference,
            Request(fixture))).StatusCode.Should().Be(404);
        await using var teacher = Context(options, 101);
        (await Service(teacher, new User(101, "Teacher")).PromoteAsync(fixture.StudentReference,
            Request(fixture))).StatusCode.Should().Be(403);
        (await foreign.StudentPromotionRecords.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    private static StudentPromotionService Service(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<Student>(db),
        new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<StudentPromotionRecord>(db),
        new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicLevel>(db),
        new GenericRepository<AcademicBatch>(db),
        new GenericRepository<AcademicCurriculum>(db), db, user,
        new Clock(Now), NullLogger<StudentPromotionService>.Instance);

    private static PromoteStudentWorkflowRequestDto Request(Fixture f) => new()
    {
        ClientRequestId = Guid.NewGuid(), SourceEnrollmentId = f.SourceEnrollmentId,
        TargetAcademicYearId = f.TargetYearId, TargetAcademicLevelId = f.TargetLevelId,
        TargetAcademicBatchId = f.TargetBatchId, TargetRoll = "5",
        Decision = StudentProgressionDecisionType.Promoted,
        StudentRowVersion = f.StudentVersion, SourceEnrollmentRowVersion = f.EnrollmentVersion,
        Note = "Annual promotion"
    };

    private static async Task<Fixture> SeedAsync(DbContextOptions<EduOSDbContext> options)
    {
        await using var db = Context(options, 101);
        var campus = new Campus { TenantId = 101, Name = "Main", Code = "MAIN" };
        var program = new AcademicProgram { TenantId = 101, Name = "School", Code = "SCHOOL" };
        var prior = new AcademicYear { TenantId = 101, Code = "2025", Name = "2025",
            StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 12, 31) };
        var next = new AcademicYear { TenantId = 101, Code = "2026", Name = "2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
        db.AddRange(campus, program, prior, next);
        await db.SaveChangesAsync();
        var sourceLevel = new AcademicLevel { TenantId = 101, AcademicProgramId = program.Id,
            Code = "C1", Name = "Class One", LevelNo = 1, IsPromotable = true };
        var targetLevel = new AcademicLevel { TenantId = 101, AcademicProgramId = program.Id,
            Code = "C2", Name = "Class Two", LevelNo = 2 };
        db.AddRange(sourceLevel, targetLevel); await db.SaveChangesAsync();
        var currentBatch = new AcademicBatch { TenantId = 101, CampusId = campus.Id,
            AcademicYearId = prior.Id, AcademicProgramId = program.Id,
            AcademicLevelId = sourceLevel.Id, Name = "2025-A", Code = "2025A", Capacity = 30 };
        var targetBatch = new AcademicBatch { TenantId = 101, CampusId = campus.Id,
            AcademicYearId = next.Id, AcademicProgramId = program.Id,
            AcademicLevelId = targetLevel.Id, Name = "2026-B", Code = "2026B", Capacity = 30 };
        var repeatBatch = new AcademicBatch { TenantId = 101, CampusId = campus.Id,
            AcademicYearId = next.Id, AcademicProgramId = program.Id,
            AcademicLevelId = sourceLevel.Id, Name = "2026-A", Code = "2026A", Capacity = 30 };
        var curriculum = new AcademicCurriculum { TenantId = 101, AcademicProgramId = program.Id,
            Name = "School Curriculum", Code = "S-CUR",
            EffectiveFrom = new DateOnly(2025, 1, 1), IsActive = true, IsCurrent = true };
        db.AddRange(currentBatch, targetBatch, repeatBatch, curriculum);
        await db.SaveChangesAsync();
        var student = new Student { TenantId = 101, PublicId = Guid.NewGuid(),
            StudentCode = "STU-0001", FullName = "Promotion Student",
            DateOfBirth = new DateOnly(2015, 1, 1), Gender = "Male",
            AdmissionDate = new DateOnly(2025, 1, 1), StatusCode = "Active",
            RowVersion = [1, 2, 3, 4, 5, 6, 7, 8] };
        db.Add(student); await db.SaveChangesAsync();
        var enrollment = new StudentEnrollment
        {
            TenantId = 101, StudentId = student.Id, CampusId = campus.Id,
            AcademicYearId = prior.Id, AcademicProgramId = program.Id,
            AcademicLevelId = sourceLevel.Id, AcademicBatchId = currentBatch.Id,
            AcademicCurriculumId = curriculum.Id, RollNo = "4",
            EnrollmentDate = new DateOnly(2025, 1, 1),
            IsCurrent = true, State = EnrollmentState.Active,
            RowVersion = [1, 2, 3, 4, 5, 6, 7, 8]
        };
        db.Add(enrollment); await db.SaveChangesAsync();
        return new Fixture(student.PublicId, enrollment.Id, sourceLevel.Id, targetLevel.Id,
            next.Id, targetBatch.Id, repeatBatch.Id,
            Convert.ToBase64String(student.RowVersion), Convert.ToBase64String(enrollment.RowVersion));
    }

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>().UseInMemoryDatabase(
            "student-promotion-" + Guid.NewGuid().ToString("N")).Options;
    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
    private sealed record Fixture(Guid StudentReference, long SourceEnrollmentId,
        long SourceLevelId, long TargetLevelId, long TargetYearId, long TargetBatchId,
        long RepeatBatchId, string StudentVersion, string EnrollmentVersion);
    private sealed class Clock(DateTimeOffset date) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => date;
    }
    private sealed class User(long tenant, string role = "TenantAdmin") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenant;
        public long UserId => 7;
        public string? FullName => "Administrator";
        public string? Email => "admin@example.test";
        public bool IsTenantAdmin => role == "TenantAdmin";
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
