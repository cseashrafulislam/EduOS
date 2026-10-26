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
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AcademicSubjectRegisterTests
{
    private static readonly byte[] Version = [1, 2, 3, 4, 5, 6, 7, 8];

    [Fact]
    public async Task Subject_register_is_tenant_scoped_paged_searchable_and_includes_inactive()
    {
        var options = Options();
        await using (var foreign = Context(options, 502))
        {
            foreign.Add(new Subject { TenantId = 502, Name = "Foreign", Code = "FOR",
                RowVersion = Version.ToArray() });
            await foreign.SaveChangesAsync();
        }
        await using var db = Context(options, 501);
        db.AddRange(
            new Subject { TenantId = 501, Name = "Algebra", Code = "ALG",
                RowVersion = Version.ToArray() },
            new Subject { TenantId = 501, Name = "Biology", Code = "BIO",
                IsActive = false, RowVersion = Version.ToArray() });
        await db.SaveChangesAsync();
        var service = Service(db);
        var all = await service.GetSubjectsAsync(null, 1, 1);
        all.Success.Should().BeTrue();
        all.Data!.TotalCount.Should().Be(2);
        all.Data.Items.Should().ContainSingle(x => x.Code == "ALG");
        var filtered = await service.GetSubjectsAsync("Bio", 1, 20);
        filtered.Data!.Items.Should().ContainSingle(x => x.Code == "BIO" && !x.IsActive);
        (await service.GetSubjectsAsync(null, 0)).StatusCode.Should().Be(400);
        (await service.GetSubjectsAsync(null, 1, 101)).StatusCode.Should().Be(400);
        (await service.GetSubjectAsync(9999)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Subject_update_preserves_code_audits_retries_and_rejects_stale_version()
    {
        await using var db = Context(Options(), 501);
        var subject = new Subject { TenantId = 501, Name = "Physics", Code = "PHY",
            RowVersion = Version.ToArray() };
        db.Add(subject); await db.SaveChangesAsync();
        var service = Service(db);
        var request = new UpdateAcademicSubjectRequestDto
        {
            Name = "Applied Physics", ShortName = "AP", DefaultCreditHours = 3,
            IsActive = true, RowVersion = Convert.ToBase64String(Version)
        };
        (await service.UpdateSubjectAsync(subject.Id, request)).Success.Should().BeTrue();
        (await service.UpdateSubjectAsync(subject.Id, request)).Success.Should().BeTrue();
        subject.Code.Should().Be("PHY");
        subject.UpdatedBy.Should().Be(17);
        request.Name = "Outdated";
        request.RowVersion = Convert.ToBase64String([9, 9, 9, 9, 9, 9, 9, 9]);
        (await service.UpdateSubjectAsync(subject.Id, request)).StatusCode.Should().Be(409);
        (await service.GetSubjectAsync(subject.Id)).Data!.Name.Should().Be("Applied Physics");
    }

    [Fact]
    public async Task Subject_with_historical_curriculum_mapping_cannot_be_deactivated()
    {
        await using var db = Context(Options(), 501);
        var subject = new Subject { TenantId = 501, Name = "Chemistry", Code = "CHE",
            RowVersion = Version.ToArray() };
        db.Add(subject); await db.SaveChangesAsync();
        db.Add(new CurriculumSubject { TenantId = 501, SubjectId = subject.Id,
            AcademicCurriculumId = 1, AcademicLevelId = 1, IsActive = false });
        await db.SaveChangesAsync();
        var service = Service(db);
        var request = new UpdateAcademicSubjectRequestDto
        {
            Name = "Chemistry", DefaultCreditHours = 0, IsActive = false,
            RowVersion = Convert.ToBase64String(Version)
        };
        (await service.UpdateSubjectAsync(subject.Id, request)).StatusCode.Should().Be(409);
        request.IsActive = true;
        request.DefaultCreditHours = 4;
        (await service.UpdateSubjectAsync(subject.Id, request)).Success.Should().BeTrue();
        subject.DefaultCreditHours.Should().Be(4);
    }

    [Fact]
    public async Task Subject_admin_endpoints_and_service_reject_unprivileged_access()
    {
        await using var db = Context(Options(), 501);
        (await Service(db, false).GetSubjectsAsync(null)).StatusCode.Should().Be(403);
        (await Service(db, false).UpdateSubjectAsync(1, new UpdateAcademicSubjectRequestDto()))
            .StatusCode.Should().Be(403);
        var method = typeof(AcademicSetupController).GetMethod(nameof(AcademicSetupController.UpdateSubject))!;
        method.GetCustomAttributes(typeof(HttpPutAttribute), true).Should().ContainSingle();
        method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single().Roles.Should().Be("TenantAdmin");
    }

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("academic-subject-register-" + Guid.NewGuid().ToString("N")).Options;

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
