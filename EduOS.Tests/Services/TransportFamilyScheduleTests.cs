using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Transport;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;
using TransportRoute = EduOS.Core.Entities.Transport.Route;

namespace EduOS.Tests.Services;

public sealed class TransportFamilyScheduleTests
{
    [Fact]
    public async Task Linked_guardian_sees_only_live_relationship_and_never_fare_or_internal_ids()
    {
        await using var db = CreateContext(101);
        var (student, guardian, link) = await SeedAsync(db);
        var service = new TransportFamilyScheduleService(db, new User(101, 42, "Guardian"), TimeProvider.System);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        var result = await service.GetAsync(today, 1, 20);
        result.Success.Should().BeTrue(result.Message);
        result.Data!.Items.Should().ContainSingle(x => x.StudentCode == student.StudentCode &&
            x.VehicleNumber == "FAMILY-BUS" && x.PickupStop == "Main gate");
        var csv = await service.ExportCsvAsync(today);
        csv.Success.Should().BeTrue(csv.Message);
        csv.Data.Should().Contain("FAMILY-BUS").And.NotContain("1750");
        link.IsDeleted = true;
        await db.SaveChangesAsync();
        (await service.GetAsync(today, 1, 20)).Data!.Items.Should().BeEmpty();
        (await service.ExportCsvAsync(today)).Data.Should().NotContain("FAMILY-BUS");
        guardian.IsActive = false;
        await db.SaveChangesAsync();
        (await service.GetAsync(today, 1, 20)).Data!.Items.Should().BeEmpty();
    }


    [Theory]
    [InlineData("Guardian")]
    [InlineData("Parent")]
    public async Task Family_schedule_and_export_require_current_pickup_authorization(string role)
    {
        await using var db = CreateContext(101);
        var (_, _, link) = await SeedAsync(db);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        var family = new TransportFamilyScheduleService(db, new User(101, 42, role), TimeProvider.System);
        var student = new TransportFamilyScheduleService(db, new User(101, 21, "Student"), TimeProvider.System);

        (await family.GetAsync(today, 1, 20)).Data!.Items.Should().ContainSingle();
        (await family.ExportCsvAsync(today)).Data.Should().Contain("FAMILY-BUS");

        // V1 guardian relationship permission changes must immediately affect both V2 read paths.
        link.IsAuthorizedPickup = false;
        await db.SaveChangesAsync();
        (await family.GetAsync(today, 1, 20)).Data!.Items.Should().BeEmpty();
        (await family.ExportCsvAsync(today)).Data.Should().NotContain("FAMILY-BUS");
        // Student self-service does not depend on a guardian's pickup permission.
        (await student.GetAsync(today, 1, 20)).Data!.Items.Should().ContainSingle();

        link.IsAuthorizedPickup = true;
        await db.SaveChangesAsync();
        (await family.GetAsync(today, 1, 20)).Data!.Items.Should().ContainSingle();
        (await family.ExportCsvAsync(today)).Data.Should().Contain("FAMILY-BUS");

        link.IsDeleted = true;
        await db.SaveChangesAsync();
        (await family.GetAsync(today, 1, 20)).Data!.Items.Should().BeEmpty();
        (await family.ExportCsvAsync(today)).Data.Should().NotContain("FAMILY-BUS");
    }

    [Fact]
    public async Task Student_sees_own_schedule_but_not_another_students_schedule()
    {
        await using var db = CreateContext(101);
        await SeedAsync(db);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        var owner = new TransportFamilyScheduleService(db, new User(101, 21, "Student"), TimeProvider.System);
        (await owner.GetAsync(today, 1, 20)).Data!.Items.Should().ContainSingle();
        var stranger = new TransportFamilyScheduleService(db, new User(101, 22, "Student"), TimeProvider.System);
        (await stranger.GetAsync(today, 1, 20)).Data!.Items.Should().BeEmpty();
        (await stranger.ExportCsvAsync(today)).Data.Should().NotContain("FAMILY-BUS");
    }

