using EduOS.App.Controllers.Api;
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

public sealed class StudentContactProfileWorkflowTests
{
    [Fact]
    public async Task Admin_can_update_read_and_replay_without_duplicate_audit()
    {
        await using var db = Context(101);
        var student = await SeedAsync(db, 101);
        var service = Service(db, 101);
        var request = Valid();
        (await service.UpdateAsync(student.PublicId, request)).Success.Should().BeTrue();
        (await service.UpdateAsync(student.PublicId, request)).Success.Should().BeTrue();
        var result = await service.GetAsync(student.PublicId);
        result.Data!.Email.Should().Be("family@example.test");
        (await db.AuditLogs.CountAsync(x => x.Action == "Student.ContactUpdated")).Should().Be(1);
    }

    [Fact]
    public async Task Duplicate_request_with_changed_payload_is_conflict()
    {
        await using var db = Context(101);
        var student = await SeedAsync(db, 101);
        var service = Service(db, 101);
        var request = Valid();
        (await service.UpdateAsync(student.PublicId, request)).Success.Should().BeTrue();
        request.Address = "Other address";
        (await service.UpdateAsync(student.PublicId, request)).StatusCode.Should().Be(409);
        (await db.Students.SingleAsync()).Address.Should().Be("Home address");
    }

    [Fact]
    public async Task Invalid_request_and_unprivileged_role_cannot_write()
    {
        await using var db = Context(101);
        var student = await SeedAsync(db, 101);
        var invalid = Valid();
        invalid.Reason = "short";
        (await Service(db, 101).UpdateAsync(student.PublicId, invalid)).StatusCode.Should().Be(400);
        (await Service(db, 101, false).UpdateAsync(student.PublicId, Valid())).StatusCode.Should().Be(403);
        (await db.AuditLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Cross_tenant_student_is_not_disclosed()
    {
        await using var db = Context(202);
        var student = await SeedAsync(db, 202);
        (await Service(db, 101).GetAsync(student.PublicId)).StatusCode.Should().Be(404);
        (await Service(db, 101).UpdateAsync(student.PublicId, Valid())).StatusCode.Should().Be(404);
        (await db.AuditLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Exited_student_contact_history_is_immutable()
    {
        await using var db = Context(101);
        var student = await SeedAsync(db, 101);
        student.StatusCode = "TC";
        await db.SaveChangesAsync();
        (await Service(db, 101).UpdateAsync(student.PublicId, Valid())).StatusCode.Should().Be(404);
    }

    [Fact]
    public void Contact_api_and_workspace_are_authorized_and_antiforgery_protected()
    {
        var source = Read("EduOS.App", "Controllers", "Api", "StudentContactProfilesController.cs");
        source.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        source.Should().Contain("[RequireModule(\"STUDENT\")]");
        source.Should().Contain("[AutoValidateAntiforgeryToken]");
        var view = Read("EduOS.App", "Views", "Students", "Index.cshtml");
        view.Should().Contain("studentContactForm").And.Contain("contact-profile.js");
    }

    private static UpdateStudentContactProfileRequestDto Valid() => new()
    {
        ClientRequestId = Guid.NewGuid(), Phone = "+8801700000000",
        Email = "family@example.test", Address = "Home address",
        PreferredLanguage = "bn-BD", Reason = "Guardian requested contact correction"
    };

    private static async Task<Student> SeedAsync(EduOSDbContext db, long tenant)
    {
        var student = new Student
        {
            TenantId = tenant, StudentCode = "S-" + Guid.NewGuid().ToString("N")[..10],
            FullName = "Student", AdmissionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StatusCode = "Active"
        };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        return student;
    }

    private static EduOSDbContext Context(long tenant)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("student-contact-" + Guid.NewGuid().ToString("N")).Options;
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

    private static StudentContactProfileService Service(EduOSDbContext db, long tenant, bool admin = true) =>
        new(db, new User(tenant, admin), NullLogger<StudentContactProfileService>.Instance);

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
