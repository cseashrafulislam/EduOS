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

public sealed class StudentEnrollmentHistoryWorkflowTests
{
    [Fact]
    public async Task Exited_student_history_uses_original_roll_campus_and_order_with_paging()
    {
        await using var db = Context(71);
        var reference = Guid.NewGuid();
        db.Students.Add(new Student
        {
            Id = 20, TenantId = 71, PublicId = reference, StudentCode = "S20",
            FullName = "Former learner", StatusCode = "Exited",
            AdmissionDate = new DateOnly(2024, 1, 1)
        });
        db.AcademicBatches.AddRange(
            new AcademicBatch { Id = 10, TenantId = 71, CampusId = 8, Name = "Old Section", Code = "A" },
            new AcademicBatch { Id = 11, TenantId = 71, CampusId = 9, Name = "New Section", Code = "B" });
        db.StudentEnrollments.AddRange(
            new StudentEnrollment
            {
                Id = 30, TenantId = 71, StudentId = 20, CampusId = 8,
                AcademicBatchId = 10, RollNo = "OLD-07", IsCurrent = false,
                EnrollmentDate = new DateOnly(2025, 1, 1),
                State = EnrollmentState.Promoted
            },
            new StudentEnrollment
            {
                Id = 31, TenantId = 71, StudentId = 20, CampusId = 9,
                AcademicBatchId = 11, RollNo = "NEW-19", IsCurrent = false,
                EnrollmentDate = new DateOnly(2026, 1, 1),
                State = EnrollmentState.Withdrawn
            });
        await db.SaveChangesAsync();

        var service = new StudentEnrollmentHistoryQueryService(db, new Actor(71));
        var first = await service.GetAsync(reference, 1, 1);
        Assert.True(first.Success, first.Message);
        Assert.Equal(2, first.Data!.TotalCount);
        Assert.Equal("NEW-19", Assert.Single(first.Data.Items).RollNo);
        Assert.Equal(9, first.Data.Items[0].CampusId);
        Assert.Equal("Withdrawn", first.Data.Items[0].State);
        var second = await service.GetAsync(reference, 2, 1);
        Assert.True(second.Success, second.Message);
        Assert.Equal("OLD-07", Assert.Single(second.Data!.Items).RollNo);
        Assert.Equal("Old Section", second.Data.Items[0].BatchName);
        Assert.False(second.Data.Items[0].IsCurrent);
    }

    [Fact]
    public async Task Invalid_requests_and_unprivileged_actor_are_rejected()
    {
        await using var db = Context(71);
        var service = new StudentEnrollmentHistoryQueryService(db, new Actor(71));
        Assert.Equal(400, (await service.GetAsync(Guid.Empty)).StatusCode);
        Assert.Equal(400, (await service.GetAsync(Guid.NewGuid(), 0)).StatusCode);
        Assert.Equal(400, (await service.GetAsync(Guid.NewGuid(), 1, 101)).StatusCode);
        Assert.Equal(400, (await service.GetAsync(Guid.NewGuid(), int.MaxValue, 100)).StatusCode);
        Assert.Equal(403, (await new StudentEnrollmentHistoryQueryService(
            db, new Actor(71, false)).GetAsync(Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Cross_tenant_student_and_mismatched_campus_catalog_are_not_exposed()
    {
        await using var db = Context(71);
        var reference = Guid.NewGuid();
        db.Students.Add(new Student
        {
            Id = 20, TenantId = 71, PublicId = reference,
            StudentCode = "S20", FullName = "Learner",
            AdmissionDate = new DateOnly(2024, 1, 1)
        });
        db.AcademicBatches.Add(new AcademicBatch
        {
            Id = 10, TenantId = 71, CampusId = 999, Name = "Wrong campus", Code = "X"
        });
        db.StudentEnrollments.Add(new StudentEnrollment
        {
            Id = 30, TenantId = 71, StudentId = 20, CampusId = 8,
            AcademicBatchId = 10, RollNo = "HIST-08",
            EnrollmentDate = new DateOnly(2025, 1, 1)
        });
        await db.SaveChangesAsync();
        Assert.Equal(404, (await new StudentEnrollmentHistoryQueryService(
            db, new Actor(72)).GetAsync(reference)).StatusCode);
        var own = await new StudentEnrollmentHistoryQueryService(
            db, new Actor(71)).GetAsync(reference);
        Assert.True(own.Success, own.Message);
        Assert.Equal("HIST-08", Assert.Single(own.Data!.Items).RollNo);
        Assert.Equal(string.Empty, own.Data.Items[0].BatchName);
    }

    [Fact]
    public void Api_is_admin_only_and_module_antiforgery_protected()
    {
        var type = typeof(StudentEnrollmentHistoryController);
        Assert.Contains(type.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), x => x.Roles == "TenantAdmin");
        Assert.Single(type.GetCustomAttributes(typeof(RequireModuleAttribute), true));
        Assert.Single(type.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
    }

    private static EduOSDbContext Context(long tenant)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("student-enrollment-history-" + Guid.NewGuid().ToString("N")).Options;
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
