using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Hostel;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Hostel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class GuardianHostelStayIsolationTests
{
    [Fact]
    public async Task Only_current_pickup_authorized_links_can_read_scheduled_stays()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("guardian-hostel-" + Guid.NewGuid().ToString("N")).Options;
        await using var db = Context(options, 101);
        var campus = new Campus { TenantId = 101, Name = "Home campus", Code = "HOME" };
        var guardian = new Guardian { TenantId = 101, UserId = 41, PersonId = 1,
            FullName = "Guardian", IsActive = true };
        var foreignGuardian = new Guardian { TenantId = 101, UserId = 42, PersonId = 2,
            FullName = "Different guardian", IsActive = true };
        var hostel = new Hostel { TenantId = 101, CampusId = 1, Name = "Main hostel", Code = "H" };
        db.AddRange(campus, guardian, foreignGuardian);
        await db.SaveChangesAsync();
        hostel.CampusId = campus.Id;
        db.Add(hostel);
        var allowed = Student("SAFE");
        var denied = Student("PRIVATE");
        var noPickup = Student("NO-PICKUP");
        db.AddRange(allowed, denied, noPickup);
        await db.SaveChangesAsync();
        db.AddRange(
            new StudentGuardian { TenantId = 101, StudentId = allowed.Id,
                GuardianId = guardian.Id, RelationCode = "Parent", IsAuthorizedPickup = true },
            new StudentGuardian { TenantId = 101, StudentId = denied.Id,
                GuardianId = foreignGuardian.Id, RelationCode = "Parent", IsAuthorizedPickup = true },
            new StudentGuardian { TenantId = 101, StudentId = noPickup.Id,
                GuardianId = guardian.Id, RelationCode = "Parent", IsAuthorizedPickup = false });
        await db.SaveChangesAsync();
        var room = new HostelRoom { TenantId = 101, HostelId = hostel.Id,
            RoomNumber = "101", Capacity = 3, IsActive = true };
        db.Add(room);
        await db.SaveChangesAsync();
        var students = new[] { allowed, denied, noPickup };
        foreach (var (student, i) in students.Select((s, i) => (s, i)))
        {
            var bed = new HostelBed { TenantId = 101, HostelRoomId = room.Id,
                BedNumber = (i + 1).ToString(), IsActive = true };
            var enrollment = new StudentEnrollment
            {
                TenantId = 101, StudentId = student.Id, CampusId = campus.Id,
                AcademicYearId = 1, AcademicProgramId = 1, AcademicLevelId = 1,
                AcademicBatchId = 1, AcademicCurriculumId = 1,
                RollNo = student.StudentCode, ClientRequestId = Guid.NewGuid(),
                IsCurrent = true, State = EnrollmentState.Active
            };
            db.AddRange(bed, enrollment);
            await db.SaveChangesAsync();
            db.Add(new StudentHostelAllocation
            {
                TenantId = 101, StudentId = student.Id, StudentEnrollmentId = enrollment.Id,
                HostelBedId = bed.Id, ClientRequestId = Guid.NewGuid(),
                StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
                State = HostelAllocationState.Active
            });
            await db.SaveChangesAsync();
        }
        var service = new GuardianHostelStayService(db, User(101, 41), TimeProvider.System,
            NullLogger<GuardianHostelStayService>.Instance);
        var response = await service.GetCurrentStaysAsync(1, 20, null);
        Assert.True(response.Success, response.Message);
        Assert.Single(response.Data!.Items);
        Assert.Equal("SAFE", response.Data.Items[0].StudentCode);
        var csv = await service.ExportCurrentPageCsvAsync(1, null);
        Assert.True(csv.Success, csv.Message);
        Assert.Contains("SAFE", csv.Data);
        Assert.DoesNotContain("PRIVATE", csv.Data);
        Assert.DoesNotContain("NO-PICKUP", csv.Data);
        Assert.Equal(400, (await service.GetCurrentStaysAsync(0, 20, null)).StatusCode);
        Assert.Equal(400, (await service.GetCurrentStaysAsync(1, 21, null)).StatusCode);
        Assert.Equal(400, (await service.GetCurrentStaysAsync(1, 20, new string('x', 101))).StatusCode);
        Assert.Equal(403, (await new GuardianHostelStayService(db, User(101, 41, false),
            TimeProvider.System, NullLogger<GuardianHostelStayService>.Instance)
            .GetCurrentStaysAsync(1, 20, null)).StatusCode);
        // Formula-prefixed child names must remain inert even after leading whitespace.
        allowed.FullName = " \t=1+1";
        await db.SaveChangesAsync();
        Assert.Contains("\"'  =1+1\"", (await service.ExportCurrentPageCsvAsync(1, null)).Data);
        // Revoking pickup permission must immediately revoke read and export.
        var link = await db.StudentGuardians.SingleAsync(x => x.StudentId == allowed.Id);
        link.IsAuthorizedPickup = false;
        await db.SaveChangesAsync();
        Assert.Empty((await service.GetCurrentStaysAsync(1, 20, null)).Data!.Items);
        Assert.DoesNotContain("SAFE", (await service.ExportCurrentPageCsvAsync(1, null)).Data);
    }

    [Fact]
    public async Task Same_user_id_in_another_tenant_does_not_see_original_tenant_stays()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("guardian-hostel-tenants-" + Guid.NewGuid().ToString("N")).Options;
        await using (var original = Context(options, 101))
        {
            original.Guardians.Add(new Guardian { TenantId = 101, UserId = 41, PersonId = 1,
                FullName = "Original", IsActive = true });
            await original.SaveChangesAsync();
        }
        await using var other = Context(options, 202);
        var service = new GuardianHostelStayService(other, User(202, 41), TimeProvider.System,
            NullLogger<GuardianHostelStayService>.Instance);
        var result = await service.GetCurrentStaysAsync(1, 20, null);
        Assert.True(result.Success, result.Message);
        Assert.Empty(result.Data!.Items);
    }

    private static Student Student(string code) => new()
    {
        TenantId = 101, PersonId = 1, StudentCode = code,
        FullName = code, StatusCode = "Active",
        AdmissionDate = DateOnly.FromDateTime(DateTime.Today)
    };

    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "41"),
                new Claim(ClaimTypes.Role, "Guardian"),
                new Claim("TenantId", tenant.ToString())
            ], "TestAuthentication"))
        };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static ICurrentUserService User(long tenant, long id, bool guardian = true) =>
        new GuardianUser(tenant, id, guardian);

    private sealed class GuardianUser(long tenant, long id, bool guardian) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => id;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => guardian ? ["Guardian"] : ["Student"];
        public bool IsInRole(string role) => guardian && role == "Guardian";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
