using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
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

public sealed class AcademicProgramMetadataTests
{
    private static readonly byte[] Version = [1, 2, 3, 4, 5, 6, 7, 8];

    [Fact]
    public async Task Programme_register_and_details_are_tenant_scoped_and_include_inactive_rows()
    {
        var options = Options();
        long foreignId;
        await using (var foreign = Context(options, 402))
        {
            var row = new AcademicProgram
            { TenantId = 402, Name = "Foreign", Code = "FOR", IsActive = true, RowVersion = Version.ToArray() };
            foreign.Add(row); await foreign.SaveChangesAsync(); foreignId = row.Id;
        }
        await using var db = Context(options, 401);
        var own = new AcademicProgram
        { TenantId = 401, Name = "Inactive Programme", Code = "OLD", IsActive = false, RowVersion = Version.ToArray() };
        db.Add(own); await db.SaveChangesAsync();
        var sut = Service(db, 401);
        var list = await sut.GetProgramsAsync("Inactive", 1, 20);
        list.Success.Should().BeTrue();
        list.Data!.TotalCount.Should().Be(1);
        list.Data.Items.Should().ContainSingle(x => x.Id == own.Id && !x.IsActive);
        (await sut.GetProgramAsync(own.Id)).Success.Should().BeTrue();
        (await sut.GetProgramAsync(foreignId)).StatusCode.Should().Be(404);
        (await sut.GetProgramsAsync(null, 0, 20)).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Programme_reassignment_is_audited_idempotent_and_versioned_without_changing_campuses()
    {
        var options = Options();
        await using var db = Context(options, 401);
        var old = new AcademicDepartment { TenantId = 401, Name = "Arts", Code = "ART" };
        var next = new AcademicDepartment { TenantId = 401, Name = "Science", Code = "SCI" };
        db.AddRange(old, next); await db.SaveChangesAsync();
        var row = new AcademicProgram
        {
            TenantId = 401, AcademicDepartmentId = old.Id, Name = "Secondary",
            Code = "SEC", DurationInMonths = 12, IsActive = true,
            IsAdmissionOpen = true, RowVersion = Version.ToArray()
        };
        db.Add(row); await db.SaveChangesAsync();
        db.Add(new ProgramCampus { TenantId = 401, AcademicProgramId = row.Id, CampusId = 91, IsActive = true });
        await db.SaveChangesAsync();
        var sut = Service(db, 401);
        var request = new UpdateAcademicProgramRequestDto
        {
            AcademicDepartmentId = next.Id, Name = "Secondary Revised",
            DurationInMonths = 12, IsActive = true, IsAdmissionOpen = false,
            DisplayOrder = 2, RowVersion = Convert.ToBase64String(Version)
        };
        (await sut.UpdateProgramAsync(row.Id, request)).Success.Should().BeTrue();
        (await sut.UpdateProgramAsync(row.Id, request)).Success.Should().BeTrue();
        (await sut.GetProgramAsync(row.Id)).Data!.AcademicDepartmentName.Should().Be("Science");
        (await db.Set<AcademicProgram>().SingleAsync()).Code.Should().Be("SEC");
        (await db.Set<ProgramCampus>().CountAsync()).Should().Be(1);
        (await db.Set<AcademicProgram>().SingleAsync()).UpdatedBy.Should().Be(17);
        (await sut.UpdateProgramAsync(row.Id, new UpdateAcademicProgramRequestDto
        {
            AcademicDepartmentId = old.Id, Name = "Secondary Changed",
            DurationInMonths = 12, IsActive = true, IsAdmissionOpen = false,
            RowVersion = Convert.ToBase64String(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 })
        })).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Active_curricula_and_batches_block_duration_changes_and_deactivation()
    {
        var options = Options();
        await using var db = Context(options, 401);
        var row = new AcademicProgram
        {
            TenantId = 401, Name = "Primary", Code = "PRI", DurationInMonths = 12,
            IsActive = true, IsAdmissionOpen = true, RowVersion = Version.ToArray()
        };
        db.Add(row); await db.SaveChangesAsync();
        db.Add(new AcademicCurriculum
        {
            TenantId = 401, AcademicProgramId = row.Id, Name = "Curriculum",
            Code = "C1", EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true
        });
        await db.SaveChangesAsync();
        var sut = Service(db, 401);
        (await sut.UpdateProgramAsync(row.Id, new UpdateAcademicProgramRequestDto
        {
            Name = "Primary", DurationInMonths = 24, IsActive = true, IsAdmissionOpen = true,
            RowVersion = Convert.ToBase64String(Version)
        })).StatusCode.Should().Be(409);
        (await sut.UpdateProgramAsync(row.Id, new UpdateAcademicProgramRequestDto
        {
            Name = "Primary", DurationInMonths = 12, IsActive = false, IsAdmissionOpen = false,
            RowVersion = Convert.ToBase64String(Version)
        })).StatusCode.Should().Be(409);
        (await Service(db, 401, false).GetProgramsAsync(null)).StatusCode.Should().Be(403);
        (await Service(db, 401, false).UpdateProgramAsync(row.Id, new UpdateAcademicProgramRequestDto
        {
            Name = "Blocked", RowVersion = Convert.ToBase64String(Version)
        })).StatusCode.Should().Be(403);
    }

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("academic-programme-metadata-" + Guid.NewGuid().ToString("N")).Options;

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

    private static AcademicSetupService Service(EduOSDbContext db, long tenant, bool admin = true) => new(
        new GenericRepository<AcademicProgram>(db), new GenericRepository<AcademicLevel>(db),
        new GenericRepository<Subject>(db), new GenericRepository<AcademicCurriculum>(db),
        new GenericRepository<CurriculumSubject>(db), new GenericRepository<AcademicBatch>(db),
        new GenericRepository<Room>(db), new GenericRepository<ProgramCampus>(db),
        new GenericRepository<EduOS.Core.Entities.SaaS.Campus>(db), new GenericRepository<AcademicDepartment>(db),
        new GenericRepository<AcademicYear>(db), new GenericRepository<AcademicTerm>(db),
        new GenericRepository<AcademicTrack>(db), new GenericRepository<Medium>(db),
        new GenericRepository<Shift>(db), new GenericRepository<SubjectOffering>(db),
        new GenericRepository<StudentSubjectRegistration>(db), new GenericRepository<StudentEnrollment>(db), db, new User(tenant, admin),
        NullLogger<AcademicSetupService>.Instance);

    private sealed class User(long tenant, bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 17;
        public long TenantId => tenant;
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
