using EduOS.Core.DTOs.Attendance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Attendance;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>Real SQL Server verifies student leave approval and derived roster flags without rewriting attendance.</summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerAcademicOperationsIntegrationTests
{
    [Fact]
    public async Task Approved_student_leave_appears_in_roster_without_auto_posting_and_review_is_optimistic()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, managerId, studentUserId, batchId, campusId;
        Guid enrollmentReference;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-LEAVE-" + suffix, Name = "Academic Operations CI",
                Email = "ops-" + suffix + "@example.test", State = TenantState.Active };
            var adminName = "ops-admin-" + suffix + "@example.test";
            var studentName = "ops-student-" + suffix + "@example.test";
            var manager = new ApplicationUser { UserName = adminName,
                NormalizedUserName = adminName.ToUpperInvariant(), Email = adminName,
                NormalizedEmail = adminName.ToUpperInvariant(), FullName = "Leave reviewer",
                EmailConfirmed = true, IsActive = true, SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString() };
            var studentUser = new ApplicationUser { UserName = studentName,
                NormalizedUserName = studentName.ToUpperInvariant(), Email = studentName,
                NormalizedEmail = studentName.ToUpperInvariant(), FullName = "Leave applicant",
                EmailConfirmed = true, IsActive = true, SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString() };
            var person = new Person { FullName = "Leave CI Student" };
            setup.AddRange(tenant, manager, studentUser, person);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; managerId = manager.Id; studentUserId = studentUser.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Name = "Main", Code = "MAIN" };
                var year = new AcademicYear { TenantId = tenantId, Name = "2026", Code = "2026",
                    StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
                var program = new AcademicProgram { TenantId = tenantId, Name = "Secondary", Code = "SEC" };
                var student = new Student { TenantId = tenantId, UserId = studentUserId,
                    PersonId = person.Id, FullName = "Leave CI Student",
                    StudentCode = "LEAVE-" + suffix, StatusCode = "Active",
                    AdmissionDate = new DateOnly(2026, 1, 1) };
                setup.AddRange(campus, year, program, student);
                await setup.SaveChangesAsync();
                campusId = campus.Id;
                var level = new AcademicLevel { TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Nine", Code = "G9", LevelNo = 9 };
                var curriculum = new AcademicCurriculum { TenantId = tenantId,
                    AcademicProgramId = program.Id, Name = "2026", Code = "SEC26",
                    EffectiveFrom = new DateOnly(2026, 1, 1), IsCurrent = true };
                setup.AddRange(level, curriculum);
                await setup.SaveChangesAsync();
                var batch = new AcademicBatch { TenantId = tenantId, CampusId = campus.Id,
                    AcademicYearId = year.Id, AcademicProgramId = program.Id,
                    AcademicLevelId = level.Id, Name = "Nine A", Code = "NINE-A", Capacity = 30 };
                setup.AcademicBatches.Add(batch);
                await setup.SaveChangesAsync();
                batchId = batch.Id;
                var enrollment = new StudentEnrollment { TenantId = tenantId, StudentId = student.Id,
                    CampusId = campus.Id, AcademicYearId = year.Id,
                    AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                    AcademicCurriculumId = curriculum.Id, AcademicBatchId = batch.Id,
                    RollNo = "01", EnrollmentDate = new DateOnly(2026, 1, 1),
                    IsCurrent = true, State = EnrollmentState.Active, ClientRequestId = Guid.NewGuid() };
                setup.StudentEnrollments.Add(enrollment);
                await setup.SaveChangesAsync();
                enrollmentReference = enrollment.PublicId;
            }
        }
        await using var db = TenantDb(options, tenantId);
        var studentLeave = LeaveService(db, new Actor(tenantId, studentUserId, "Student"));
        var reviewer = LeaveService(db, new Actor(tenantId, managerId, "TenantAdmin"));
        var attendance = AttendanceService(db, new Actor(tenantId, managerId, "TenantAdmin"));
        var request = new CreateStudentLeaveApplicationRequestDto {
            ClientRequestId = Guid.NewGuid(), StudentEnrollmentReference = enrollmentReference,
            FromDate = new DateOnly(2026, 10, 10), ToDate = new DateOnly(2026, 10, 11),
            Reason = "Personal leave" };
        var submitted = await studentLeave.ApplyAsync(request);
        Assert.True(submitted.Success, submitted.Message);
        var replay = await studentLeave.ApplyAsync(request);
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(submitted.Data!.Id, replay.Data!.Id);
        var before = await attendance.GetRosterAsync(new StudentAttendanceRosterQueryDto {
            AcademicBatchId = batchId, AttendanceDate = new DateOnly(2026, 10, 10) });
        Assert.True(before.Success, before.Message);
        Assert.False(Assert.Single(before.Data!.Students).HasApprovedLeave);
        var stale = await reviewer.ReviewAsync(submitted.Data.Id, new ReviewStudentLeaveRequestDto {
            State = LeaveState.Approved, RowVersion = Convert.ToBase64String(new byte[8]) });
        Assert.Equal(409, stale.StatusCode);
        var approved = await reviewer.ReviewAsync(submitted.Data.Id, new ReviewStudentLeaveRequestDto {
            State = LeaveState.Approved, RowVersion = submitted.Data.RowVersion,
            ReviewNote = "Approved" });
        Assert.True(approved.Success, approved.Message);
        db.ChangeTracker.Clear();
        // Real SQL Server: a historical enrollment's approved leave remains visible
        // in a bounded report, with dates clipped without altering attendance.
        var leaveReport = new StudentLeavePeriodReportService(
            db, new Actor(tenantId, managerId, "TenantAdmin"),
            NullLogger<StudentLeavePeriodReportService>.Instance);
        var period = await leaveReport.GetAsync(new StudentLeavePeriodReportQueryDto {
            AcademicBatchId = batchId, FromDate = new DateOnly(2026, 10, 10),
            ToDate = new DateOnly(2026, 10, 10), State = LeaveState.Approved
        });
        Assert.True(period.Success, period.Message);
        var periodRow = Assert.Single(period.Data!.Items);
        Assert.Equal(enrollmentReference, periodRow.StudentEnrollmentReference);
        Assert.Equal("01", periodRow.RollNo);
        Assert.Equal(1, periodRow.PeriodDays);
        Assert.Equal(1, period.Data.TotalCount);
        Assert.Equal(404, (await new StudentLeavePeriodReportService(
            db, new Actor(tenantId + 1, managerId, "TenantAdmin"),
            NullLogger<StudentLeavePeriodReportService>.Instance)
            .GetAsync(new StudentLeavePeriodReportQueryDto {
                AcademicBatchId = batchId, FromDate = new DateOnly(2026, 10, 10),
                ToDate = new DateOnly(2026, 10, 10)
            })).StatusCode);
        var after = await attendance.GetRosterAsync(new StudentAttendanceRosterQueryDto {
            AcademicBatchId = batchId, AttendanceDate = new DateOnly(2026, 10, 10) });
        Assert.True(after.Success, after.Message);
        Assert.True(Assert.Single(after.Data!.Students).HasApprovedLeave);
        Assert.Equal(1, after.Data.Summary.Unmarked);
        Assert.Empty(await db.StudentAttendances.ToListAsync());
        Assert.Single(await db.StudentLeaveApplications.AsNoTracking().Where(x =>
            x.TenantId == tenantId && x.State == LeaveState.Approved).ToListAsync());

        // A manager can finalize only a fully marked roster; Leave must be
        // deliberately recorded, never inferred as an already-posted mark.
        var session = await attendance.EnsureSessionAsync(new EnsureStudentAttendanceSessionDto
        {
            AcademicBatchId = batchId, AttendanceDate = new DateOnly(2026, 10, 10)
        });
        Assert.True(session.Success, session.Message);
        var incomplete = await attendance.FinalizeSessionAsync(
            session.Data!.AttendanceSessionId, session.Data.SessionRowVersion);
        Assert.Equal(409, incomplete.StatusCode);
        var saved = await attendance.SaveAsync(new SaveAttendanceRegisterRequestDto
        {
            AttendanceSessionId = session.Data.AttendanceSessionId,
            SessionRowVersion = session.Data.SessionRowVersion,
            Students = [new SaveStudentAttendanceRequestDto
            {
                StudentEnrollmentReference = enrollmentReference, State = AttendanceState.Leave
            }]
        });
        Assert.True(saved.Success, saved.Message);
        // A draft register is editable through SaveAsync, not the audited correction API.
        db.ChangeTracker.Clear();
        var draftMark = await db.StudentAttendances.AsNoTracking().SingleAsync();
        var draftCorrection = await attendance.CorrectAttendanceAsync(draftMark.Id,
            new AttendanceCorrectionRequestDto
            {
                NewState = AttendanceState.Absent, Reason = "Draft correction should be rejected",
                RowVersion = Convert.ToBase64String(draftMark.RowVersion)
            });
        Assert.Equal(409, draftCorrection.StatusCode);
        Assert.Empty(await db.StudentAttendanceAdjustments.AsNoTracking().ToListAsync());
        db.ChangeTracker.Clear();
        var finalized = await attendance.FinalizeSessionAsync(
            saved.Data!.AttendanceSessionId, saved.Data.SessionRowVersion);
        Assert.True(finalized.Success, finalized.Message);
        Assert.True(finalized.Data!.IsFinalized);
        var finalizedReplay = await attendance.FinalizeSessionAsync(
            saved.Data.AttendanceSessionId, saved.Data.SessionRowVersion);
        Assert.True(finalizedReplay.Success, finalizedReplay.Message);
        Assert.True(finalizedReplay.Data!.IsFinalized);
        var locked = await attendance.SaveAsync(new SaveAttendanceRegisterRequestDto
        {
            AttendanceSessionId = finalized.Data.AttendanceSessionId,
            SessionRowVersion = finalized.Data.SessionRowVersion,
            Students = [new SaveStudentAttendanceRequestDto
            {
                StudentEnrollmentReference = enrollmentReference, State = AttendanceState.Present,
                RowVersion = finalized.Data.Students[0].RowVersion
            }]
        });
        Assert.Equal(409, locked.StatusCode);
        db.ChangeTracker.Clear();
        var leaveMark = await db.StudentAttendances.AsNoTracking().SingleAsync();
        Assert.Equal(AttendanceState.Leave, leaveMark.State);
        var changed = await attendance.CorrectAttendanceAsync(leaveMark.Id, new AttendanceCorrectionRequestDto
        {
            NewState = AttendanceState.Absent,
            Reason = "Attendance review confirmed student was absent",
            RowVersion = Convert.ToBase64String(leaveMark.RowVersion)
        });
        Assert.True(changed.Success, changed.Message);
        db.ChangeTracker.Clear();
        var posted = await db.StudentAttendances.AsNoTracking().SingleAsync();
        Assert.Equal(AttendanceState.Absent, posted.State);
        var adjustment = await db.StudentAttendanceAdjustments.AsNoTracking().SingleAsync();
        Assert.Equal(AttendanceState.Leave, adjustment.PreviousState);
        Assert.Equal(AttendanceState.Absent, adjustment.NewState);
        Assert.Equal(managerId, adjustment.ChangedByUserId);
        var history = await attendance.GetCorrectionsAsync(leaveMark.Id);
        Assert.True(history.Success, history.Message);
        Assert.Equal(1, history.Data!.TotalCount);
        var eventRow = Assert.Single(history.Data.Items);
        Assert.Equal(adjustment.Id, eventRow.Id);
        Assert.Equal(AttendanceState.Leave, eventRow.PreviousState);
        Assert.Equal(AttendanceState.Absent, eventRow.NewState);
        Assert.Equal(managerId, eventRow.ChangedByUserId);
        Assert.Equal(adjustment.Reason, eventRow.Reason);

        // SQL Server translation and live-grant enforcement across the complete
        // register lifecycle: picker, roster, finalized replay and audit history.
        var principal = AttendanceService(db, new Actor(tenantId, managerId, "Principal"));
        var scopedRoster = new StudentAttendanceRosterQueryDto
        {
            AcademicBatchId = batchId, AttendanceDate = new DateOnly(2026, 10, 10)
        };
        Assert.Empty((await principal.GetBatchChoicesAsync(scopedRoster.AttendanceDate, null)).Data!.Items);
        Assert.Equal(409, (await principal.GetRosterAsync(scopedRoster)).StatusCode);
        Assert.Equal(404, (await principal.GetCorrectionsAsync(leaveMark.Id)).StatusCode);
        Assert.Equal(404, (await principal.FinalizeSessionAsync(
            finalized.Data.AttendanceSessionId, saved.Data.SessionRowVersion)).StatusCode);
        var grant = new UserCampusAccess
        {
            TenantId = tenantId, UserId = managerId, CampusId = campusId, IsActive = true
        };
        db.UserCampusAccesses.Add(grant);
        await db.SaveChangesAsync();
        Assert.Equal(1, (await principal.GetBatchChoicesAsync(scopedRoster.AttendanceDate, null)).Data!.TotalCount);
        Assert.True((await principal.GetRosterAsync(scopedRoster)).Success);
        Assert.True((await principal.GetCorrectionsAsync(leaveMark.Id)).Success);
        Assert.True((await principal.FinalizeSessionAsync(
            finalized.Data.AttendanceSessionId, saved.Data.SessionRowVersion)).Success);
        grant.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await principal.GetCorrectionsAsync(leaveMark.Id)).StatusCode);
        Assert.Equal(404, (await principal.FinalizeSessionAsync(
            finalized.Data.AttendanceSessionId, saved.Data.SessionRowVersion)).StatusCode);

        var paged = await attendance.GetCorrectionsAsync(leaveMark.Id, page: 2, pageSize: 1);
        Assert.True(paged.Success, paged.Message);
        Assert.Empty(paged.Data!.Items);
        Assert.Equal(1, paged.Data.TotalCount);
        Assert.Equal(400, (await attendance.GetCorrectionsAsync(leaveMark.Id, page: 0)).StatusCode);
        Assert.Equal(400, (await attendance.GetCorrectionsAsync(leaveMark.Id, pageSize: 101)).StatusCode);
        Assert.Equal(404, (await attendance.GetCorrectionsAsync(long.MaxValue)).StatusCode);
        var teacher = AttendanceService(db, new Actor(tenantId, managerId, "Teacher"));
        Assert.Equal(403, (await teacher.GetCorrectionsAsync(leaveMark.Id)).StatusCode);
        var otherTenant = AttendanceService(db, new Actor(tenantId + 999, managerId, "TenantAdmin"));
        Assert.Equal(404, (await otherTenant.GetCorrectionsAsync(leaveMark.Id)).StatusCode);
        // A replay with a stale version cannot create another correction event.
        var correctionReplay = await attendance.CorrectAttendanceAsync(leaveMark.Id, new AttendanceCorrectionRequestDto
        {
            NewState = AttendanceState.Absent, Reason = "Attendance review confirmed student was absent",
            RowVersion = Convert.ToBase64String(leaveMark.RowVersion)
        });
        Assert.Equal(409, correctionReplay.StatusCode);
        Assert.Single(await db.StudentAttendanceAdjustments.AsNoTracking().ToListAsync());
        var finalizedSession = await db.AttendanceSessions.AsNoTracking().SingleAsync();
        Assert.True(finalizedSession.IsFinalized);
        Assert.Equal(managerId, finalizedSession.FinalizedByUserId);

        // Real SQL Server executes the grouped historical-enrollment report query.
        // The audited correction must appear in the report's final status totals.
        var reportService = new StudentAttendanceReportService(
            db, new Actor(tenantId, managerId, "TenantAdmin"),
            NullLogger<StudentAttendanceReportService>.Instance);
        var reportQuery = new StudentAttendanceReportQueryDto
        {
            AcademicBatchId = batchId,
            FromDate = new DateOnly(2026, 10, 10),
            ToDate = new DateOnly(2026, 10, 11)
        };
        var report = await reportService.GetAsync(reportQuery);
        Assert.True(report.Success, report.Message);
        var reportRow = Assert.Single(report.Data!.Items);
        Assert.Equal(enrollmentReference, reportRow.StudentEnrollmentReference);
        Assert.Equal("01", reportRow.RollNo);
        Assert.Equal(1, reportRow.MarkedSessions);
        Assert.Equal(1, reportRow.Absent);
        Assert.Equal(0m, reportRow.AttendancePercent);
        Assert.Equal(403, (await new StudentAttendanceReportService(
            db, new Actor(tenantId, managerId, "Teacher"),
            NullLogger<StudentAttendanceReportService>.Instance).GetAsync(reportQuery)).StatusCode);
        Assert.Equal(404, (await new StudentAttendanceReportService(
            db, new Actor(tenantId + 999, managerId, "TenantAdmin"),
            NullLogger<StudentAttendanceReportService>.Instance).GetAsync(reportQuery)).StatusCode);
    }

    private static EduOSDbContext TenantDb(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, accessor);
    }
    private static StudentLeaveService LeaveService(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<StudentLeaveApplication>(db),
        new GenericRepository<StudentEnrollment>(db), new GenericRepository<Student>(db),
        new GenericRepository<StudentGuardian>(db), new GenericRepository<Guardian>(db),
        new GenericRepository<AcademicYear>(db), db, user, TimeProvider.System,
        NullLogger<StudentLeaveService>.Instance, db, null!, null!);
    private static StudentAttendanceService AttendanceService(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<StudentAttendance>(db),
        new GenericRepository<StudentAttendanceAdjustment>(db),
        new GenericRepository<StudentLeaveApplication>(db),
        new GenericRepository<AttendanceSession>(db),
        new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<StudentSubjectRegistration>(db),
        new GenericRepository<Student>(db), new GenericRepository<AcademicBatch>(db),
        new GenericRepository<AcademicYear>(db), new GenericRepository<Employee>(db),
        new GenericRepository<InstructorAssignment>(db), new GenericRepository<SubjectOffering>(db),
        db, db, user, TimeProvider.System, NullLogger<StudentAttendanceService>.Instance);

    private sealed class Actor(long tenant, long userId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenant;
        public long UserId => userId;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string name) => name == role;
        public string? FullName => role;
        public string? Email => "ci@example.test";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "SQL academic operations CI";
    }
}
