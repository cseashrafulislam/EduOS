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
        await using var db = Context(Options(), 501);
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
        db.Add(new CurriculumSubject { TenantId = 502, AcademicCurriculumId = row.Id,
            AcademicLevelId = level.Id, SubjectId = subject.Id });
        await db.SaveChangesAsync();
        var detail = await Service(db).GetCurriculumAsync(row.Id);
        detail.Success.Should().BeTrue();
        detail.Data!.SubjectCount.Should().Be(1);
        detail.Data.Subjects.Should().ContainSingle(x => x.SubjectName == "Biology" &&
            x.AcademicLevelName == "Level One" && x.PassMarks == 40);
        (await Service(db).GetCurriculumAsync(9999)).StatusCode.Should().Be(404);
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
        new GenericRepository<StudentSubjectRegistration>(db), db, new User(admin),
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
