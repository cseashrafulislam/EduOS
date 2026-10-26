using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Academic;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AcademicCurriculumRegisterTests
{
    private static readonly byte[] Version = [1, 2, 3, 4, 5, 6, 7, 8];

    [Fact]
    public async Task Register_is_tenant_scoped_paged_and_filters_effective_dates()
    {
        var options = Options();
        await using (var foreign = Context(options, 502))
        {
            foreign.Add(new AcademicCurriculum { TenantId = 502, AcademicProgramId = 1,
                Name = "Foreign", Code = "FOREIGN", VersionNo = 1,
                EffectiveFrom = new DateOnly(2025, 1, 1), RowVersion = Version.ToArray() });
            await foreign.SaveChangesAsync();
        }
        await using var db = Context(options, 501);
        db.AddRange(
            new AcademicCurriculum { TenantId = 501, AcademicProgramId = 1,
                Name = "Legacy", Code = "OLD", VersionNo = 1, IsActive = false,
                EffectiveFrom = new DateOnly(2023, 1, 1), EffectiveTo = new DateOnly(2024, 12, 31),
                RowVersion = Version.ToArray() },
            new AcademicCurriculum { TenantId = 501, AcademicProgramId = 1,
                Name = "Current", Code = "NEW", VersionNo = 2,
                EffectiveFrom = new DateOnly(2025, 1, 1), RowVersion = Version.ToArray() });
        await db.SaveChangesAsync();
        var service = Service(db);
        var result = await service.GetCurriculaAsync(null, 1, null, 1, 1);
        result.Success.Should().BeTrue();
        result.Data!.TotalCount.Should().Be(2);
        result.Data.Items.Should().ContainSingle(x => x.Code == "NEW");
        var old = await service.GetCurriculaAsync("Leg", 1, new DateOnly(2024, 6, 1));
        old.Data!.Items.Should().ContainSingle(x => x.Code == "OLD" && !x.IsActive);
        (await service.GetCurriculaAsync(null, null, null, 0)).StatusCode.Should().Be(400);
        (await service.GetCurriculaAsync(null, null, null, 1, 101)).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Detail_returns_only_own_tenant_subject_mappings_and_names()
    {
        var options = Options();
        await using var db = Context(options, 501);
        var row = new AcademicCurriculum { TenantId = 501, AcademicProgramId = 1,
            Name = "Science", Code = "SCI", VersionNo = 1,
            EffectiveFrom = new DateOnly(2025, 1, 1), RowVersion = Version.ToArray() };
        var level = new AcademicLevel { TenantId = 501, AcademicProgramId = 1,
            Name = "Level One", Code = "L1" };
        var subject = new Subject { TenantId = 501, Name = "Biology", Code = "BIO" };
        db.AddRange(row, level, subject);
        await db.SaveChangesAsync();
        db.Add(new CurriculumSubject { TenantId = 501, AcademicCurriculumId = row.Id,
            AcademicLevelId = level.Id, SubjectId = subject.Id, FullMarks = 100, PassMarks = 40 });
        await db.SaveChangesAsync();
        // The tenant boundary must be respected even when preparing test fixtures.
        await using (var foreign = Context(options, 502))
        {
            foreign.Add(new CurriculumSubject { TenantId = 502, AcademicCurriculumId = row.Id,
                AcademicLevelId = level.Id, SubjectId = subject.Id });
            await foreign.SaveChangesAsync();
        }
        // HttpContextAccessor flows through AsyncLocal. Restore the owning tenant
        // after foreign-tenant seeding, then exercise the actual tenant-filtered read.
        await using var tenantRead = Context(options, 501);
        var detail = await Service(tenantRead).GetCurriculumAsync(row.Id);
        detail.Success.Should().BeTrue();
        detail.Data!.SubjectCount.Should().Be(1);
        detail.Data.Subjects.Should().ContainSingle(x => x.SubjectName == "Biology" &&
            x.AcademicLevelName == "Level One" && x.PassMarks == 40);
        (await Service(tenantRead).GetCurriculumAsync(9999)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Tenant_admin_authorization_is_required_for_curriculum_register()
    {
        await using var db = Context(Options(), 501);
        (await Service(db, false).GetCurriculaAsync(null, null, null)).StatusCode.Should().Be(403);
        (await Service(db, false).GetCurriculumAsync(1)).StatusCode.Should().Be(403);
        var method = typeof(AcademicSetupController).GetMethod(nameof(AcademicSetupController.Curricula))!;
        method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single().Roles.Should().Be("TenantAdmin");
    }


    [Fact]
    public async Task Offering_dependencies_lock_mapping_but_identical_replay_remains_safe()
    {
        await using var db = Context(Options(), 501);
        var curriculum = new AcademicCurriculum { TenantId = 501, AcademicProgramId = 8,
            Name = "Used", Code = "USED", VersionNo = 1, IsCurrent = true,
            EffectiveFrom = new DateOnly(2025, 1, 1), RowVersion = Version.ToArray() };
        var level = new AcademicLevel { TenantId = 501, AcademicProgramId = 8, Name = "L1", Code = "L1" };
        var biology = new Subject { TenantId = 501, Name = "Biology", Code = "BIO" };
        var physics = new Subject { TenantId = 501, Name = "Physics", Code = "PHY" };
        db.AddRange(curriculum, level, biology, physics);
        await db.SaveChangesAsync();
        var mapping = new CurriculumSubject { TenantId = 501, AcademicCurriculumId = curriculum.Id,
            AcademicLevelId = level.Id, SubjectId = biology.Id, FullMarks = 100, PassMarks = 33 };
        db.Add(mapping);
        await db.SaveChangesAsync();
        db.Add(new SubjectOffering { TenantId = 501, AcademicBatchId = 22,
            AcademicYearId = 11, CurriculumSubjectId = mapping.Id, Code = "BIO-1" });
        await db.SaveChangesAsync();
        var service = Service(db);
        var detail = await service.GetCurriculumAsync(curriculum.Id);
        detail.Data!.IsSubjectMappingLocked.Should().BeTrue();
        detail.Data.SubjectOfferingCount.Should().Be(1);
        detail.Data.SubjectMappingLockReason.Should().Contain("Subject offerings");
        var register = await service.GetCurriculaAsync(null, null, null);
        register.Data!.Items.Should().ContainSingle(x => x.Id == curriculum.Id &&
            x.IsSubjectMappingLocked && x.SubjectOfferingCount == 1);
        var rejected = await service.RegisterCurriculumSubjectAsync(curriculum.Id,
            new SaveCurriculumSubjectRequestDto { AcademicLevelId = level.Id,
                SubjectId = physics.Id, FullMarks = 100, PassMarks = 33 });
        rejected.StatusCode.Should().Be(409);
        (await db.CurriculumSubjects.CountAsync()).Should().Be(1);
        var replay = await service.RegisterCurriculumSubjectAsync(curriculum.Id,
            new SaveCurriculumSubjectRequestDto { AcademicLevelId = level.Id,
                SubjectId = biology.Id, FullMarks = 100, PassMarks = 33 });
        replay.Success.Should().BeTrue();
        replay.Data!.Id.Should().Be(mapping.Id);
    }

    [Fact]
    public async Task Superseded_and_expired_versions_are_locked_while_unused_current_is_editable()
    {
        await using var db = Context(Options(), 501);
        var old = new AcademicCurriculum { TenantId = 501, AcademicProgramId = 7,
            Name = "Old", Code = "OLD", VersionNo = 1,
            EffectiveFrom = new DateOnly(2023, 1, 1), RowVersion = Version.ToArray() };
        var current = new AcademicCurriculum { TenantId = 501, AcademicProgramId = 7,
            Name = "Current", Code = "CURRENT", VersionNo = 2, IsCurrent = true,
            EffectiveFrom = new DateOnly(2025, 1, 1), RowVersion = Version.ToArray() };
        var expired = new AcademicCurriculum { TenantId = 501, AcademicProgramId = 9,
            Name = "Expired", Code = "EXPIRED", VersionNo = 1, IsCurrent = true,
            EffectiveFrom = new DateOnly(2020, 1, 1),
            EffectiveTo = new DateOnly(2021, 1, 1), RowVersion = Version.ToArray() };
        var level = new AcademicLevel { TenantId = 501, AcademicProgramId = 7, Name = "L1", Code = "L1" };
        var subject = new Subject { TenantId = 501, Name = "Math", Code = "MATH" };
        db.AddRange(old, current, expired, level, subject);
        await db.SaveChangesAsync();
        var request = new SaveCurriculumSubjectRequestDto { AcademicLevelId = level.Id,
            SubjectId = subject.Id, FullMarks = 100, PassMarks = 33 };
        (await Service(db).RegisterCurriculumSubjectAsync(old.Id, request)).StatusCode.Should().Be(409);
        (await Service(db).RegisterCurriculumSubjectAsync(expired.Id, request)).StatusCode.Should().Be(409);
        (await Service(db).GetCurriculumAsync(old.Id)).Data!.IsSubjectMappingLocked.Should().BeTrue();
        (await Service(db).GetCurriculumAsync(expired.Id)).Data!.IsSubjectMappingLocked.Should().BeTrue();
        (await Service(db).RegisterCurriculumSubjectAsync(current.Id, request)).Success.Should().BeTrue();
        (await db.CurriculumSubjects.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Historical_enrollment_without_offerings_locks_mapping_and_reports_count()
    {
        await using var db = Context(Options(), 501);
        var curriculum = new AcademicCurriculum { TenantId = 501, AcademicProgramId = 12,
            Name = "Historical", Code = "HIST", VersionNo = 1, IsCurrent = true,
            EffectiveFrom = new DateOnly(2025, 1, 1), RowVersion = Version.ToArray() };
        var level = new AcademicLevel { TenantId = 501, AcademicProgramId = 12, Name = "Level", Code = "L1" };
        var biology = new Subject { TenantId = 501, Name = "Biology", Code = "BIO" };
        var physics = new Subject { TenantId = 501, Name = "Physics", Code = "PHY" };
        db.AddRange(curriculum, level, biology, physics);
        await db.SaveChangesAsync();
        var mapping = new CurriculumSubject { TenantId = 501, AcademicCurriculumId = curriculum.Id,
            AcademicLevelId = level.Id, SubjectId = biology.Id, FullMarks = 100, PassMarks = 33 };
        db.Add(mapping);
        db.Add(new StudentEnrollment { TenantId = 501, AcademicCurriculumId = curriculum.Id,
            AcademicProgramId = 12, AcademicLevelId = level.Id, AcademicBatchId = 10,
            AcademicYearId = 20, CampusId = 30, StudentId = 40, RollNo = "OLD-01",
            EnrollmentDate = new DateOnly(2025, 2, 1), IsCurrent = false });
        await db.SaveChangesAsync();
        var service = Service(db);
        var detail = await service.GetCurriculumAsync(curriculum.Id);
        detail.Data!.StudentEnrollmentCount.Should().Be(1);
        detail.Data.SubjectOfferingCount.Should().Be(0);
        detail.Data.IsSubjectMappingLocked.Should().BeTrue();
        detail.Data.SubjectMappingLockReason.Should().Contain("Student enrollments");
        var list = await service.GetCurriculaAsync(null, 12, null);
        list.Data!.Items.Should().ContainSingle(x => x.Id == curriculum.Id &&
            x.StudentEnrollmentCount == 1 && x.IsSubjectMappingLocked);
        var newMapping = new SaveCurriculumSubjectRequestDto { AcademicLevelId = level.Id,
            SubjectId = physics.Id, FullMarks = 100, PassMarks = 33 };
        (await service.RegisterCurriculumSubjectAsync(curriculum.Id, newMapping)).StatusCode.Should().Be(409);
        (await db.CurriculumSubjects.CountAsync()).Should().Be(1);
        var replay = await service.RegisterCurriculumSubjectAsync(curriculum.Id,
            new SaveCurriculumSubjectRequestDto { AcademicLevelId = level.Id,
                SubjectId = biology.Id, FullMarks = 100, PassMarks = 33 });
        replay.Success.Should().BeTrue();
        replay.Data!.Id.Should().Be(mapping.Id);
    }

    [Fact]
    public async Task Other_tenant_enrollment_does_not_lock_unused_curriculum()
    {
        var options = Options();
        long curriculumId;
        await using (var db = Context(options, 501))
        {
            var curriculum = new AcademicCurriculum { TenantId = 501, AcademicProgramId = 15,
                Name = "Unused", Code = "UNUSED", IsCurrent = true,
                EffectiveFrom = new DateOnly(2025, 1, 1), RowVersion = Version.ToArray() };
            db.Add(curriculum);
            await db.SaveChangesAsync();
            curriculumId = curriculum.Id;
        }
        await using (var foreign = Context(options, 502))
        {
            foreign.Add(new StudentEnrollment { TenantId = 502, AcademicCurriculumId = curriculumId,
                AcademicProgramId = 15, AcademicLevelId = 1, AcademicBatchId = 10,
                AcademicYearId = 20, CampusId = 30, StudentId = 50, RollNo = "FOREIGN",
                EnrollmentDate = new DateOnly(2025, 2, 1) });
            await foreign.SaveChangesAsync();
        }
        await using var own = Context(options, 501);
        var detail = await Service(own).GetCurriculumAsync(curriculumId);
        detail.Data!.StudentEnrollmentCount.Should().Be(0);
        detail.Data.IsSubjectMappingLocked.Should().BeFalse();
        var list = await Service(own).GetCurriculaAsync(null, 15, null);
        list.Data!.Items.Should().ContainSingle(x => x.Id == curriculumId &&
            x.StudentEnrollmentCount == 0 && !x.IsSubjectMappingLocked);
    }

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("academic-curriculum-register-" + Guid.NewGuid().ToString("N")).Options;

    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "17"),
            new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", tenant.ToString())
        ], "TestAuthentication"));
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static AcademicSetupService Service(EduOSDbContext db, bool admin = true) => new(
        new GenericRepository<AcademicProgram>(db), new GenericRepository<AcademicLevel>(db),
        new GenericRepository<Subject>(db), new GenericRepository<AcademicCurriculum>(db),
        new GenericRepository<CurriculumSubject>(db), new GenericRepository<AcademicBatch>(db),
        new GenericRepository<Room>(db), new GenericRepository<ProgramCampus>(db),
        new GenericRepository<EduOS.Core.Entities.SaaS.Campus>(db),
        new GenericRepository<AcademicDepartment>(db),
        new GenericRepository<AcademicYear>(db), new GenericRepository<AcademicTerm>(db),
        new GenericRepository<AcademicTrack>(db), new GenericRepository<Medium>(db),
        new GenericRepository<Shift>(db), new GenericRepository<SubjectOffering>(db),
        new GenericRepository<StudentSubjectRegistration>(db), new GenericRepository<StudentEnrollment>(db), db, new User(admin),
        NullLogger<AcademicSetupService>.Instance);

    private sealed class User(bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 17;
        public long TenantId => 501;
        public string? FullName => "Academic Manager";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Teacher"];
        public bool IsInRole(string role) => admin ? role == "TenantAdmin" : role == "Teacher";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
