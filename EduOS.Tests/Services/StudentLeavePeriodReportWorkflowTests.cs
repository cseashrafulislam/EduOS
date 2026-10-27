using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using EduOS.Core.DTOs.Attendance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Attendance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentLeavePeriodReportWorkflowTests
{
    [Fact]
    public async Task Report_uses_historical_enrollment_clips_days_and_filters_state()
    {
        await using var db = Context(71);
        db.AcademicBatches.Add(new AcademicBatch {
            Id = 10, TenantId = 71, CampusId = 8, Name = "Nine", Code = "N9" });
        db.Students.Add(new Student {
            Id = 20, TenantId = 71, StudentCode = "ST20", FullName = "Learner",
            AdmissionDate = new DateOnly(2025, 1, 1) });
        db.StudentEnrollments.Add(new StudentEnrollment {
            Id = 30, TenantId = 71, StudentId = 20, AcademicBatchId = 10,
            CampusId = 8, RollNo = "HIST-09", IsCurrent = false });
        db.StudentLeaveApplications.AddRange(
            new StudentLeaveApplication {
                Id = 40, TenantId = 71, StudentEnrollmentId = 30,
                FromDate = new DateOnly(2026, 9, 28), ToDate = new DateOnly(2026, 10, 4),
                Reason = "Medical", State = LeaveState.Approved },
            new StudentLeaveApplication {
                Id = 41, TenantId = 71, StudentEnrollmentId = 30,
                FromDate = new DateOnly(2026, 10, 6), ToDate = new DateOnly(2026, 10, 8),
                Reason = "Family", State = LeaveState.Submitted });
        await db.SaveChangesAsync();

        var report = await Service(db, 71).GetAsync(Query());
        Assert.True(report.Success, report.Message);
        Assert.Equal(2, report.Data!.TotalCount);
        var first = report.Data.Items[0];
        Assert.Equal("HIST-09", first.RollNo);
        Assert.Equal(4, first.PeriodDays);
        Assert.Equal(3, report.Data.Items[1].PeriodDays);

        var approvedOnly = Query();
        approvedOnly.State = LeaveState.Approved;
        var filtered = await Service(db, 71).GetAsync(approvedOnly);
        Assert.True(filtered.Success, filtered.Message);
        Assert.Equal(1, filtered.Data!.TotalCount);
        Assert.Equal(40, Assert.Single(filtered.Data.Items).ApplicationId);

        var pageTwo = Query();
        pageTwo.Page = 2; pageTwo.PageSize = 1;
        var paged = await Service(db, 71).GetAsync(pageTwo);
        Assert.True(paged.Success, paged.Message);
        Assert.Equal(41, Assert.Single(paged.Data!.Items).ApplicationId);
    }

    [Fact]
    public async Task Invalid_filters_and_non_manager_access_are_rejected()
    {
        await using var db = Context(71);
        var invalid = Query();
        invalid.ToDate = new DateOnly(2027, 12, 1);
        Assert.Equal(400, (await Service(db, 71).GetAsync(invalid)).StatusCode);
        invalid = Query(); invalid.PageSize = 101;
        Assert.Equal(400, (await Service(db, 71).GetAsync(invalid)).StatusCode);
        invalid = Query(); invalid.State = (LeaveState)999;
        Assert.Equal(400, (await Service(db, 71).GetAsync(invalid)).StatusCode);
        Assert.Equal(403, (await Service(db, 71, false).GetAsync(Query())).StatusCode);
    }

    [Fact]
    public async Task Cross_tenant_batch_and_wrong_campus_enrollment_are_not_disclosed()
    {
        await using var db = Context(71);
        db.AcademicBatches.Add(new AcademicBatch {
            Id = 10, TenantId = 71, CampusId = 8, Name = "Nine", Code = "N9" });
        db.Students.Add(new Student {
            Id = 20, TenantId = 71, StudentCode = "ST20", FullName = "Learner",
            AdmissionDate = new DateOnly(2025, 1, 1) });
        db.StudentEnrollments.Add(new StudentEnrollment {
            Id = 30, TenantId = 71, StudentId = 20, AcademicBatchId = 10,
            CampusId = 9, RollNo = "OTHER-CAMPUS" });
        db.StudentLeaveApplications.Add(new StudentLeaveApplication {
            Id = 40, TenantId = 71, StudentEnrollmentId = 30,
            FromDate = new DateOnly(2026, 10, 3), ToDate = new DateOnly(2026, 10, 4),
            Reason = "Family", State = LeaveState.Approved });
        await db.SaveChangesAsync();
        Assert.Equal(404, (await Service(db, 72).GetAsync(Query())).StatusCode);
        var own = await Service(db, 71).GetAsync(Query());
        Assert.True(own.Success, own.Message);
        Assert.Empty(own.Data!.Items);
    }

    [Fact]
    public void Report_api_requires_manager_role_module_and_antiforgery()
    {
        var type = typeof(StudentLeavePeriodReportsController);
        Assert.Contains(type.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(),
            attribute => attribute.Roles == "TenantAdmin,Principal,VicePrincipal");
        Assert.Single(type.GetCustomAttributes(typeof(RequireModuleAttribute), true));
        Assert.Single(type.GetCustomAttributes(typeof(AutoValidateAntiforgeryTokenAttribute), true));
    }


    [Fact]
    public async Task Principal_report_requires_live_campus_grant_and_revocation_is_immediate()
    {
        await using var db = Context(71);
        db.AcademicBatches.Add(new AcademicBatch { Id = 10, TenantId = 71, CampusId = 8,
            Name = "Batch", Code = "B" });
        db.Campuses.AddRange(
            new Campus { Id = 8, TenantId = 71, Name = "North", Code = "N", IsActive = true },
            new Campus { Id = 9, TenantId = 71, Name = "South", Code = "S", IsActive = true });
        db.UserCampusAccesses.Add(new UserCampusAccess { Id = 101, TenantId = 71,
            UserId = 7, CampusId = 9, IsActive = true });
        await db.SaveChangesAsync();
        var principal = Service(db, 71, manager: false, principal: true);
        Assert.Equal(404, (await principal.GetAsync(Query())).StatusCode);
        var grant = new UserCampusAccess { Id = 102, TenantId = 71,
            UserId = 7, CampusId = 8, IsActive = true };
        db.UserCampusAccesses.Add(grant);
        await db.SaveChangesAsync();
        Assert.True((await principal.GetAsync(Query())).Success);
        grant.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await principal.GetAsync(Query())).StatusCode);
    }

    [Fact]
    public async Task Principal_report_rejects_inactive_campus_even_with_valid_grant()
    {
        await using var db = Context(71);
        db.AcademicBatches.Add(new AcademicBatch { Id = 10, TenantId = 71, CampusId = 8,
            Name = "Batch", Code = "B" });
        db.Campuses.Add(new Campus { Id = 8, TenantId = 71, Name = "North", Code = "N", IsActive = false });
        db.UserCampusAccesses.Add(new UserCampusAccess { Id = 101, TenantId = 71,
            UserId = 7, CampusId = 8, IsActive = true });
        await db.SaveChangesAsync();
        Assert.Equal(404, (await Service(db, 71, manager: false, principal: true)
            .GetAsync(Query())).StatusCode);
    }

    private static StudentLeavePeriodReportQueryDto Query() => new() {
        AcademicBatchId = 10, FromDate = new DateOnly(2026, 10, 1),
        ToDate = new DateOnly(2026, 10, 10)
    };

    private static StudentLeavePeriodReportService Service(EduOSDbContext db, long tenant, bool manager = true, bool principal = false) =>
        new(db, new Actor(tenant, manager, principal), NullLogger<StudentLeavePeriodReportService>.Instance);

    private static EduOSDbContext Context(long tenant)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("student-leave-period-report-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "7"),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", tenant.ToString())
            }, "TestAuthentication"))
        };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed class Actor(long tenant, bool manager, bool principal) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => manager;
        public IReadOnlyList<string> Roles => manager ? new[] { "TenantAdmin" } :
            principal ? new[] { "Principal" } : new[] { "Teacher" };
        public bool IsInRole(string role) => Roles.Contains(role);
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
