using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
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

public sealed class AcademicDepartmentSetupTests
{
    [Fact]
    public async Task Create_search_and_retry_are_tenant_scoped_and_idempotent()
    {
        var options = Options();
        await using var db = Context(options, 301);
        var sut = Service(db, 301);
        var request = new SaveAcademicDepartmentRequestDto
        { Name = "Science", Code = " sci ", Description = "STEM" };
        var created = await sut.CreateDepartmentAsync(request);
        var replay = await sut.CreateDepartmentAsync(request);
        created.StatusCode.Should().Be(201);
        replay.Data!.Id.Should().Be(created.Data!.Id);
        (await db.Set<AcademicDepartment>().CountAsync()).Should().Be(1);
        (await sut.GetDepartmentsAsync("SCI")).Data!.Items.Should().ContainSingle(x => x.Code == "SCI");
        (await sut.CreateDepartmentAsync(new SaveAcademicDepartmentRequestDto
        { Name = "Different", Code = "SCI" })).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Update_requires_version_and_protects_active_programmes()
    {
        var options = Options();
        await using var db = Context(options, 301);
        var sut = Service(db, 301);
        var created = (await sut.CreateDepartmentAsync(new SaveAcademicDepartmentRequestDto
        { Name = "Science", Code = "SCI" })).Data!;
        var row = await db.Set<AcademicDepartment>().SingleAsync();
        row.RowVersion = [1, 2, 3, 4, 5, 6, 7, 8];
        await db.SaveChangesAsync();
        var version = Convert.ToBase64String(row.RowVersion);
        db.Add(new AcademicProgram
        { TenantId = 301, AcademicDepartmentId = created.Id, Name = "Secondary", Code = "SEC" });
        await db.SaveChangesAsync();
        (await sut.UpdateDepartmentAsync(created.Id, new SaveAcademicDepartmentRequestDto
        { Name = "Science", Code = "SCI", IsActive = false, RowVersion = version }))
            .StatusCode.Should().Be(409);
        (await sut.UpdateDepartmentAsync(created.Id, new SaveAcademicDepartmentRequestDto
        { Name = "Natural Sciences", Code = "SCI", IsActive = true, RowVersion = version }))
            .Success.Should().BeTrue();
        (await sut.UpdateDepartmentAsync(created.Id, new SaveAcademicDepartmentRequestDto
        { Name = "Physical Sciences", Code = "SCI", IsActive = true,
          RowVersion = Convert.ToBase64String(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 }) }))
            .StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Foreign_tenant_and_non_admin_cannot_mutate_department()
    {
        var options = Options();
        await using (var db = Context(options, 301))
            (await Service(db, 301).CreateDepartmentAsync(new SaveAcademicDepartmentRequestDto
            { Name = "Science", Code = "SCI" })).Success.Should().BeTrue();
        await using var other = Context(options, 302);
        (await Service(other, 302).GetDepartmentsAsync(null)).Data!.Items.Should().BeEmpty();
        (await Service(other, 302).UpdateDepartmentAsync(1, new SaveAcademicDepartmentRequestDto
        { Name = "Foreign", Code = "SCI", RowVersion = Convert.ToBase64String(new byte[8]) }))
            .StatusCode.Should().Be(404);
        (await Service(other, 302, false).CreateDepartmentAsync(new SaveAcademicDepartmentRequestDto
        { Name = "Denied", Code = "DEN" })).StatusCode.Should().Be(403);
    }

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("academic-departments-" + Guid.NewGuid().ToString("N")).Options;

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
        new GenericRepository<Campus>(db), new GenericRepository<AcademicDepartment>(db),
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
