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

public sealed class AcademicRoomRegisterTests
{
    private static readonly byte[] Version = [1, 2, 3, 4, 5, 6, 7, 8];

    [Fact]
    public async Task Register_filters_campus_and_tenant_including_inactive_rooms()
    {
        var options = Options();
        await using (var foreign = Context(options, 502))
        {
            foreign.Add(new Room { TenantId = 502, CampusId = 10, Name = "Foreign room",
                Code = "FR", Capacity = 40, RowVersion = Version.ToArray() });
            await foreign.SaveChangesAsync();
        }
        await using var db = Context(options, 501);
        db.AddRange(
            new Room { TenantId = 501, CampusId = 10, Name = "Room A", Code = "A",
                Capacity = 40, IsActive = false, RowVersion = Version.ToArray() },
            new Room { TenantId = 501, CampusId = 11, Name = "Room B", Code = "B",
                Capacity = 50, RowVersion = Version.ToArray() });
        await db.SaveChangesAsync();
        var result = await Service(db).GetRoomsAsync("Room", null);
        result.Success.Should().BeTrue();
        result.Data!.TotalCount.Should().Be(2);
        result.Data.Items.Should().ContainSingle(x => x.Code == "A" && !x.IsActive);
        (await Service(db).GetRoomsAsync(null, null, 0)).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Update_is_idempotent_and_prevents_capacity_reduction_and_stale_version()
    {
        var options = Options();
        await using var db = Context(options, 501);
        var room = new Room { TenantId = 501, CampusId = 10, Name = "Room A",
            Code = "A", Capacity = 40, RowVersion = Version.ToArray() };
        db.Add(room); await db.SaveChangesAsync();
        var sut = Service(db);
        var request = new UpdateAcademicRoomRequestDto { Name = "Room A Updated",
            Capacity = 45, RoomType = "Lab", RowVersion = Convert.ToBase64String(Version) };
        (await sut.UpdateRoomAsync(room.Id, request)).Success.Should().BeTrue();
        (await sut.UpdateRoomAsync(room.Id, request)).Success.Should().BeTrue();
        room.Code.Should().Be("A");
        room.CampusId.Should().Be(10);
        room.UpdatedBy.Should().Be(17);
        (await sut.UpdateRoomAsync(room.Id, new UpdateAcademicRoomRequestDto
        { Name = "Reduced", Capacity = 20, RowVersion = Convert.ToBase64String(Version) })).StatusCode.Should().Be(409);
        (await sut.UpdateRoomAsync(room.Id, new UpdateAcademicRoomRequestDto
        { Name = "Stale", Capacity = 50, RowVersion = Convert.ToBase64String([9, 9, 9, 9, 9, 9, 9, 9]) })).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Non_admin_is_denied_and_room_update_requires_tenant_admin()
    {
        var options = Options();
        await using var db = Context(options, 501);
        (await Service(db, false).GetRoomsAsync(null, null)).StatusCode.Should().Be(403);
        (await Service(db, false).UpdateRoomAsync(1, new UpdateAcademicRoomRequestDto())).StatusCode.Should().Be(403);
        var method = typeof(AcademicSetupController).GetMethod(nameof(AcademicSetupController.UpdateRoom))!;
        method.GetCustomAttributes(typeof(HttpPutAttribute), true).Should().ContainSingle();
        method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single().Roles.Should().Be("TenantAdmin");
    }

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("academic-room-register-" + Guid.NewGuid().ToString("N")).Options;

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