    [Fact]
    public async Task Tenant_switch_and_unlinked_guardian_do_not_reveal_children()
    {
        await using var db = CreateContext(101);
        await SeedAsync(db);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        var otherTenant = new TransportFamilyScheduleService(db, new User(202, 42, "Guardian"), TimeProvider.System);
        (await otherTenant.GetAsync(today, 1, 20)).Data!.Items.Should().BeEmpty();
        var stranger = new TransportFamilyScheduleService(db, new User(101, 99, "Guardian"), TimeProvider.System);
        (await stranger.GetAsync(today, 1, 20)).Data!.Items.Should().BeEmpty();
        var staff = new TransportFamilyScheduleService(db, new User(101, 42, "TransportManager"), TimeProvider.System);
        (await staff.GetAsync(today, 1, 20)).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Invalid_date_and_paging_are_bounded()
    {
        await using var db = CreateContext(101);
        await SeedAsync(db);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        var service = new TransportFamilyScheduleService(db, new User(101, 42, "Guardian"), TimeProvider.System);
        (await service.GetAsync(today.AddDays(31), 1, 20)).StatusCode.Should().Be(400);
        (await service.GetAsync(today, 0, 20)).StatusCode.Should().Be(400);
        (await service.GetAsync(today, 1, 501)).StatusCode.Should().Be(400);
        (await service.ExportCsvAsync(today.AddDays(-1))).StatusCode.Should().Be(400);
    }


    [Fact]
    public async Task Parent_role_can_read_and_export_only_live_linked_child_with_tenant_isolation()
    {
        await using var db = CreateContext(101);
        var (_, _, link) = await SeedAsync(db);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        var parent = new TransportFamilyScheduleService(db, new User(101, 42, "Parent"), TimeProvider.System);
        var visible = await parent.GetAsync(today, 1, 20);
        visible.Success.Should().BeTrue(visible.Message);
        visible.Data!.Items.Should().ContainSingle(x => x.VehicleNumber == "FAMILY-BUS");
        (await parent.ExportCsvAsync(today)).Data.Should().Contain("FAMILY-BUS").And.NotContain("1750");

        var unrelated = new TransportFamilyScheduleService(db, new User(101, 99, "Parent"), TimeProvider.System);
        (await unrelated.GetAsync(today, 1, 20)).Data!.Items.Should().BeEmpty();
        (await unrelated.ExportCsvAsync(today)).Data.Should().NotContain("FAMILY-BUS");

        var switchedTenant = new TransportFamilyScheduleService(db, new User(202, 42, "Parent"), TimeProvider.System);
        (await switchedTenant.GetAsync(today, 1, 20)).Data!.Items.Should().BeEmpty();
        (await switchedTenant.ExportCsvAsync(today)).Data.Should().NotContain("FAMILY-BUS");

        link.IsDeleted = true;
        await db.SaveChangesAsync();
        (await parent.GetAsync(today, 1, 20)).Data!.Items.Should().BeEmpty();
        (await parent.ExportCsvAsync(today)).Data.Should().NotContain("FAMILY-BUS");
    }

    [Fact]
    public void Parent_role_is_accepted_by_both_family_routes_without_opening_staff_access()
    {
        var apiType = typeof(EduOS.App.Controllers.Api.TransportFamilyScheduleController);
        var apiRole = apiType.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single(x => !string.IsNullOrWhiteSpace(x.Roles));
        apiRole.Roles.Should().Be("Student,Guardian,Parent");

        var familyAction = typeof(EduOS.App.Controllers.TransportOperationsController)
            .GetMethod(nameof(EduOS.App.Controllers.TransportOperationsController.Family))!;
        var pageRole = familyAction.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single();
        pageRole.Roles.Should().Be("Student,Guardian,Parent");

        var editAction = typeof(EduOS.App.Controllers.TransportOperationsController)
            .GetMethod(nameof(EduOS.App.Controllers.TransportOperationsController.EditStops))!;
        var staffRole = editAction.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single();
        staffRole.Roles.Should().NotContain("Parent");
    }

    private static EduOSDbContext CreateContext(long tenant)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("transport-family-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "42"),
                new Claim(ClaimTypes.Role, "Guardian"),
                new Claim("TenantId", tenant.ToString())
            ], "TestAuthentication"))
        };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static async Task<(Student Student, Guardian Guardian, StudentGuardian Link)> SeedAsync(EduOSDbContext db)
    {
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        var campus = new Campus { TenantId = 101, Name = "Family campus", Code = "FC", IsActive = true };
        var student = new Student { TenantId = 101, PersonId = 1, UserId = 21,
            FullName = "Child", StudentCode = "CH-1", AdmissionDate = today, StatusCode = "Active" };
        var guardian = new Guardian { TenantId = 101, PersonId = 2, UserId = 42,
            FullName = "Guardian", IsActive = true };
        var route = new TransportRoute { TenantId = 101, CampusId = 1, Name = "Family route", Code = "FR" };
        var vehicle = new Vehicle { TenantId = 101, CampusId = 1, VehicleNumber = "FAMILY-BUS", Capacity = 20 };
        db.AddRange(campus, student, guardian);
        await db.SaveChangesAsync();
        route.CampusId = campus.Id; vehicle.CampusId = campus.Id;
        db.AddRange(route, vehicle);
        await db.SaveChangesAsync();
        var pickup = new RouteStop { TenantId = 101, RouteId = route.Id, Name = "Main gate", SequenceNo = 1 };
        var enrollment = new StudentEnrollment { TenantId = 101, StudentId = student.Id,
            CampusId = campus.Id, AcademicYearId = 1, AcademicProgramId = 1,
            AcademicLevelId = 1, AcademicBatchId = 1, AcademicCurriculumId = 1,
            ClientRequestId = Guid.NewGuid(), RollNo = "1", IsCurrent = true,
            State = EnrollmentState.Active, EnrollmentDate = today };
        var link = new StudentGuardian { TenantId = 101, StudentId = student.Id,
            GuardianId = guardian.Id, RelationCode = "Parent", IsAuthorizedPickup = true };
        db.AddRange(pickup, enrollment, link);
        await db.SaveChangesAsync();
        db.StudentTransports.Add(new StudentTransport { TenantId = 101,
            ClientRequestId = Guid.NewGuid(), StudentId = student.Id,
            StudentEnrollmentId = enrollment.Id, RouteId = route.Id, VehicleId = vehicle.Id,
            PickupStopId = pickup.Id, StartDate = today.AddDays(-1), MonthlyFare = 1750m,
            State = TransportAssignmentState.Active });
        await db.SaveChangesAsync();
        return (student, guardian, link);
    }

    private sealed class User(long tenant, long userId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
