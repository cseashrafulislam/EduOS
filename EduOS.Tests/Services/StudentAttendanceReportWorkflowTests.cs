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
    public async Task Daily_report_aggregates_finalized_historical_marks_by_date_and_subject()
    {
        await using var db = Context(71);
        db.AcademicBatches.Add(new AcademicBatch { Id = 10, TenantId = 71, CampusId = 8,
            Name = "Cohort", Code = "C" });
        db.SubjectOfferings.AddRange(
            new SubjectOffering { Id = 61, TenantId = 71, AcademicBatchId = 10, Code = "M" },
            new SubjectOffering { Id = 62, TenantId = 71, AcademicBatchId = 10, Code = "S" });
        db.Students.Add(new Student { Id = 20, TenantId = 71, StudentCode = "ST20",
            FullName = "Learner", AdmissionDate = new DateOnly(2025, 1, 1) });
        db.StudentEnrollments.Add(new StudentEnrollment { Id = 30, TenantId = 71,
            StudentId = 20, AcademicBatchId = 10, CampusId = 8, RollNo = "OLD", IsCurrent = false });
        db.AttendanceSessions.AddRange(
            new AttendanceSession { Id = 40, TenantId = 71, AcademicBatchId = 10,
                SubjectOfferingId = 61, AttendanceDate = new DateOnly(2026, 10, 5), IsFinalized = true },
            new AttendanceSession { Id = 41, TenantId = 71, AcademicBatchId = 10,
                SubjectOfferingId = 62, AttendanceDate = new DateOnly(2026, 10, 5), IsFinalized = true },
            new AttendanceSession { Id = 42, TenantId = 71, AcademicBatchId = 10,
                SubjectOfferingId = 61, AttendanceDate = new DateOnly(2026, 10, 6), IsFinalized = true },
            new AttendanceSession { Id = 43, TenantId = 71, AcademicBatchId = 10,
                SubjectOfferingId = 61, AttendanceDate = new DateOnly(2026, 10, 7), IsFinalized = false });
        db.StudentAttendances.AddRange(
            new StudentAttendance { Id = 50, TenantId = 71, AttendanceSessionId = 40,
                StudentEnrollmentId = 30, State = AttendanceState.Present },
            new StudentAttendance { Id = 51, TenantId = 71, AttendanceSessionId = 41,
                StudentEnrollmentId = 30, State = AttendanceState.Absent },
            new StudentAttendance { Id = 52, TenantId = 71, AttendanceSessionId = 42,
                StudentEnrollmentId = 30, State = AttendanceState.Late },
            new StudentAttendance { Id = 53, TenantId = 71, AttendanceSessionId = 43,
                StudentEnrollmentId = 30, State = AttendanceState.Absent });
        await db.SaveChangesAsync();
        var service = Service(db, 71);
        var all = await service.GetDailySummaryAsync(Query());
        Assert.True(all.Success, all.Message);
        Assert.Equal(2, all.Data!.Count);
        Assert.Equal(new DateOnly(2026, 10, 5), all.Data[0].AttendanceDate);
        Assert.Equal(2, all.Data[0].Marked);
        Assert.Equal(1, all.Data[0].Present);
        Assert.Equal(1, all.Data[0].Absent);
        Assert.Equal(50m, all.Data[0].AttendancePercent);
        Assert.Equal(1, all.Data[1].Late);
        Assert.Equal(100m, all.Data[1].AttendancePercent);
        var subject = Query();
        subject.SubjectOfferingId = 61;
        var filtered = await service.GetDailySummaryAsync(subject);
        Assert.True(filtered.Success, filtered.Message);
        Assert.Equal(2, filtered.Data!.Count);
        Assert.Equal(1, filtered.Data[0].Marked);
        Assert.Equal(100m, filtered.Data[0].AttendancePercent);
        subject.SubjectOfferingId = 999;
        Assert.Equal(404, (await service.GetDailySummaryAsync(subject)).StatusCode);
    }

    [Fact]
    public async Task Daily_report_rejects_invalid_ranges_and_revoked_campus_access()
    {
        await using var db = Context(71);
        var invalid = Query();
        invalid.ToDate = invalid.FromDate.AddDays(31);
        Assert.Equal(400, (await Service(db, 71).GetDailySummaryAsync(invalid)).StatusCode);
        Assert.Equal(403, (await Service(db, 71, false).GetDailySummaryAsync(Query())).StatusCode);
        db.AcademicBatches.Add(new AcademicBatch { Id = 10, TenantId = 71, CampusId = 8,
            Name = "Cohort", Code = "C" });
        db.Campuses.Add(new Campus { Id = 8, TenantId = 71, Name = "North", Code = "N", IsActive = true });
        db.UserCampusAccesses.Add(new UserCampusAccess { Id = 101, TenantId = 71,
            UserId = 7, CampusId = 8, IsActive = true });
        await db.SaveChangesAsync();
        var principal = Service(db, 71, manager: false, principal: true);
        Assert.True((await principal.GetDailySummaryAsync(Query())).Success);
        db.UserCampusAccesses.Single().IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await principal.GetDailySummaryAsync(Query())).StatusCode);
        Assert.Equal(404, (await Service(db, 72).GetDailySummaryAsync(Query())).StatusCode);
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

    [Fact]
    public async Task Attendance_risk_register_filters_aggregates_before_paging_and_sorts_by_severity()
    {
        await using var db = Context(71);
        db.AcademicBatches.Add(new AcademicBatch { Id = 10, TenantId = 71, CampusId = 8,
            Name = "Cohort", Code = "C" });
        for (var i = 1; i <= 3; i++)
        {
            db.Students.Add(new Student { Id = 20 + i, TenantId = 71,
                StudentCode = "ST" + i, FullName = "Learner " + i,
                AdmissionDate = new DateOnly(2025, 1, 1) });
            db.StudentEnrollments.Add(new StudentEnrollment { Id = 30 + i, TenantId = 71,
                StudentId = 20 + i, AcademicBatchId = 10, CampusId = 8,
                RollNo = "R" + i, IsCurrent = false });
        }
        for (var i = 0; i < 3; i++)
            db.AttendanceSessions.Add(new AttendanceSession { Id = 40 + i, TenantId = 71,
                AcademicBatchId = 10, AttendanceDate = new DateOnly(2026, 10, 5 + i),
                IsFinalized = true });
        long markId = 50;
        for (var student = 1; student <= 3; student++)
            for (var day = 0; day < 3; day++)
                db.StudentAttendances.Add(new StudentAttendance {
                    Id = markId++, TenantId = 71, AttendanceSessionId = 40 + day,
                    StudentEnrollmentId = 30 + student,
                    State = student == 2 || (student == 1 && day < 2) ||
                        (student == 3 && day == 0) ? AttendanceState.Present : AttendanceState.Absent
                });
        await db.SaveChangesAsync();

        var service = Service(db, 71);
        var query = Query();
        query.MaxAttendancePercent = 75m;
        query.MinMarkedSessions = 3;
        query.Sort = "lowest";
        query.PageSize = 1;
        var first = await service.GetAsync(query);
        Assert.True(first.Success, first.Message);
        Assert.Equal(2, first.Data!.TotalCount);
        Assert.Equal("R3", Assert.Single(first.Data.Items).RollNo);
        query.Page = 2;
        var second = await service.GetAsync(query);
        Assert.True(second.Success, second.Message);
        Assert.Equal("R1", Assert.Single(second.Data!.Items).RollNo);
        query.Page = 1;
        query.Sort = "absent";
        Assert.Equal("R3", Assert.Single((await service.GetAsync(query)).Data!.Items).RollNo);
        query.Sort = "roll";
        Assert.Equal("R1", Assert.Single((await service.GetAsync(query)).Data!.Items).RollNo);
        query.MinMarkedSessions = 4;
        Assert.Equal(0, (await service.GetAsync(query)).Data!.TotalCount);
        query.MinMarkedSessions = 1;
        query.MaxAttendancePercent = 0m;
        Assert.Equal(0, (await service.GetAsync(query)).Data!.TotalCount);
    }

    [Fact]
    public async Task Attendance_risk_register_rejects_invalid_filters_and_respects_tenant_access()
    {
        await using var db = Context(71);
        var query = Query();
        query.MaxAttendancePercent = -1m;
        Assert.Equal(400, (await Service(db, 71).GetAsync(query)).StatusCode);
        query.MaxAttendancePercent = 101m;
        Assert.Equal(400, (await Service(db, 71).GetAsync(query)).StatusCode);
        query.MaxAttendancePercent = null;
        query.MinMarkedSessions = 0;
        Assert.Equal(400, (await Service(db, 71).GetAsync(query)).StatusCode);
        query.MinMarkedSessions = 1001;
        Assert.Equal(400, (await Service(db, 71).GetAsync(query)).StatusCode);
        query.MinMarkedSessions = 1;
        query.Sort = "unknown";
        Assert.Equal(400, (await Service(db, 71).GetAsync(query)).StatusCode);
        query.Sort = "lowest";
        Assert.Equal(403, (await Service(db, 71, manager: false).GetAsync(query)).StatusCode);
        Assert.Equal(404, (await Service(db, 72).GetAsync(query)).StatusCode);
    }


    [Fact]
    public async Task Risk_summary_counts_distinct_finalized_enrollments_across_all_severity_bands()
    {
        await using var db = Context(71);
        db.AcademicBatches.Add(new AcademicBatch { Id = 10, TenantId = 71, CampusId = 8,
            Name = "Cohort", Code = "C" });
        for (var i = 1; i <= 4; i++)
        {
            db.Students.Add(new Student { Id = 20 + i, TenantId = 71,
                StudentCode = "ST" + i, FullName = "Learner " + i,
                AdmissionDate = new DateOnly(2025, 1, 1) });
            db.StudentEnrollments.Add(new StudentEnrollment { Id = 30 + i, TenantId = 71,
                StudentId = 20 + i, AcademicBatchId = 10, CampusId = 8, RollNo = "R" + i });
        }
        for (var i = 0; i < 4; i++)
            db.AttendanceSessions.Add(new AttendanceSession { Id = 40 + i, TenantId = 71,
                AcademicBatchId = 10, AttendanceDate = new DateOnly(2026, 10, 5 + i),
                IsFinalized = i < 3 });
        long id = 100;
        for (var student = 1; student <= 4; student++)
            for (var day = 0; day < 4; day++)
                db.StudentAttendances.Add(new StudentAttendance {
                    Id = id++, TenantId = 71, AttendanceSessionId = 40 + day,
                    StudentEnrollmentId = 30 + student,
                    State = student == 4 || (student == 3 && day < 2) ||
                        (student == 2 && day == 0) ? AttendanceState.Present : AttendanceState.Absent
                });
        await db.SaveChangesAsync();
        var query = Query();
        query.MinMarkedSessions = 3;
        var result = await Service(db, 71).GetRiskSummaryAsync(query);
        Assert.True(result.Success, result.Message);
        Assert.Equal(4, result.Data!.EligibleEnrollments);
        Assert.Equal(3, result.Data.Below75);
        Assert.Equal(3, result.Data.Below90);
        Assert.Equal(3, result.Data.Below100);
        query.MinMarkedSessions = 4;
        var empty = await Service(db, 71).GetRiskSummaryAsync(query);
        Assert.True(empty.Success, empty.Message);
        Assert.Equal(0, empty.Data!.EligibleEnrollments);
    }

    [Fact]
    public async Task Risk_summary_rejects_direct_id_probes_and_revoked_campus_grants()
    {
        await using var db = Context(71);
        db.AcademicBatches.Add(new AcademicBatch { Id = 10, TenantId = 71,
            CampusId = 8, Name = "Cohort", Code = "C" });
        db.Campuses.Add(new Campus { Id = 8, TenantId = 71,
            Name = "North", Code = "N", IsActive = true });
        db.UserCampusAccesses.Add(new UserCampusAccess { Id = 101, TenantId = 71,
            UserId = 7, CampusId = 8, IsActive = true });
        db.SubjectOfferings.Add(new SubjectOffering { Id = 61, TenantId = 71,
            AcademicBatchId = 11, Code = "FOREIGN" });
        await db.SaveChangesAsync();
        var principal = Service(db, 71, manager: false, principal: true);
        Assert.True((await principal.GetRiskSummaryAsync(Query())).Success);
        var foreign = Query();
        foreign.SubjectOfferingId = 61;
        Assert.Equal(404, (await principal.GetRiskSummaryAsync(foreign)).StatusCode);
        Assert.Equal(404, (await Service(db, 72).GetRiskSummaryAsync(Query())).StatusCode);
        Assert.Equal(403, (await Service(db, 71, manager: false).GetRiskSummaryAsync(Query())).StatusCode);
        var invalid = Query();
        invalid.MinMarkedSessions = 0;
        Assert.Equal(400, (await principal.GetRiskSummaryAsync(invalid)).StatusCode);
        db.UserCampusAccesses.Single().IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await principal.GetRiskSummaryAsync(Query())).StatusCode);
    }

    [Fact]
    public async Task Subject_coverage_is_grouped_paged_and_scoped_to_finalized_historical_marks()
    {
        await using var db = Context(71);
        db.AcademicBatches.Add(new AcademicBatch { Id = 10, TenantId = 71, CampusId = 8,
            Name = "Cohort", Code = "C" });
        db.Campuses.Add(new Campus { Id = 8, TenantId = 71, Name = "North", Code = "N", IsActive = true });
        db.UserCampusAccesses.Add(new UserCampusAccess { Id = 101, TenantId = 71,
            UserId = 7, CampusId = 8, IsActive = true });
        db.Subjects.AddRange(
            new Subject { Id = 70, TenantId = 71, Code = "MATH", Name = "Mathematics" },
            new Subject { Id = 71, TenantId = 71, Code = "ENG", Name = "English" });
        db.CurriculumSubjects.AddRange(
            new CurriculumSubject { Id = 80, TenantId = 71, SubjectId = 70 },
            new CurriculumSubject { Id = 81, TenantId = 71, SubjectId = 71 });
        db.SubjectOfferings.AddRange(
            new SubjectOffering { Id = 61, TenantId = 71, AcademicBatchId = 10,
                CurriculumSubjectId = 80, Code = "M1" },
            new SubjectOffering { Id = 62, TenantId = 71, AcademicBatchId = 10,
                CurriculumSubjectId = 81, Code = "E1" },
            new SubjectOffering { Id = 63, TenantId = 71, AcademicBatchId = 11,
                CurriculumSubjectId = 80, Code = "FOREIGN" });
        db.Students.Add(new Student { Id = 20, TenantId = 71, StudentCode = "ST20",
            FullName = "Learner", AdmissionDate = new DateOnly(2025, 1, 1) });
        db.StudentEnrollments.Add(new StudentEnrollment { Id = 30, TenantId = 71,
            StudentId = 20, AcademicBatchId = 10, CampusId = 8, RollNo = "OLD",
            IsCurrent = false });
        db.AttendanceSessions.AddRange(
            new AttendanceSession { Id = 40, TenantId = 71, AcademicBatchId = 10,
                SubjectOfferingId = 61, AttendanceDate = new DateOnly(2026, 10, 5), IsFinalized = true },
            new AttendanceSession { Id = 41, TenantId = 71, AcademicBatchId = 10,
                SubjectOfferingId = 62, AttendanceDate = new DateOnly(2026, 10, 5), IsFinalized = true },
            new AttendanceSession { Id = 42, TenantId = 71, AcademicBatchId = 10,
                SubjectOfferingId = 61, AttendanceDate = new DateOnly(2026, 10, 6), IsFinalized = false },
            new AttendanceSession { Id = 43, TenantId = 71, AcademicBatchId = 10,
                AttendanceDate = new DateOnly(2026, 10, 6), IsFinalized = true });
        db.StudentAttendances.AddRange(
            new StudentAttendance { Id = 50, TenantId = 71, AttendanceSessionId = 40,
                StudentEnrollmentId = 30, State = AttendanceState.Present },
            new StudentAttendance { Id = 51, TenantId = 71, AttendanceSessionId = 41,
                StudentEnrollmentId = 30, State = AttendanceState.Absent },
            new StudentAttendance { Id = 52, TenantId = 71, AttendanceSessionId = 42,
                StudentEnrollmentId = 30, State = AttendanceState.Absent },
            new StudentAttendance { Id = 53, TenantId = 71, AttendanceSessionId = 43,
                StudentEnrollmentId = 30, State = AttendanceState.Present });
        await db.SaveChangesAsync();
        var principal = Service(db, 71, manager: false, principal: true);
        var query = Query();
        query.PageSize = 1;
        var first = await principal.GetSubjectSummaryAsync(query, null, "lowest");
        Assert.True(first.Success, first.Message);
        Assert.Equal(2, first.Data!.TotalCount);
        var english = Assert.Single(first.Data.Items);
        Assert.Equal("English", english.SubjectName);
        Assert.Equal(1, english.Absent);
        Assert.Equal(0m, english.AttendancePercent);
        query.Page = 2;
        var second = await principal.GetSubjectSummaryAsync(query, null, "lowest");
        Assert.Equal("Mathematics", Assert.Single(second.Data!.Items).SubjectName);
        query.Page = 1;
        var searched = await principal.GetSubjectSummaryAsync(query, "Math", "subject");
        Assert.Equal(1, searched.Data!.TotalCount);
        Assert.Equal(100m, Assert.Single(searched.Data.Items).AttendancePercent);
        query.SubjectOfferingId = 63;
        Assert.Equal(404, (await principal.GetSubjectSummaryAsync(query, null, "subject")).StatusCode);
        query.SubjectOfferingId = null;
        Assert.Equal(403, (await Service(db, 71, manager: false)
            .GetSubjectSummaryAsync(query, null, "subject")).StatusCode);
        Assert.Equal(404, (await Service(db, 72)
            .GetSubjectSummaryAsync(query, null, "subject")).StatusCode);
        Assert.Equal(400, (await principal.GetSubjectSummaryAsync(query, null, "unknown")).StatusCode);
        Assert.Equal(400, (await principal.GetSubjectSummaryAsync(query, new string('x', 101), "subject")).StatusCode);
        db.UserCampusAccesses.Single().IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await principal.GetSubjectSummaryAsync(query, null, "subject")).StatusCode);
    }

    [Fact]
    public async Task Session_register_includes_unmarked_drafts_and_preserves_finalized_history()
    {
        await using var db = Context(71);
        db.AcademicBatches.Add(new AcademicBatch { Id = 10, TenantId = 71, CampusId = 8,
            Name = "Cohort", Code = "C" });
        db.Students.Add(new Student { Id = 20, TenantId = 71, StudentCode = "ST20",
            FullName = "Learner", AdmissionDate = new DateOnly(2025, 1, 1) });
        db.StudentEnrollments.Add(new StudentEnrollment { Id = 30, TenantId = 71,
            StudentId = 20, AcademicBatchId = 10, CampusId = 8, RollNo = "OLD", IsCurrent = false });
        db.AttendanceSessions.AddRange(
            new AttendanceSession { Id = 40, TenantId = 71, AcademicBatchId = 10,
                AttendanceDate = new DateOnly(2026, 10, 5), IsFinalized = true },
            new AttendanceSession { Id = 41, TenantId = 71, AcademicBatchId = 10,
                AttendanceDate = new DateOnly(2026, 10, 6), IsFinalized = false },
            new AttendanceSession { Id = 42, TenantId = 71, AcademicBatchId = 11,
                AttendanceDate = new DateOnly(2026, 10, 6), IsFinalized = false });
        db.StudentAttendances.Add(new StudentAttendance { Id = 50, TenantId = 71,
            AttendanceSessionId = 40, StudentEnrollmentId = 30, State = AttendanceState.Present });
        await db.SaveChangesAsync();

        var service = Service(db, 71);
        var draft = await service.GetSessionRegisterAsync(Query(), "draft");
        Assert.True(draft.Success, draft.Message);
        Assert.Equal(1, draft.Data!.TotalCount);
        var unmarked = Assert.Single(draft.Data.Items);
        Assert.Equal(41, unmarked.SessionId);
        Assert.Equal(0, unmarked.Marked);
        Assert.False(unmarked.IsFinalized);

        var finalized = await service.GetSessionRegisterAsync(Query(), "finalized");
        Assert.True(finalized.Success, finalized.Message);
        Assert.Equal(40, Assert.Single(finalized.Data!.Items).SessionId);
        Assert.Equal(1, finalized.Data.Items[0].Present);
        var paged = Query();
        paged.PageSize = 1;
        Assert.Equal(2, (await service.GetSessionRegisterAsync(paged, "all")).Data!.TotalCount);
        Assert.Equal(41, Assert.Single((await service.GetSessionRegisterAsync(paged, "all")).Data!.Items).SessionId);
        paged.Page = 2;
        Assert.Equal(40, Assert.Single((await service.GetSessionRegisterAsync(paged, "all")).Data!.Items).SessionId);
    }

    [Fact]
    public async Task Session_register_rejects_invalid_filters_and_live_campus_revocation()
    {
        await using var db = Context(71);
        db.AcademicBatches.Add(new AcademicBatch { Id = 10, TenantId = 71, CampusId = 8,
            Name = "Cohort", Code = "C" });
        db.Campuses.Add(new Campus { Id = 8, TenantId = 71,
            Name = "North", Code = "N", IsActive = true });
        db.UserCampusAccesses.Add(new UserCampusAccess { Id = 101, TenantId = 71,
            UserId = 7, CampusId = 8, IsActive = true });
        db.SubjectOfferings.Add(new SubjectOffering { Id = 61, TenantId = 71,
            AcademicBatchId = 11, Code = "OTHER" });
        await db.SaveChangesAsync();
        var principal = Service(db, 71, manager: false, principal: true);
        Assert.True((await principal.GetSessionRegisterAsync(Query(), "draft")).Success);
        Assert.Equal(400, (await principal.GetSessionRegisterAsync(Query(), "wrong")).StatusCode);
        var invalid = Query(); invalid.PageSize = 101;
        Assert.Equal(400, (await principal.GetSessionRegisterAsync(invalid, "all")).StatusCode);
        invalid = Query(); invalid.SubjectOfferingId = 61;
        Assert.Equal(404, (await principal.GetSessionRegisterAsync(invalid, "all")).StatusCode);
        Assert.Equal(404, (await Service(db, 72).GetSessionRegisterAsync(Query(), "all")).StatusCode);
        Assert.Equal(403, (await Service(db, 71, manager: false)
            .GetSessionRegisterAsync(Query(), "all")).StatusCode);
        db.UserCampusAccesses.Single().IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await principal.GetSessionRegisterAsync(Query(), "draft")).StatusCode);
    }

    [Fact]
    public async Task Session_overview_and_empty_status_filter_reconcile_authorized_historical_marks()
    {
        await using var db = Context(71);
        db.AcademicBatches.Add(new AcademicBatch { Id = 10, TenantId = 71, CampusId = 8,
            Name = "Cohort", Code = "C" });
        db.Students.Add(new Student { Id = 20, TenantId = 71, StudentCode = "ST20",
            FullName = "Learner", AdmissionDate = new DateOnly(2025, 1, 1) });
        db.StudentEnrollments.Add(new StudentEnrollment { Id = 30, TenantId = 71,
            StudentId = 20, AcademicBatchId = 10, CampusId = 8, RollNo = "OLD", IsCurrent = false });
        db.AttendanceSessions.AddRange(
            new AttendanceSession { Id = 40, TenantId = 71, AcademicBatchId = 10,
                AttendanceDate = new DateOnly(2026, 10, 5), IsFinalized = true },
            new AttendanceSession { Id = 41, TenantId = 71, AcademicBatchId = 10,
                AttendanceDate = new DateOnly(2026, 10, 6), IsFinalized = false },
            new AttendanceSession { Id = 42, TenantId = 71, AcademicBatchId = 10,
                AttendanceDate = new DateOnly(2026, 10, 7), IsFinalized = false },
            new AttendanceSession { Id = 43, TenantId = 71, AcademicBatchId = 10,
                AttendanceDate = new DateOnly(2026, 10, 8), IsFinalized = true },
            new AttendanceSession { Id = 44, TenantId = 71, AcademicBatchId = 11,
                AttendanceDate = new DateOnly(2026, 10, 8), IsFinalized = false });
        db.StudentAttendances.AddRange(
            new StudentAttendance { Id = 50, TenantId = 71, AttendanceSessionId = 40,
                StudentEnrollmentId = 30, State = AttendanceState.Present },
            new StudentAttendance { Id = 51, TenantId = 71, AttendanceSessionId = 42,
                StudentEnrollmentId = 30, State = AttendanceState.Absent });
        await db.SaveChangesAsync();
        var service = Service(db, 71);
        var overview = await service.GetSessionOverviewAsync(Query());
        Assert.True(overview.Success, overview.Message);
        Assert.Equal(4, overview.Data!.TotalSessions);
        Assert.Equal(2, overview.Data.DraftSessions);
        Assert.Equal(2, overview.Data.FinalizedSessions);
        Assert.Equal(1, overview.Data.EmptyDraftSessions);
        Assert.Equal(1, overview.Data.EmptyFinalizedSessions);
        var empty = await service.GetSessionRegisterAsync(Query(), "empty");
        Assert.Equal(2, empty.Data!.TotalCount);
        Assert.Equal(new long[] { 43, 41 }, empty.Data.Items.Select(x => x.SessionId));
        Assert.Equal(41, Assert.Single((await service.GetSessionRegisterAsync(Query(),
            "empty-draft")).Data!.Items).SessionId);
        Assert.Equal(43, Assert.Single((await service.GetSessionRegisterAsync(Query(),
            "empty-finalized")).Data!.Items).SessionId);
        Assert.Equal(403, (await Service(db, 71, manager: false)
            .GetSessionOverviewAsync(Query())).StatusCode);
        Assert.Equal(404, (await Service(db, 72).GetSessionOverviewAsync(Query())).StatusCode);
        var invalid = Query(); invalid.ToDate = invalid.FromDate.AddDays(31);
        Assert.Equal(400, (await service.GetSessionOverviewAsync(invalid)).StatusCode);
        Assert.Equal(400, (await service.GetSessionRegisterAsync(Query(), "empty-invalid")).StatusCode);
    }

    [Fact]
    public async Task Session_overview_rejects_foreign_subject_and_revoked_campus_grant()
    {
        await using var db = Context(71);
        db.AcademicBatches.Add(new AcademicBatch { Id = 10, TenantId = 71, CampusId = 8,
            Name = "Cohort", Code = "C" });
        db.Campuses.Add(new Campus { Id = 8, TenantId = 71, Name = "North",
            Code = "N", IsActive = true });
        db.UserCampusAccesses.Add(new UserCampusAccess { Id = 101, TenantId = 71,
            UserId = 7, CampusId = 8, IsActive = true });
        db.SubjectOfferings.Add(new SubjectOffering { Id = 61, TenantId = 71,
            AcademicBatchId = 11, Code = "FOREIGN" });
        await db.SaveChangesAsync();
        var principal = Service(db, 71, manager: false, principal: true);
        Assert.True((await principal.GetSessionOverviewAsync(Query())).Success);
        var foreign = Query(); foreign.SubjectOfferingId = 61;
        Assert.Equal(404, (await principal.GetSessionOverviewAsync(foreign)).StatusCode);
        db.UserCampusAccesses.Single().IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await principal.GetSessionOverviewAsync(Query())).StatusCode);
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
