using EduOS.Core.DTOs.Student;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Students;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentDirectoryServiceTests
{
    [Fact]
    public async Task Directory_masks_phone_and_returns_canonical_enrollment_and_guardian_details()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var student = await SeedAsync(db, 101);
        var service = Service(db, 101);
        var page = await service.GetPageAsync(new StudentDirectoryQueryDto
        {
            Search = student.StudentCode, AcademicLevelId = 3
        });
        var details = await service.GetAsync(student.PublicId);
        page.Success.Should().BeTrue();
        page.Data!.Items.Should().ContainSingle();
        page.Data.Items[0].RollNo.Should().Be("12");
        page.Data.Items[0].AcademicLevelName.Should().Be("Level Six");
        page.Data.Items[0].MaskedMobile.Should().EndWith("5678")
            .And.NotBe("+8801712345678");
        details.Success.Should().BeTrue();
        details.Data!.Phone.Should().Be("+8801712345678");
        details.Data.DateOfBirth.Should().Be(new DateOnly(2012, 2, 3));
        details.Data.AdmissionDate.Should().Be(new DateOnly(2026, 9, 8));
        details.Data.Guardians.Should().ContainSingle();
        details.Data.Enrollments.Should().ContainSingle();
        details.Data.Enrollments[0].State.Should().Be(EduOS.Core.Enums.Domain.EnrollmentState.Active);
    }

    [Fact]
    public async Task Directory_filters_inside_sql_and_rejects_foreign_tenant_records()
    {
        var options = Options();
        Guid foreignReference;
        await using (var other = Context(options, 202))
            foreignReference = (await SeedAsync(other, 202)).PublicId;
        await using var db = Context(options, 101);
        var service = Service(db, 101);
        var page = await service.GetPageAsync(new StudentDirectoryQueryDto { AcademicYearId = 2 });
        page.Success.Should().BeTrue();
        page.Data!.Items.Should().BeEmpty();
        (await service.GetAsync(foreignReference)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Unprivileged_role_cannot_read_student_directory()
    {
        await using var db = Context(Options(), 101);
        var service = Service(db, 101, "Teacher");
        (await service.GetPageAsync(new StudentDirectoryQueryDto())).StatusCode.Should().Be(403);
    }

    private static StudentDirectoryService Service(EduOSDbContext db, long tenant, string role = "TenantAdmin") => new(
        new GenericRepository<Student>(db),
        new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<StudentGuardian>(db),
        new GenericRepository<Guardian>(db),
        new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicTerm>(db),
        new GenericRepository<AcademicLevel>(db),
        new GenericRepository<AcademicBatch>(db),
        new GenericRepository<AcademicTrack>(db),
        new GenericRepository<Campus>(db),
        new TestCurrentUser(tenant, role),
        NullLogger<StudentDirectoryService>.Instance);

    private static async Task<Student> SeedAsync(EduOSDbContext db, long tenant)
    {
        var campus = new Campus { TenantId = tenant, Name = "Campus", Code = "C" };
        var year = new AcademicYear { TenantId = tenant, Name = "2026",
            Code = "Y2026", StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31) };
        var program = new AcademicProgram { TenantId = tenant, Name = "School", Code = "P" };
        db.AddRange(campus, year, program);
        await db.SaveChangesAsync();
        var level = new AcademicLevel { TenantId = tenant, AcademicProgramId = program.Id,
            Name = "Level Six", Code = "L6", LevelNo = 6 };
        db.Add(level); await db.SaveChangesAsync();
        var batch = new AcademicBatch { TenantId = tenant, CampusId = campus.Id, AcademicYearId = year.Id,
            AcademicProgramId = program.Id, AcademicLevelId = level.Id,
            Name = "Batch A", Code = "BA", Capacity = 30 };
        var curriculum = new AcademicCurriculum { TenantId = tenant, AcademicProgramId = program.Id,
            Name = "Main Curriculum", Code = "CUR", EffectiveFrom = new DateOnly(2026, 1, 1) };
        db.AddRange(batch, curriculum); await db.SaveChangesAsync();
        var student = new Student
        {
            TenantId = tenant, PublicId = Guid.NewGuid(),
            StudentCode = "STU-" + Guid.NewGuid().ToString("N"),
            FullName = "Rahim Uddin", FullNameBangla = "রহিম উদ্দিন",
            Phone = "+8801712345678", Gender = "Male",
            DateOfBirth = new DateOnly(2012, 2, 3),
            AdmissionDate = new DateOnly(2026, 9, 8), StatusCode = "Active"
        };
        db.Add(student); await db.SaveChangesAsync();
        var guardian = new Guardian { TenantId = tenant, FullName = "Karim Uddin", Phone = "+8801700000000" };
        db.Add(guardian); await db.SaveChangesAsync();
        db.AddRange(
            new StudentGuardian { TenantId = tenant, StudentId = student.Id,
                GuardianId = guardian.Id, RelationCode = "Father", IsPrimary = true },
            new StudentEnrollment
            {
                TenantId = tenant, StudentId = student.Id, CampusId = campus.Id,
                AcademicYearId = year.Id, AcademicProgramId = program.Id,
                AcademicLevelId = level.Id, AcademicBatchId = batch.Id,
                AcademicCurriculumId = curriculum.Id, RollNo = "12",
                EnrollmentDate = new DateOnly(2026, 9, 8), IsCurrent = true
            });
        await db.SaveChangesAsync();
        return student;
    }

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("student-directory-" + Guid.NewGuid().ToString("N")).Options;

    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "7"),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", tenant.ToString())
            }, "TestAuthentication"))
        };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed class TestCurrentUser(long tenant, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => "Directory User";
        public string? Email => "directory@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => new[] { role };
        public bool IsInRole(string requestedRole) => requestedRole == role;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "EduOS tests";
    }
}
