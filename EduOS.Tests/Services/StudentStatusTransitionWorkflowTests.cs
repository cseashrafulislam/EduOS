using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
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

public sealed class StudentStatusTransitionWorkflowTests
{
    [Fact]
    public async Task Admin_can_suspend_and_replay_once_without_duplicate_history()
    {
        await using var db = Context(101);
        var student = await SeedAsync(db, 101);
        var service = Service(db, 101);
        var request = Valid("Suspended");
        (await service.ChangeAsync(student.PublicId, request)).Success.Should().BeTrue();
        (await service.ChangeAsync(student.PublicId, request)).Success.Should().BeTrue();
        (await service.GetAsync(student.PublicId)).Data!.Status.Should().Be("Suspended");
        (await db.StudentStatusHistories.CountAsync()).Should().Be(1);
        (await db.AuditLogs.CountAsync(x => x.Action == "Student.StatusChanged")).Should().Be(1);
    }

    [Fact]
    public async Task Duplicate_request_with_new_target_or_reason_is_rejected()
    {
        await using var db = Context(101);
        var student = await SeedAsync(db, 101);
        var service = Service(db, 101);
        var request = Valid("Suspended");
        (await service.ChangeAsync(student.PublicId, request)).Success.Should().BeTrue();
        request.Reason = "Different administrator reason";
        (await service.ChangeAsync(student.PublicId, request)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Invalid_role_and_cross_tenant_are_denied_without_audit()
    {
        await using var db = Context(202);
        var student = await SeedAsync(db, 202);
        (await Service(db, 202, false).ChangeAsync(student.PublicId, Valid("Suspended"))).StatusCode.Should().Be(403);
        (await Service(db, 101).ChangeAsync(student.PublicId, Valid("Suspended"))).StatusCode.Should().Be(404);
        (await db.AuditLogs.CountAsync(x => x.Action == "Student.StatusChanged")).Should().Be(0);
    }

    [Fact]
    public async Task Invalid_transition_and_missing_current_enrollment_are_blocked()
    {
        await using var db = Context(101);
        var student = await SeedAsync(db, 101);
        var service = Service(db, 101);
        (await service.ChangeAsync(student.PublicId, Valid("TC"))).StatusCode.Should().Be(400);
        student.StatusCode = "TC";
        await db.SaveChangesAsync();
        (await service.ChangeAsync(student.PublicId, Valid("Suspended"))).StatusCode.Should().Be(409);
        student.StatusCode = "Active";
        var enrollment = await db.StudentEnrollments.SingleAsync();
        enrollment.IsCurrent = false;
        await db.SaveChangesAsync();
        (await service.ChangeAsync(student.PublicId, Valid("Suspended"))).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Stale_rowversion_is_conflict()
    {
        await using var db = Context(101);
        var student = await SeedAsync(db, 101);
        student.RowVersion = new byte[] { 1, 2, 3 };
        await db.SaveChangesAsync();
        var request = Valid("Suspended");
        request.RowVersion = Convert.ToBase64String(new byte[] { 9, 9, 9 });
        (await Service(db, 101).ChangeAsync(student.PublicId, request)).StatusCode.Should().Be(409);
        (await db.StudentStatusHistories.CountAsync()).Should().Be(0);
    }

    [Fact]
    public void Api_and_student_workspace_require_admin_and_antiforgery()
    {
        var controller = Read("EduOS.App", "Controllers", "Api", "StudentStatusTransitionsController.cs");
        controller.Should().Contain("[Authorize(Roles = \"TenantAdmin\")]");
        controller.Should().Contain("[RequireModule(\"STUDENT\")]");
        controller.Should().Contain("[AutoValidateAntiforgeryToken]");
        var view = Read("EduOS.App", "Views", "Students", "Index.cshtml");
        view.Should().Contain("studentOperationalStatusForm").And.Contain("operational-status.js");
    }

    private static ChangeStudentOperationalStatusRequestDto Valid(string target) => new()
    {
        ClientRequestId = Guid.NewGuid(), TargetStatus = target,
        Reason = "Temporary suspension approved by principal"
    };

    private static async Task<Student> SeedAsync(EduOSDbContext db, long tenant)
    {
        var student = new Student
        {
            TenantId = tenant, StudentCode = "SS-" + Guid.NewGuid().ToString("N")[..10],
            FullName = "Student", AdmissionDate = new DateOnly(2026, 1, 1), StatusCode = "Active"
        };
        var campus = new Campus { TenantId = tenant, Code = "MAIN", Name = "Main Campus" };
        db.AddRange(student, campus);
        await db.SaveChangesAsync();
        db.StudentEnrollments.Add(new StudentEnrollment
        {
            TenantId = tenant, StudentId = student.Id, CampusId = campus.Id,
            PublicId = Guid.NewGuid(), IsCurrent = true, State = EnrollmentState.Active,
            EnrollmentDate = new DateOnly(2026, 1, 1), RollNo = "01"
        });
        await db.SaveChangesAsync();
        return student;
    }

    private static EduOSDbContext Context(long tenant)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("student-status-" + Guid.NewGuid().ToString("N")).Options;
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

    private static StudentStatusTransitionService Service(EduOSDbContext db, long tenant, bool admin = true) =>
        new(db, new User(tenant, admin), NullLogger<StudentStatusTransitionService>.Instance);

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
