using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Students;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class GuardianContactProfileWorkflowTests
{
    [Fact]
    public async Task Admin_can_read_update_and_replay_once()
    {
        await using var db = Context(101);
        var (student, guardian) = await SeedAsync(db, 101);
        var service = Service(db, 101);
        var request = Valid();
        (await service.UpdateAsync(student.PublicId, guardian.PublicId, request)).Success.Should().BeTrue();
        (await service.UpdateAsync(student.PublicId, guardian.PublicId, request)).Success.Should().BeTrue();
        var result = await service.GetAsync(student.PublicId, guardian.PublicId);
        result.Data!.Email.Should().Be("guardian@example.test");
        result.Data.Occupation.Should().Be("Teacher");
        (await db.AuditLogs.CountAsync(x => x.Action == "Guardian.ContactUpdated")).Should().Be(1);
    }

    [Fact]
    public async Task Changed_payload_with_same_request_id_conflicts()
    {
        await using var db = Context(101);
        var (student, guardian) = await SeedAsync(db, 101);
        var request = Valid();
        (await Service(db, 101).UpdateAsync(student.PublicId, guardian.PublicId, request)).Success.Should().BeTrue();
        request.Address = "Other address";
        (await Service(db, 101).UpdateAsync(student.PublicId, guardian.PublicId, request)).StatusCode.Should().Be(409);
        (await db.Guardians.SingleAsync()).Address.Should().Be("Guardian home");
    }

    [Fact]
    public async Task Invalid_and_unprivileged_requests_are_rejected()
    {
        await using var db = Context(101);
        var (student, guardian) = await SeedAsync(db, 101);
        var invalid = Valid(); invalid.Reason = "short";
        (await Service(db, 101).UpdateAsync(student.PublicId, guardian.PublicId, invalid)).StatusCode.Should().Be(400);
        (await Service(db, 101, false).UpdateAsync(student.PublicId, guardian.PublicId, Valid())).StatusCode.Should().Be(403);
        (await db.AuditLogs.CountAsync(x => x.Action == "Guardian.ContactUpdated")).Should().Be(0);
    }

    [Fact]
    public async Task Cross_tenant_and_unlinked_guardians_are_not_disclosed()
    {
        await using var db = Context(202);
        var (student, guardian) = await SeedAsync(db, 202);
        (await Service(db, 101).GetAsync(student.PublicId, guardian.PublicId)).StatusCode.Should().Be(404);
        (await Service(db, 101).UpdateAsync(student.PublicId, guardian.PublicId, Valid())).StatusCode.Should().Be(404);
        db.StudentGuardians.Remove(await db.StudentGuardians.SingleAsync());
        await db.SaveChangesAsync();
        (await Service(db, 202).GetAsync(student.PublicId, guardian.PublicId)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Revoked_link_and_exited_student_block_contact_updates()
    {
        await using var db = Context(101);
        var (student, guardian) = await SeedAsync(db, 101);
        var link = await db.StudentGuardians.SingleAsync();
        link.IsDeleted = true;
        await db.SaveChangesAsync();
        (await Service(db, 101).UpdateAsync(student.PublicId, guardian.PublicId, Valid())).StatusCode.Should().Be(404);
        link.IsDeleted = false; student.StatusCode = "TC";
        await db.SaveChangesAsync();
        (await Service(db, 101).UpdateAsync(student.PublicId, guardian.PublicId, Valid())).StatusCode.Should().Be(404);
    }

    [Fact]
    public void Guardian_contact_api_and_ui_require_admin_module_and_antiforgery()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "GuardianContactProfilesController.cs");
        api.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]")
            .And.Contain("[RequireModule(\"STUDENT\")]")
            .And.Contain("[AutoValidateAntiforgeryToken]");
        var view = Read("EduOS.App", "Views", "StudentGuardians", "Index.cshtml");
        view.Should().Contain("guardianContactForm").And.Contain("guardianContactSave");
        var js = Read("EduOS.App", "wwwroot", "js", "students", "guardians.js");
        js.Should().Contain("setupGuardianContact");
    }

    private static UpdateGuardianContactProfileRequestDto Valid() => new()
    {
        ClientRequestId = Guid.NewGuid(), Phone = "+8801700000000",
        Email = "guardian@example.test", Occupation = "Teacher",
        Address = "Guardian home", Reason = "Guardian requested contact correction"
    };

    private static async Task<(Student, Guardian)> SeedAsync(EduOSDbContext db, long tenant)
    {
        var student = new Student { TenantId = tenant, StudentCode = "S-" + Guid.NewGuid().ToString("N")[..10],
            FullName = "Learner", AdmissionDate = DateOnly.FromDateTime(DateTime.UtcNow), StatusCode = "Active" };
        var guardian = new Guardian { TenantId = tenant, FullName = "Guardian", IsActive = true };
        db.Students.Add(student); db.Guardians.Add(guardian);
        await db.SaveChangesAsync();
        db.StudentGuardians.Add(new StudentGuardian { TenantId = tenant, StudentId = student.Id,
            GuardianId = guardian.Id, RelationCode = "Mother", IsPrimary = true });
        await db.SaveChangesAsync();
        return (student, guardian);
    }

    private static EduOSDbContext Context(long tenant)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("guardian-contact-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "7"),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", tenant.ToString())
            }, "TestAuthentication"))
        };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static GuardianContactProfileService Service(EduOSDbContext db, long tenant, bool admin = true) =>
        new(db, new User(tenant, admin), NullLogger<GuardianContactProfileService>.Instance);

    private static string Read(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join("/", segments));
    }

    private sealed class User(long tenant, bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => "Admin";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Teacher"];
        public bool IsInRole(string role) => admin && role == "TenantAdmin";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
