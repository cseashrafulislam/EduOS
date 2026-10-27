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
