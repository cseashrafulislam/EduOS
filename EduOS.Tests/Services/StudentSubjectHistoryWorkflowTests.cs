using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Academic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentSubjectHistoryWorkflowTests
{
    [Fact]
    public async Task Former_student_registrations_keep_original_enrollment_roll_and_credit_snapshot()
    {
        await using var db = Context(71);
        var studentRef = Guid.NewGuid();
        db.Students.Add(new Student
        {
            Id = 10, TenantId = 71, PublicId = studentRef, StudentCode = "HIST-10",
            FullName = "Former learner", StatusCode = "Exited",
            AdmissionDate = new DateOnly(2024, 1, 1)
        });
        db.StudentEnrollments.AddRange(
            new StudentEnrollment
            {
                Id = 20, TenantId = 71, StudentId = 10, CampusId = 4,
                PublicId = Guid.NewGuid(), AcademicBatchId = 40, RollNo = "OLD-04",
                EnrollmentDate = new DateOnly(2024, 1, 1),
                State = EnrollmentState.Promoted, IsCurrent = false
            },
            new StudentEnrollment
            {
                Id = 21, TenantId = 71, StudentId = 10, CampusId = 5,
                PublicId = Guid.NewGuid(), AcademicBatchId = 41, RollNo = "NEW-05",
                EnrollmentDate = new DateOnly(2025, 1, 1),
                State = EnrollmentState.Withdrawn, IsCurrent = false
            });
        db.StudentSubjectRegistrations.AddRange(
            new StudentSubjectRegistration
            {
                Id = 30, TenantId = 71, StudentEnrollmentId = 20,
                SubjectOfferingId = 999, CreditHoursSnapshot = 2.5m,
                RegisteredAt = new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                State = SubjectRegistrationState.Completed
            },
            new StudentSubjectRegistration
            {
                Id = 31, TenantId = 71, StudentEnrollmentId = 21,
                SubjectOfferingId = 998, CreditHoursSnapshot = 3m,
                RegisteredAt = new DateTime(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                State = SubjectRegistrationState.Approved
            });
        await db.SaveChangesAsync();
        var service = new StudentSubjectHistoryQueryService(db, new Actor(71));
        var first = await service.GetAsync(studentRef, 1, 1);
        Assert.True(first.Success, first.Message);
        Assert.Equal(2, first.Data!.TotalCount);
        Assert.Equal("NEW-05", Assert.Single(first.Data.Items).RollNo);
        Assert.Equal(5, first.Data.Items[0].CampusId);
        Assert.Equal(3m, first.Data.Items[0].CreditHoursSnapshot);
        var second = await service.GetAsync(studentRef, 2, 1);
        Assert.True(second.Success, second.Message);
        Assert.Equal("OLD-04", Assert.Single(second.Data!.Items).RollNo);
        Assert.Equal("Promoted", second.Data.Items[0].EnrollmentState);
        Assert.Equal(2.5m, second.Data.Items[0].CreditHoursSnapshot);
        Assert.Equal(string.Empty, second.Data.Items[0].SubjectName);
    }

    [Fact]
    public async Task Invalid_pages_cross_tenant_students_and_non_admin_roles_are_denied()
    {
        await using var db = Context(71);
        var reference = Guid.NewGuid();
        db.Students.Add(new Student
        {
            Id = 10, TenantId = 71, PublicId = reference, StudentCode = "HIST-10",
            FullName = "Learner", AdmissionDate = new DateOnly(2024, 1, 1)
        });
        await db.SaveChangesAsync();
        var service = new StudentSubjectHistoryQueryService(db, new Actor(71));
        Assert.Equal(400, (await service.GetAsync(Guid.Empty)).StatusCode);
        Assert.Equal(400, (await service.GetAsync(reference, 0)).StatusCode);
        Assert.Equal(400, (await service.GetAsync(reference, 1, 101)).StatusCode);
        Assert.Equal(400, (await service.GetAsync(reference, int.MaxValue, 100)).StatusCode);
        Assert.Equal(404, (await new StudentSubjectHistoryQueryService(
            db, new Actor(72)).GetAsync(reference)).StatusCode);
        Assert.Equal(403, (await new StudentSubjectHistoryQueryService(
            db, new Actor(71, false)).GetAsync(reference)).StatusCode);
    }

    [Fact]
    public void Endpoint_requires_admin_academic_module_and_antiforgery()
    {
        var type = typeof(StudentSubjectHistoryController);
        Assert.Contains(type.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), x => x.Roles == "TenantAdmin");
        Assert.Single(type.GetCustomAttributes(typeof(RequireModuleAttribute), true));
        Assert.Single(type.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
    }

    private static EduOSDbContext Context(long tenant)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("student-subject-history-" + Guid.NewGuid().ToString("N")).Options;
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

    private sealed class Actor(long tenant, bool admin = true) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => admin;
        public IReadOnlyList<string> Roles => admin ? new[] { "TenantAdmin" } : new[] { "Teacher" };
        public bool IsInRole(string role) => Roles.Contains(role);
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
