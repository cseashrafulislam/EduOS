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

public sealed class StudentAttendanceReportWorkflowTests
{
    [Fact]
    public async Task Finalized_marks_use_historical_enrollment_and_ignore_draft_sessions()
    {
        await using var db = Context(71);
        db.AcademicBatches.Add(new AcademicBatch { Id = 10, TenantId = 71, CampusId = 8, Name = "Nine", Code = "N9" });
        db.Students.Add(new Student { Id = 20, TenantId = 71, StudentCode = "ST20", FullName = "Learner",
            AdmissionDate = new DateOnly(2025, 1, 1) });
        db.StudentEnrollments.Add(new StudentEnrollment { Id = 30, TenantId = 71, StudentId = 20,
            AcademicBatchId = 10, CampusId = 8, RollNo = "OLD-09", IsCurrent = false });
        db.AttendanceSessions.AddRange(
            new AttendanceSession { Id = 40, TenantId = 71, AcademicBatchId = 10,
                AttendanceDate = new DateOnly(2026, 10, 5), IsFinalized = true },
            new AttendanceSession { Id = 41, TenantId = 71, AcademicBatchId = 10,
                AttendanceDate = new DateOnly(2026, 10, 6), IsFinalized = false });
        db.StudentAttendances.AddRange(
            new StudentAttendance { Id = 50, TenantId = 71, AttendanceSessionId = 40,
                StudentEnrollmentId = 30, State = AttendanceState.Present },
            new StudentAttendance { Id = 51, TenantId = 71, AttendanceSessionId = 41,
                StudentEnrollmentId = 30, State = AttendanceState.Absent });
        await db.SaveChangesAsync();
        var result = await Service(db, 71).GetAsync(Query());
        Assert.True(result.Success, result.Message);
        var row = Assert.Single(result.Data!.Items);
        Assert.Equal("OLD-09", row.RollNo);
        Assert.Equal(1, row.MarkedSessions);
        Assert.Equal(1, row.Present);
        Assert.Equal(100m, row.AttendancePercent);
    }

    [Fact]
    public async Task Subject_scoped_report_counts_only_its_finalized_marks_and_rejects_foreign_offering_ids()
    {
        await using var db = Context(71);
        db.AcademicBatches.Add(new AcademicBatch { Id = 10, TenantId = 71, CampusId = 8,
            Name = "Cohort", Code = "C" });
        db.SubjectOfferings.AddRange(
            new SubjectOffering { Id = 61, TenantId = 71, AcademicBatchId = 10, Code = "M" },
            new SubjectOffering { Id = 62, TenantId = 71, AcademicBatchId = 10, Code = "S" },
            new SubjectOffering { Id = 63, TenantId = 71, AcademicBatchId = 11, Code = "O" });
        db.Students.Add(new Student { Id = 20, TenantId = 71, StudentCode = "ST20",
            FullName = "Learner", AdmissionDate = new DateOnly(2025, 1, 1) });
        db.StudentEnrollments.Add(new StudentEnrollment { Id = 30, TenantId = 71,
            StudentId = 20, AcademicBatchId = 10, CampusId = 8, RollNo = "R1" });
        db.AttendanceSessions.AddRange(
            new AttendanceSession { Id = 40, TenantId = 71, AcademicBatchId = 10,
                SubjectOfferingId = 61, AttendanceDate = new DateOnly(2026, 10, 5), IsFinalized = true },
            new AttendanceSession { Id = 41, TenantId = 71, AcademicBatchId = 10,
                SubjectOfferingId = 62, AttendanceDate = new DateOnly(2026, 10, 5), IsFinalized = true },
            new AttendanceSession { Id = 42, TenantId = 71, AcademicBatchId = 10,
                AttendanceDate = new DateOnly(2026, 10, 5), IsFinalized = true });
        db.StudentAttendances.AddRange(
            new StudentAttendance { Id = 50, TenantId = 71, AttendanceSessionId = 40,
                StudentEnrollmentId = 30, State = AttendanceState.Present },
            new StudentAttendance { Id = 51, TenantId = 71, AttendanceSessionId = 41,
                StudentEnrollmentId = 30, State = AttendanceState.Absent },
            new StudentAttendance { Id = 52, TenantId = 71, AttendanceSessionId = 42,
                StudentEnrollmentId = 30, State = AttendanceState.Late });
        await db.SaveChangesAsync();
        var service = Service(db, 71);
        var query = Query();
        query.SubjectOfferingId = 61;
        var response = await service.GetAsync(query);
        Assert.True(response.Success, response.Message);
        var row = Assert.Single(response.Data!.Items);
        Assert.Equal(1, row.MarkedSessions);
        Assert.Equal(1, row.Present);
        Assert.Equal(100m, row.AttendancePercent);
        query.SubjectOfferingId = 63;
        Assert.Equal(404, (await service.GetAsync(query)).StatusCode);
        query.SubjectOfferingId = 64;
        Assert.Equal(404, (await service.GetAsync(query)).StatusCode);
        query.SubjectOfferingId = null;
        Assert.Equal(3, Assert.Single((await service.GetAsync(query)).Data!.Items).MarkedSessions);
    }

