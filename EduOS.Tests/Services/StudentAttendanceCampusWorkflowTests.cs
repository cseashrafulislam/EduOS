using EduOS.Core.DTOs.Attendance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.HR;
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
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentAttendanceCampusWorkflowTests
{
    private static readonly DateOnly AttendanceDate = new(2026, 10, 10);

    [Fact]
    public async Task Principal_batch_picker_and_roster_require_live_campus_access()
    {
        await using var db = Context();
        await SeedBatches(db);
        var principal = Service(db, new Actor("Principal"));
        var query = new StudentAttendanceRosterQueryDto
        {
            AcademicBatchId = 10, AttendanceDate = AttendanceDate
        };
        Assert.Empty((await principal.GetBatchChoicesAsync(AttendanceDate, null)).Data!.Items);
        Assert.Equal(409, (await principal.GetRosterAsync(query)).StatusCode);

        var grant = new UserCampusAccess
        {
            Id = 31, TenantId = 71, UserId = 7, CampusId = 8, IsActive = true
        };
        db.UserCampusAccesses.Add(grant);
        await db.SaveChangesAsync();
        var page = await principal.GetBatchChoicesAsync(AttendanceDate, null, pageSize: 1);
        Assert.True(page.Success, page.Message);
        Assert.Equal(1, page.Data!.TotalCount);
        Assert.Equal(10, Assert.Single(page.Data.Items).Id);
        Assert.Equal(1, (await principal.GetBatchChoicesAsync(null, "North")).Data!.TotalCount);
        Assert.True((await principal.GetRosterAsync(query)).Success);
        Assert.Equal(409, (await principal.GetRosterAsync(new StudentAttendanceRosterQueryDto
        {
            AcademicBatchId = 11, AttendanceDate = AttendanceDate
        })).StatusCode);

        grant.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Empty((await principal.GetBatchChoicesAsync(AttendanceDate, null)).Data!.Items);
        Assert.Equal(409, (await principal.GetRosterAsync(query)).StatusCode);
        grant.IsActive = true;
        (await db.Campuses.FindAsync(8L))!.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Empty((await principal.GetBatchChoicesAsync(AttendanceDate, null)).Data!.Items);
        Assert.Equal(409, (await principal.GetRosterAsync(query)).StatusCode);

        var admin = Service(db, new Actor("TenantAdmin"));
        Assert.Equal(2, (await admin.GetBatchChoicesAsync(null, null)).Data!.TotalCount);
        Assert.Equal(400, (await principal.GetBatchChoicesAsync(AttendanceDate, null, page: 0)).StatusCode);
        Assert.Equal(400, (await principal.GetBatchChoicesAsync(AttendanceDate, null, pageSize: 101)).StatusCode);
        Assert.Empty((await Service(db, new Actor("Teacher"))
            .GetBatchChoicesAsync(AttendanceDate, null)).Data!.Items);
    }

    [Fact]
    public async Task Principal_cannot_replay_finalized_session_or_read_history_after_revocation()
    {
        await using var db = Context();
        await SeedBatches(db);
        db.Students.Add(new Student
        {
            Id = 40, TenantId = 71, StudentCode = "S40", FullName = "Student",
            StatusCode = "Active", AdmissionDate = new DateOnly(2026, 1, 1)
        });
        db.StudentEnrollments.Add(new StudentEnrollment
        {
            Id = 41, TenantId = 71, StudentId = 40, AcademicBatchId = 10,
            CampusId = 8, AcademicYearId = 3, AcademicLevelId = 4,
            RollNo = "1", IsCurrent = true, State = EnrollmentState.Active
        });
        db.AttendanceSessions.Add(new AttendanceSession
        {
            Id = 42, TenantId = 71, AcademicBatchId = 10,
            AttendanceDate = AttendanceDate, IsFinalized = true
        });
        db.StudentAttendances.Add(new StudentAttendance
        {
            Id = 43, TenantId = 71, AttendanceSessionId = 42,
            StudentEnrollmentId = 41, State = AttendanceState.Present
        });
        await db.SaveChangesAsync();
        var principal = Service(db, new Actor("VicePrincipal"));
        const string version = "AQID";
        Assert.Equal(404, (await principal.FinalizeSessionAsync(42, version)).StatusCode);
        Assert.Equal(404, (await principal.GetCorrectionsAsync(43)).StatusCode);
        Assert.Equal(404, (await principal.CorrectAttendanceAsync(43, new AttendanceCorrectionRequestDto
        {
            NewState = AttendanceState.Absent, Reason = "Authorized audit only", RowVersion = version
        })).StatusCode);

        var grant = new UserCampusAccess
        {
            Id = 44, TenantId = 71, UserId = 7, CampusId = 8, IsActive = true
        };
        db.UserCampusAccesses.Add(grant);
        await db.SaveChangesAsync();
        Assert.True((await principal.GetCorrectionsAsync(43)).Success);
        Assert.True((await principal.FinalizeSessionAsync(42, version)).Success);
        grant.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await principal.GetCorrectionsAsync(43)).StatusCode);
        Assert.Equal(404, (await principal.FinalizeSessionAsync(42, version)).StatusCode);
    }


    [Fact]
    public async Task Subject_picker_is_campus_scoped_and_revocation_safe()
    {
        await using var db = Context();
        await SeedBatches(db);
        await SeedOfferings(db);
        var principal = Service(db, new Actor("Principal"));
        Assert.Equal(404, (await principal.GetSubjectChoicesAsync(10, AttendanceDate, null)).StatusCode);
        db.UserCampusAccesses.Add(new UserCampusAccess
        {
            Id = 90, TenantId = 71, UserId = 7, CampusId = 8, IsActive = true
        });
        await db.SaveChangesAsync();
        var result = await principal.GetSubjectChoicesAsync(10, AttendanceDate, "Mat", pageSize: 1);
        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Data!.TotalCount);
        Assert.Equal(70, Assert.Single(result.Data.Items).Id);
        Assert.Equal(404, (await principal.GetSubjectChoicesAsync(11, AttendanceDate, null)).StatusCode);
        Assert.Equal(400, (await principal.GetSubjectChoicesAsync(10, AttendanceDate, null, pageSize: 101)).StatusCode);
        (await db.UserCampusAccesses.FindAsync(90L))!.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await principal.GetSubjectChoicesAsync(10, AttendanceDate, null)).StatusCode);
    }

    [Fact]
    public async Task Teacher_subject_picker_and_registered_roster_enforce_assignment()
    {
        await using var db = Context();
        await SeedBatches(db);
        await SeedOfferings(db);
        db.Employees.Add(new Employee
        {
            Id = 80, TenantId = 71, UserId = 7, EmployeeCode = "T7",
            FullName = "Instructor", State = EmployeeState.Active
        });
        db.InstructorAssignments.Add(new InstructorAssignment
        {
            Id = 81, TenantId = 71, EmployeeId = 80, SubjectOfferingId = 70,
            EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true
        });
        db.Students.AddRange(
            new Student { Id = 100, TenantId = 71, StudentCode = "S100",
                FullName = "Registered", StatusCode = "Active" },
            new Student { Id = 101, TenantId = 71, StudentCode = "S101",
                FullName = "Unregistered", StatusCode = "Active" });
        db.StudentEnrollments.AddRange(
            new StudentEnrollment { Id = 110, TenantId = 71, StudentId = 100,
                AcademicBatchId = 10, CampusId = 8, AcademicYearId = 3,
                RollNo = "01", IsCurrent = true, State = EnrollmentState.Active },
            new StudentEnrollment { Id = 111, TenantId = 71, StudentId = 101,
                AcademicBatchId = 10, CampusId = 8, AcademicYearId = 3,
                RollNo = "02", IsCurrent = true, State = EnrollmentState.Active });
        db.StudentSubjectRegistrations.Add(new StudentSubjectRegistration
        {
            Id = 120, TenantId = 71, StudentEnrollmentId = 110,
            SubjectOfferingId = 70, State = SubjectRegistrationState.Approved
        });
        await db.SaveChangesAsync();
        var teacher = Service(db, new Actor("Teacher"));
        var choices = await teacher.GetSubjectChoicesAsync(10, AttendanceDate, null);
        Assert.True(choices.Success, choices.Message);
        Assert.Equal(70, Assert.Single(choices.Data!.Items).Id);
        Assert.Equal(404, (await teacher.GetSubjectChoicesAsync(11, AttendanceDate, null)).StatusCode);
        var roster = await teacher.GetRosterAsync(new StudentAttendanceRosterQueryDto
        {
            AcademicBatchId = 10, AttendanceDate = AttendanceDate, SubjectOfferingId = 70
        });
        Assert.True(roster.Success, roster.Message);
        Assert.Equal("S100", Assert.Single(roster.Data!.Students).StudentCode);
        Assert.Equal(409, (await teacher.GetRosterAsync(new StudentAttendanceRosterQueryDto
        {
            AcademicBatchId = 10, AttendanceDate = AttendanceDate, SubjectOfferingId = 71
        })).StatusCode);
        (await db.InstructorAssignments.FindAsync(81L))!.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await teacher.GetSubjectChoicesAsync(10, AttendanceDate, null)).StatusCode);
    }


    [Fact]
    public async Task Correction_report_is_historical_campus_scoped_and_revocation_safe()
    {
        await using var db = Context();
        await SeedBatches(db);
        db.Students.AddRange(
            new Student { Id = 201, TenantId = 71, StudentCode = "N201", FullName = "North learner",
                StatusCode = "Active", AdmissionDate = new DateOnly(2026, 1, 1) },
            new Student { Id = 202, TenantId = 71, StudentCode = "S202", FullName = "South learner",
                StatusCode = "Active", AdmissionDate = new DateOnly(2026, 1, 1) });
        db.StudentEnrollments.AddRange(
            new StudentEnrollment { Id = 211, TenantId = 71, StudentId = 201,
                AcademicBatchId = 10, CampusId = 8, AcademicYearId = 3, AcademicLevelId = 4,
                RollNo = "N1", State = EnrollmentState.Completed, IsCurrent = false },
            new StudentEnrollment { Id = 212, TenantId = 71, StudentId = 202,
                AcademicBatchId = 11, CampusId = 9, AcademicYearId = 3, AcademicLevelId = 4,
                RollNo = "S1", State = EnrollmentState.Active, IsCurrent = true });
        db.AttendanceSessions.AddRange(
            new AttendanceSession { Id = 221, TenantId = 71, AcademicBatchId = 10,
                AttendanceDate = AttendanceDate, IsFinalized = true },
            new AttendanceSession { Id = 222, TenantId = 71, AcademicBatchId = 11,
                AttendanceDate = AttendanceDate, IsFinalized = true });
        db.StudentAttendances.AddRange(
            new StudentAttendance { Id = 231, TenantId = 71, AttendanceSessionId = 221,
                StudentEnrollmentId = 211, State = AttendanceState.Present },
            new StudentAttendance { Id = 232, TenantId = 71, AttendanceSessionId = 222,
                StudentEnrollmentId = 212, State = AttendanceState.Present });
        db.StudentAttendanceAdjustments.AddRange(
            new StudentAttendanceAdjustment { Id = 241, TenantId = 71,
                StudentAttendanceId = 231, PreviousState = AttendanceState.Absent,
                NewState = AttendanceState.Present, Reason = "North audit",
                ChangedByUserId = 7, ChangedAt = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc) },
            new StudentAttendanceAdjustment { Id = 242, TenantId = 71,
                StudentAttendanceId = 232, PreviousState = AttendanceState.Absent,
                NewState = AttendanceState.Present, Reason = "South audit",
                ChangedByUserId = 7, ChangedAt = new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc) });
        await db.SaveChangesAsync();

        var principal = Service(db, new Actor("Principal"));
        var query = new StudentAttendanceCorrectionReportQueryDto
        {
            AcademicBatchId = 10, FromDate = AttendanceDate, ToDate = AttendanceDate
        };
        Assert.Equal(404, (await principal.GetCorrectionReportAsync(query)).StatusCode);
        var grant = new UserCampusAccess { Id = 251, TenantId = 71, UserId = 7,
            CampusId = 8, IsActive = true };
        db.UserCampusAccesses.Add(grant);
        await db.SaveChangesAsync();
        var allowed = await principal.GetCorrectionReportAsync(query);
        Assert.True(allowed.Success, allowed.Message);
        Assert.Equal(1, allowed.Data!.TotalCount);
        Assert.Equal("N201", Assert.Single(allowed.Data.Items).StudentCode);
        Assert.Equal("N1", Assert.Single(allowed.Data.Items).RollNo);
        Assert.Equal(404, (await principal.GetCorrectionReportAsync(new StudentAttendanceCorrectionReportQueryDto
        {
            AcademicBatchId = 11, FromDate = AttendanceDate, ToDate = AttendanceDate
        })).StatusCode);
        Assert.Equal(400, (await principal.GetCorrectionReportAsync(query, pageSize: 101)).StatusCode);
        Assert.Equal(400, (await principal.GetCorrectionReportAsync(query, page: 0)).StatusCode);
        grant.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await principal.GetCorrectionReportAsync(query)).StatusCode);
        var admin = Service(db, new Actor("TenantAdmin"));
        Assert.Equal("S202", Assert.Single((await admin.GetCorrectionReportAsync(
            new StudentAttendanceCorrectionReportQueryDto
            {
                AcademicBatchId = 11, FromDate = AttendanceDate, ToDate = AttendanceDate
            })).Data!.Items).StudentCode);
    }

    [Fact]
    public async Task Correction_report_filters_subject_and_pages_without_cross_batch_offerings()
    {
        await using var db = Context();
        await SeedBatches(db);
        await SeedOfferings(db);
        db.Students.Add(new Student { Id = 301, TenantId = 71, StudentCode = "A301",
            FullName = "Learner", StatusCode = "Active", AdmissionDate = new DateOnly(2026, 1, 1) });
        db.StudentEnrollments.Add(new StudentEnrollment { Id = 311, TenantId = 71,
            StudentId = 301, AcademicBatchId = 10, CampusId = 8, AcademicYearId = 3,
            AcademicLevelId = 4, RollNo = "5", State = EnrollmentState.Active, IsCurrent = true });
        db.AttendanceSessions.AddRange(
            new AttendanceSession { Id = 321, TenantId = 71, AcademicBatchId = 10,
                AttendanceDate = AttendanceDate, SubjectOfferingId = 70, IsFinalized = true },
            new AttendanceSession { Id = 322, TenantId = 71, AcademicBatchId = 10,
                AttendanceDate = AttendanceDate, SubjectOfferingId = 71, IsFinalized = true });
        db.StudentAttendances.AddRange(
            new StudentAttendance { Id = 331, TenantId = 71, AttendanceSessionId = 321,
                StudentEnrollmentId = 311, State = AttendanceState.Present },
            new StudentAttendance { Id = 332, TenantId = 71, AttendanceSessionId = 322,
                StudentEnrollmentId = 311, State = AttendanceState.Present });
        for (var i = 0; i < 3; i++)
            db.StudentAttendanceAdjustments.Add(new StudentAttendanceAdjustment
            {
                Id = 340 + i, TenantId = 71, StudentAttendanceId = 331,
                PreviousState = AttendanceState.Absent, NewState = AttendanceState.Present,
                Reason = "Audit " + i, ChangedByUserId = 7,
                ChangedAt = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc).AddMinutes(i)
            });
        db.StudentAttendanceAdjustments.Add(new StudentAttendanceAdjustment
        {
            Id = 350, TenantId = 71, StudentAttendanceId = 332,
            PreviousState = AttendanceState.Leave, NewState = AttendanceState.Absent,
            Reason = "Other subject", ChangedByUserId = 8
        });
        await db.SaveChangesAsync();
        var service = Service(db, new Actor("TenantAdmin"));
        var query = new StudentAttendanceCorrectionReportQueryDto
        {
            AcademicBatchId = 10, FromDate = AttendanceDate, ToDate = AttendanceDate,
            SubjectOfferingId = 70
        };
        var first = await service.GetCorrectionReportAsync(query, pageSize: 2);
        Assert.True(first.Success, first.Message);
        Assert.Equal(3, first.Data!.TotalCount);
        Assert.Equal(2, first.Data.Items.Count);
        Assert.Equal(342, first.Data.Items[0].AdjustmentId);
        Assert.Equal(1, (await service.GetCorrectionReportAsync(query, page: 2, pageSize: 2))
            .Data!.Items.Count);
        query.SubjectOfferingId = 72;
        Assert.Equal(404, (await service.GetCorrectionReportAsync(query)).StatusCode);
        query.SubjectOfferingId = null;
        Assert.Equal(4, (await service.GetCorrectionReportAsync(query)).Data!.TotalCount);
        query.StudentSearch = "A30";
        Assert.Equal(4, (await service.GetCorrectionReportAsync(query)).Data!.TotalCount);
        query.StudentSearch = "unknown";
        Assert.Equal(0, (await service.GetCorrectionReportAsync(query)).Data!.TotalCount);
        query.StudentSearch = null;
        query.ChangedByUserId = 8;
        Assert.Equal(1, (await service.GetCorrectionReportAsync(query)).Data!.TotalCount);
        query.PreviousState = AttendanceState.Leave;
        query.NewState = AttendanceState.Absent;
        Assert.Equal(350, Assert.Single((await service.GetCorrectionReportAsync(query)).Data!.Items).AdjustmentId);
        query.ChangedByUserId = 7;
        Assert.Equal(0, (await service.GetCorrectionReportAsync(query)).Data!.TotalCount);
        query.ChangedByUserId = 0;
        Assert.Equal(400, (await service.GetCorrectionReportAsync(query)).StatusCode);
        query.ChangedByUserId = null;
        query.PreviousState = (AttendanceState)99;
        Assert.Equal(400, (await service.GetCorrectionReportAsync(query)).StatusCode);
        query.PreviousState = null;
        query.NewState = (AttendanceState)99;
        Assert.Equal(400, (await service.GetCorrectionReportAsync(query)).StatusCode);
        query.NewState = null;
        query.StudentSearch = new string('x', 101);
        Assert.Equal(400, (await service.GetCorrectionReportAsync(query)).StatusCode);
        query.StudentSearch = null;
        query.ToDate = query.FromDate.AddDays(31);
        Assert.Equal(400, (await service.GetCorrectionReportAsync(query)).StatusCode);
    }

    private static async Task SeedOfferings(EduOSDbContext db)
    {
        db.Subjects.AddRange(
            new Subject { Id = 50, TenantId = 71, Code = "MATH", Name = "Mathematics" },
            new Subject { Id = 51, TenantId = 71, Code = "ENG", Name = "English" });
        db.CurriculumSubjects.AddRange(
            new CurriculumSubject { Id = 60, TenantId = 71, SubjectId = 50, AcademicLevelId = 4 },
            new CurriculumSubject { Id = 61, TenantId = 71, SubjectId = 51, AcademicLevelId = 4 });
        db.SubjectOfferings.AddRange(
            new SubjectOffering { Id = 70, TenantId = 71, AcademicBatchId = 10,
                CurriculumSubjectId = 60, AcademicYearId = 3, Code = "N-MATH", IsActive = true },
            new SubjectOffering { Id = 71, TenantId = 71, AcademicBatchId = 10,
                CurriculumSubjectId = 61, AcademicYearId = 3, Code = "N-ENG", IsActive = true },
            new SubjectOffering { Id = 72, TenantId = 71, AcademicBatchId = 11,
                CurriculumSubjectId = 61, AcademicYearId = 3, Code = "S-ENG", IsActive = true });
        await db.SaveChangesAsync();
    }

    private static async Task SeedBatches(EduOSDbContext db)
    {
        db.Campuses.AddRange(
            new Campus { Id = 8, TenantId = 71, Name = "North", Code = "N", IsActive = true },
            new Campus { Id = 9, TenantId = 71, Name = "South", Code = "S", IsActive = true });
        db.AcademicYears.Add(new AcademicYear
        {
            Id = 3, TenantId = 71, Name = "2026", Code = "2026", IsActive = true,
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31)
        });
        db.AcademicPrograms.Add(new AcademicProgram
        {
            Id = 2, TenantId = 71, Name = "Secondary", Code = "SEC"
        });
        db.AcademicLevels.Add(new AcademicLevel
        {
            Id = 4, TenantId = 71, AcademicProgramId = 2,
            Name = "Nine", Code = "N9", IsActive = true
        });
        db.AcademicBatches.AddRange(
            new AcademicBatch { Id = 10, TenantId = 71, CampusId = 8,
                AcademicYearId = 3, AcademicProgramId = 2, AcademicLevelId = 4,
                Name = "North batch", Code = "NB", IsActive = true },
            new AcademicBatch { Id = 11, TenantId = 71, CampusId = 9,
                AcademicYearId = 3, AcademicProgramId = 2, AcademicLevelId = 4,
                Name = "South batch", Code = "SB", IsActive = true });
        await db.SaveChangesAsync();
    }

    private static EduOSDbContext Context()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("attendance-campus-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "7"),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", "71")
            }, "TestAuthentication"))
        };
        http.Items["TenantId"] = 71L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static StudentAttendanceService Service(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<StudentAttendance>(db),
        new GenericRepository<StudentAttendanceAdjustment>(db),
        new GenericRepository<StudentLeaveApplication>(db),
        new GenericRepository<AttendanceSession>(db),
        new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<StudentSubjectRegistration>(db),
        new GenericRepository<Student>(db),
        new GenericRepository<AcademicBatch>(db),
        new GenericRepository<AcademicYear>(db),
        new GenericRepository<Employee>(db),
        new GenericRepository<InstructorAssignment>(db),
        new GenericRepository<SubjectOffering>(db),
        db, db, user, TimeProvider.System,
        NullLogger<StudentAttendanceService>.Instance);

    private sealed class Actor(string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => 71;
        public long UserId => 7;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => new[] { role };
        public bool IsInRole(string name) => name == role;
        public string? FullName => null;
        public string? Email => null;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
