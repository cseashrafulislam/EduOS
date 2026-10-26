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

public sealed class AcademicLevelRegisterTests
{
    private static readonly byte[] Version = [1, 2, 3, 4, 5, 6, 7, 8];

    [Fact]
    public async Task Level_register_is_tenant_scoped_paged_and_includes_inactive()
    {
        var options = Options();
        await using (var foreign = Context(options, 502))
        {
            foreign.Add(new AcademicLevel { TenantId = 502, AcademicProgramId = 999,
                Name = "Foreign", Code = "FOREIGN", LevelNo = 10, RowVersion = Version.ToArray() });
            await foreign.SaveChangesAsync();
        }
        await using var db = Context(options, 501);
        var programme = new AcademicProgram { TenantId = 501, Name = "Secondary", Code = "SEC", IsActive = true };
        db.Add(programme); await db.SaveChangesAsync();
        db.AddRange(
            new AcademicLevel { TenantId = 501, AcademicProgramId = programme.Id, Name = "Grade 8",
                Code = "G8", LevelNo = 8, IsActive = false, RowVersion = Version.ToArray() },
            new AcademicLevel { TenantId = 501, AcademicProgramId = programme.Id, Name = "Grade 9",
                Code = "G9", LevelNo = 9, RowVersion = Version.ToArray() });
        await db.SaveChangesAsync();
        var sut = Service(db);
        var first = await sut.GetLevelsAsync("Grade", programme.Id, 1, 1);
        first.Success.Should().BeTrue();
        first.Data!.TotalCount.Should().Be(2);
        first.Data.Items.Should().ContainSingle(x => x.Code == "G8" && !x.IsActive &&
            x.AcademicProgramName == "Secondary");
        (await sut.GetLevelsAsync(null, null, 0)).StatusCode.Should().Be(400);
        (await sut.GetLevelsAsync(null, 99999)).StatusCode.Should().Be(404);
        (await sut.GetLevelAsync(99999)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Level_update_is_idempotent_audited_and_checks_stale_version()
    {
        var options = Options();
        await using var db = Context(options, 501);
        var programme = new AcademicProgram { TenantId = 501, Name = "Secondary", Code = "SEC", IsActive = true };
        db.Add(programme); await db.SaveChangesAsync();
        var level = new AcademicLevel { TenantId = 501, AcademicProgramId = programme.Id,
            Name = "Grade 8", Code = "G8", LevelNo = 8, RowVersion = Version.ToArray() };
        db.Add(level); await db.SaveChangesAsync();
        var sut = Service(db);
        var request = new UpdateAcademicLevelRequestDto { Name = "Grade Eight", LevelNo = 8,
            IsPromotable = true, IsActive = true, DisplayOrder = 2,
            RowVersion = Convert.ToBase64String(Version) };
        (await sut.UpdateLevelAsync(level.Id, request)).Success.Should().BeTrue();
        (await sut.UpdateLevelAsync(level.Id, request)).Success.Should().BeTrue();
        level.Code.Should().Be("G8");
        level.AcademicProgramId.Should().Be(programme.Id);
        level.UpdatedBy.Should().Be(17);
        (await sut.UpdateLevelAsync(level.Id, new UpdateAcademicLevelRequestDto {
            Name = "Stale", LevelNo = 8, IsPromotable = true, DisplayOrder = 2,
            RowVersion = Convert.ToBase64String([9, 9, 9, 9, 9, 9, 9, 9]) })).StatusCode.Should().Be(409);
        (await sut.UpdateLevelAsync(level.Id, new UpdateAcademicLevelRequestDto {
            Name = "Invalid", LevelNo = 8, IsPromotable = true, IsTerminalLevel = true,
            RowVersion = Convert.ToBase64String(Version) })).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Referenced_level_blocks_progression_number_and_deactivation_changes()
    {
        var options = Options();
        await using var db = Context(options, 501);
        var programme = new AcademicProgram { TenantId = 501, Name = "Secondary", Code = "SEC", IsActive = true };
        db.Add(programme); await db.SaveChangesAsync();
        var level = new AcademicLevel { TenantId = 501, AcademicProgramId = programme.Id,
            Name = "Grade 9", Code = "G9", LevelNo = 9, RowVersion = Version.ToArray() };
        db.Add(level); await db.SaveChangesAsync();
        db.Add(new AcademicBatch { TenantId = 501, AcademicProgramId = programme.Id,
            AcademicLevelId = level.Id, Name = "9A", Code = "9A", CampusId = 1, AcademicYearId = 1 });
        await db.SaveChangesAsync();
        var sut = Service(db);
        var request = new UpdateAcademicLevelRequestDto { Name = "Grade Nine", LevelNo = 10,
            IsPromotable = true, IsActive = true, RowVersion = Convert.ToBase64String(Version) };
        (await sut.UpdateLevelAsync(level.Id, request)).StatusCode.Should().Be(409);
        request.LevelNo = 9; request.IsTerminalLevel = true; request.IsPromotable = false;
        (await sut.UpdateLevelAsync(level.Id, request)).StatusCode.Should().Be(409);
        request.IsTerminalLevel = false; request.IsPromotable = true; request.IsActive = false;
        (await sut.UpdateLevelAsync(level.Id, request)).StatusCode.Should().Be(409);
        request.IsActive = true;
        (await sut.UpdateLevelAsync(level.Id, request)).Success.Should().BeTrue();
    }

    [Fact]
    public async Task Level_update_requires_tenant_admin_and_authorized_put()
    {
        var options = Options();
        await using var db = Context(options, 501);
        (await Service(db, false).GetLevelsAsync(null, null)).StatusCode.Should().Be(403);
        (await Service(db, false).UpdateLevelAsync(1, new UpdateAcademicLevelRequestDto())).StatusCode.Should().Be(403);
        var method = typeof(AcademicSetupController).GetMethod(nameof(AcademicSetupController.UpdateLevel))!;
        method.GetCustomAttributes(typeof(HttpPutAttribute), true).Should().ContainSingle();
        method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single().Roles.Should().Be("TenantAdmin");
    }

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("academic-level-register-" + Guid.NewGuid().ToString("N")).Options;

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
