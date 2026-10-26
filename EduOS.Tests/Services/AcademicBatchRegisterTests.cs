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

public sealed class AcademicBatchRegisterTests
{
    private static readonly byte[] Version = [1, 2, 3, 4, 5, 6, 7, 8];

    [Fact]
    public async Task Register_includes_inactive_sections_and_filters_tenant_campus_and_year()
    {
        var options = Options();
        // Seed the foreign tenant first. HttpContextAccessor uses ambient context, so the
        // active tenant must be restored before constructing the service and querying.
        await using (var foreign = Context(options, 402))
        {
            foreign.Add(new AcademicBatch { TenantId = 402, Name = "Section Foreign",
                Code = "F", CampusId = 5, AcademicYearId = 7, IsActive = true,
                RowVersion = Version.ToArray() });
            await foreign.SaveChangesAsync();
        }
        await using var db = Context(options, 401);
        db.AddRange(
            new AcademicBatch { TenantId = 401, Name = "Section A", Code = "A", CampusId = 5,
                AcademicYearId = 7, IsActive = false, RowVersion = Version.ToArray() },
            new AcademicBatch { TenantId = 401, Name = "Section B", Code = "B", CampusId = 6,
                AcademicYearId = 7, IsActive = true, RowVersion = Version.ToArray() });
        await db.SaveChangesAsync();
        var sut = Service(db, 401);
        var result = await sut.GetBatchesAsync("Section", 5, 7);
        result.Success.Should().BeTrue();
        result.Data!.Items.Should().ContainSingle(x => x.Code == "A" && !x.IsActive);
        result.Data.TotalCount.Should().Be(1);
        (await sut.GetBatchesAsync(null, null, null, 0)).StatusCode.Should().Be(400);
        (await sut.GetBatchesAsync(null, -1, null)).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Metadata_write_is_idempotent_audited_and_prevents_capacity_reduction_or_stale_version()
    {
        var options = Options();
        await using var db = Context(options, 401);
        var row = new AcademicBatch { TenantId = 401, Name = "Section A", Code = "A",
            CampusId = 5, AcademicYearId = 7, Capacity = 40, RowVersion = Version.ToArray() };
        db.Add(row); await db.SaveChangesAsync();
        var sut = Service(db, 401);
        var request = new UpdateAcademicBatchRequestDto
        { Name = "Section Updated", Capacity = 50, DisplayOrder = 2, RowVersion = Convert.ToBase64String(Version) };
        (await sut.UpdateBatchAsync(row.Id, request)).Success.Should().BeTrue();
        (await sut.UpdateBatchAsync(row.Id, request)).Success.Should().BeTrue();
        var updated = await db.Set<AcademicBatch>().SingleAsync();
        updated.Code.Should().Be("A");
        updated.CampusId.Should().Be(5);
        updated.UpdatedBy.Should().Be(17);
        (await sut.UpdateBatchAsync(row.Id, new UpdateAcademicBatchRequestDto
        { Name = "Reduced", Capacity = 20, RowVersion = Convert.ToBase64String(Version) })).StatusCode.Should().Be(409);
        (await sut.UpdateBatchAsync(row.Id, new UpdateAcademicBatchRequestDto
        { Name = "Stale", Capacity = 60, RowVersion = Convert.ToBase64String([9, 9, 9, 9, 9, 9, 9, 9]) })).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Tenant_admin_only_and_put_contract_are_enforced()
    {
        var options = Options();
        await using var db = Context(options, 401);
        (await Service(db, 401, false).GetBatchesAsync(null, null, null)).StatusCode.Should().Be(403);
        (await Service(db, 401, false).UpdateBatchAsync(1, new UpdateAcademicBatchRequestDto())).StatusCode.Should().Be(403);
        var method = typeof(AcademicSetupController).GetMethod(nameof(AcademicSetupController.UpdateBatch))!;
        method.GetCustomAttributes(typeof(HttpPutAttribute), true).Should().ContainSingle();
        method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single().Roles.Should().Be("TenantAdmin");
    }

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("academic-batch-register-" + Guid.NewGuid().ToString("N")).Options;

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