    [Fact]
    public async Task Historical_subject_picker_is_paged_and_respects_live_campus_grants()
    {
        await using var db = Context(71);
        db.AcademicBatches.Add(new AcademicBatch { Id = 10, TenantId = 71, CampusId = 8,
            Name = "Cohort", Code = "C" });
        db.Campuses.Add(new Campus { Id = 8, TenantId = 71, Name = "North", Code = "N", IsActive = true });
        db.UserCampusAccesses.Add(new UserCampusAccess { Id = 101, TenantId = 71,
            UserId = 7, CampusId = 8, IsActive = true });
        db.Subjects.Add(new Subject { Id = 70, TenantId = 71, Name = "Mathematics", Code = "MATH" });
        db.CurriculumSubjects.Add(new CurriculumSubject { Id = 71, TenantId = 71, SubjectId = 70 });
        db.SubjectOfferings.AddRange(
            new SubjectOffering { Id = 61, TenantId = 71, AcademicBatchId = 10,
                CurriculumSubjectId = 71, Code = "M1", IsActive = false },
            new SubjectOffering { Id = 62, TenantId = 71, AcademicBatchId = 10,
                CurriculumSubjectId = 71, Code = "M2" });
        await db.SaveChangesAsync();
        var principal = Service(db, 71, manager: false, principal: true);
        var page = await principal.GetSubjectChoicesAsync(10, "Math", 1, 1);
        Assert.True(page.Success, page.Message);
        Assert.Equal(2, page.Data!.TotalCount);
        Assert.Single(page.Data.Items);
        var grant = await db.UserCampusAccesses.SingleAsync();
        grant.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await principal.GetSubjectChoicesAsync(10, null)).StatusCode);
    }

    [Fact]
    public async Task Invalid_range_and_unprivileged_access_are_rejected()
    {
        await using var db = Context(71);
        Assert.Equal(400, (await Service(db, 71).GetAsync(new StudentAttendanceReportQueryDto {
            AcademicBatchId = 10, FromDate = new DateOnly(2026, 10, 1),
            ToDate = new DateOnly(2026, 12, 1) })).StatusCode);
        Assert.Equal(403, (await Service(db, 71, false).GetAsync(Query())).StatusCode);
    }

    [Fact]
    public async Task Cross_tenant_batch_is_not_disclosed()
    {
        await using var db = Context(71);
        db.AcademicBatches.Add(new AcademicBatch { Id = 10, TenantId = 71, CampusId = 8,
            Name = "Nine", Code = "N9" });
        await db.SaveChangesAsync();
        Assert.Equal(404, (await Service(db, 72).GetAsync(Query())).StatusCode);
    }

    [Fact]
    public void Report_api_and_view_are_manager_only_and_module_scoped()
    {
        var type = typeof(StudentAttendanceReportsController);
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

    private static StudentAttendanceReportQueryDto Query() => new() {
        AcademicBatchId = 10, FromDate = new DateOnly(2026, 10, 1),
        ToDate = new DateOnly(2026, 10, 10)
    };
    private static StudentAttendanceReportService Service(EduOSDbContext db, long tenant, bool manager = true, bool principal = false) =>
        new(db, new Actor(tenant, manager, principal), NullLogger<StudentAttendanceReportService>.Instance);
    private static EduOSDbContext Context(long tenant)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("attendance-report-" + Guid.NewGuid().ToString("N")).Options;
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
