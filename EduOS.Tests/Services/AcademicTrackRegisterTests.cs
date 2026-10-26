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

public sealed class AcademicTrackRegisterTests
{
    private static readonly byte[] Version = [1, 2, 3, 4, 5, 6, 7, 8];

    [Fact]
    public async Task Track_register_is_tenant_scoped_paged_and_includes_inactive_and_global()
    {
        var options = Options();
        await using (var foreign = Context(options, 502))
        {
            foreign.Add(new AcademicTrack { TenantId = 502, Name = "Foreign",
                Code = "FOREIGN", RowVersion = Version.ToArray() });
            await foreign.SaveChangesAsync();
        }
        await using var db = Context(options, 501);
        var programme = new AcademicProgram { TenantId = 501, Name = "Secondary", Code = "SEC", IsActive = true };
        db.Add(programme); await db.SaveChangesAsync();
        db.AddRange(
            new AcademicTrack { TenantId = 501, AcademicProgramId = programme.Id,
                Name = "Science", Code = "SCI", IsActive = false, RowVersion = Version.ToArray() },
            new AcademicTrack { TenantId = 501, Name = "General", Code = "GEN",
                RowVersion = Version.ToArray() });
        await db.SaveChangesAsync();
        var service = Service(db);
        var all = await service.GetTracksAsync(null, null, 1, 20);
        all.Success.Should().BeTrue();
        all.Data!.TotalCount.Should().Be(2);
        all.Data.Items.Should().ContainSingle(x => x.AcademicProgramId == null);
        var filtered = await service.GetTracksAsync("Sci", programme.Id, 1, 1);
        filtered.Data!.TotalCount.Should().Be(1);
        filtered.Data.Items.Should().ContainSingle(x => x.Code == "SCI" &&
            !x.IsActive && x.AcademicProgramName == "Secondary");
        (await service.GetTracksAsync(null, null, 0)).StatusCode.Should().Be(400);
        (await service.GetTracksAsync(null, 99999)).StatusCode.Should().Be(404);
        (await service.GetTrackAsync(99999)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Track_update_is_audited_idempotent_and_rejects_stale_rowversion()
    {
        await using var db = Context(Options(), 501);
        var track = new AcademicTrack { TenantId = 501, Name = "General",
            Code = "GEN", RowVersion = Version.ToArray() };
        db.Add(track); await db.SaveChangesAsync();
        var service = Service(db);
        var request = new UpdateAcademicTrackRequestDto { Name = "General Studies",
            Description = "School-wide", IsDefault = true, IsActive = true,
            DisplayOrder = 2, RowVersion = Convert.ToBase64String(Version) };
        (await service.UpdateTrackAsync(track.Id, request)).Success.Should().BeTrue();
        (await service.UpdateTrackAsync(track.Id, request)).Success.Should().BeTrue();
        track.Code.Should().Be("GEN");
        track.AcademicProgramId.Should().BeNull();
        track.UpdatedBy.Should().Be(17);
        request.Name = "Stale";
        request.RowVersion = Convert.ToBase64String([9, 9, 9, 9, 9, 9, 9, 9]);
        (await service.UpdateTrackAsync(track.Id, request)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Track_default_conflict_and_referenced_deactivation_are_blocked()
    {
        await using var db = Context(Options(), 501);
        var programme = new AcademicProgram { TenantId = 501, Name = "Secondary", Code = "SEC", IsActive = true };
        db.Add(programme); await db.SaveChangesAsync();
        var first = new AcademicTrack { TenantId = 501, AcademicProgramId = programme.Id,
            Name = "Science", Code = "SCI", IsDefault = true, RowVersion = Version.ToArray() };
        var second = new AcademicTrack { TenantId = 501, AcademicProgramId = programme.Id,
            Name = "Commerce", Code = "COM", RowVersion = Version.ToArray() };
        db.AddRange(first, second); await db.SaveChangesAsync();
        var service = Service(db);
        var update = new UpdateAcademicTrackRequestDto { Name = "Commerce",
            IsDefault = true, IsActive = true, RowVersion = Convert.ToBase64String(Version) };
        (await service.UpdateTrackAsync(second.Id, update)).StatusCode.Should().Be(409);
        db.Add(new AcademicBatch { TenantId = 501, AcademicProgramId = programme.Id,
            AcademicLevelId = 1, AcademicTrackId = second.Id,
            CampusId = 1, AcademicYearId = 1, Name = "9A", Code = "9A" });
        await db.SaveChangesAsync();
        update.IsDefault = false; update.IsActive = false;
        (await service.UpdateTrackAsync(second.Id, update)).StatusCode.Should().Be(409);
        update.IsActive = true; update.Name = "Commerce Updated";
        (await service.UpdateTrackAsync(second.Id, update)).Success.Should().BeTrue();
    }

    [Fact]
    public async Task Track_register_and_mutations_require_tenant_admin()
    {
        await using var db = Context(Options(), 501);
        (await Service(db, false).GetTracksAsync(null, null)).StatusCode.Should().Be(403);
        (await Service(db, false).UpdateTrackAsync(1, new UpdateAcademicTrackRequestDto())).StatusCode.Should().Be(403);
        var method = typeof(AcademicSetupController).GetMethod(nameof(AcademicSetupController.UpdateTrack))!;
        method.GetCustomAttributes(typeof(HttpPutAttribute), true).Should().ContainSingle();
        method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single().Roles.Should().Be("TenantAdmin");
    }

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("academic-track-register-" + Guid.NewGuid().ToString("N")).Options;

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
        new GenericRepository<EduOS.Core.Entities.SaaS.Campus>(db), new GenericRepository<AcademicDepartment>(db),
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
