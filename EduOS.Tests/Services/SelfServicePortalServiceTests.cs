using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Portals;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public class SelfServicePortalServiceTests
{
    [Fact]
    public async Task Student_portal_lists_only_current_tenant_student_linked_to_user()
    {
        await using var context = CreateContext(out var accessor);
        var own = Student(10, 99, "OWN"), other = Student(10, 100, "OTHER"), foreign = Student(20, 99, "FOREIGN");
        context.Set<Student>().AddRange(own, other, foreign);
        await context.SaveChangesAsync();
        SetTenant(accessor, 10);
        var result = await CreateService(context, new TestCurrentUser(10, 99, "Student")).GetLinkedStudentsAsync();
        result.Success.Should().BeTrue();
        result.Data.Should().ContainSingle();
        result.Data![0].StudentCode.Should().Be("OWN");
    }

    [Fact]
    public async Task Guardian_portal_lists_only_students_with_explicit_guardian_link()
    {
        await using var context = CreateContext(out var accessor);
        var linked = Student(10, null, "LINKED"), unlinked = Student(10, null, "UNLINKED");
        var guardian = new Guardian { TenantId = 10, PersonId = 1, UserId = 99, FullName = "Guardian", IsActive = true };
        context.Set<Student>().AddRange(linked, unlinked);
        context.Set<Guardian>().Add(guardian);
        await context.SaveChangesAsync();
        context.Set<StudentGuardian>().Add(new StudentGuardian
        {
            TenantId = 10, StudentId = linked.Id, GuardianId = guardian.Id,
            RelationCode = "Father", IsPrimary = true
        });
        await context.SaveChangesAsync();
        SetTenant(accessor, 10);
        var service = CreateService(context, new TestCurrentUser(10, 99, "Guardian"));
        var result = await service.GetLinkedStudentsAsync();
        result.Success.Should().BeTrue();
        result.Data.Should().ContainSingle();
        result.Data![0].Reference.Should().Be(linked.PublicId);
        (await service.GetTimetableAsync(unlinked.PublicId)).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Portal_does_not_disclose_unlinked_or_cross_tenant_student_data()
    {
        await using var context = CreateContext(out var accessor);
        var linked = Student(10, 99, "OWN"), unrelated = Student(10, 100, "OTHER"), foreign = Student(20, 99, "FOREIGN");
        context.Set<Student>().AddRange(linked, unrelated, foreign);
        await context.SaveChangesAsync(); SetTenant(accessor, 10);
        var service = CreateService(context, new TestCurrentUser(10, 99, "Student"));
        (await service.GetTransportAsync(unrelated.PublicId)).StatusCode.Should().Be(403);
        (await service.GetAssignmentsAsync(unrelated.PublicId)).StatusCode.Should().Be(403);
        (await service.GetHomeworkAsync(foreign.PublicId)).StatusCode.Should().Be(403);
        (await service.GetTimetableAsync(foreign.PublicId)).StatusCode.Should().Be(403);
        (await service.GetFeesAsync(foreign.PublicId)).StatusCode.Should().Be(403);
        (await service.GetResultsAsync(foreign.PublicId)).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Homework_contains_only_published_homework_for_active_course_enrollments()
    {
        await using var context = CreateContext(out var accessor);
        var student = Student(10, 99, "OWN");
        var subject = new Subject { TenantId = 10, Code = "MATH", Name = "Mathematics" };
        var course = Course(10, "Math");
        context.Set<Student>().Add(student);
        context.Set<Subject>().Add(subject);
        context.Set<Course>().Add(course);
        await context.SaveChangesAsync();
        course.SubjectId = subject.Id;
        context.Set<CourseEnrollment>().Add(new CourseEnrollment
        {
            TenantId = 10, CourseId = course.Id, StudentId = student.Id,
            ClientRequestId = Guid.NewGuid(), State = CourseEnrollmentState.Active
        });
        context.Set<Assignment>().AddRange(
            Assignment(10, course.Id, "Visible homework", LearningTaskType.Homework, true),
            Assignment(10, course.Id, "Unpublished homework", LearningTaskType.Homework, false),
            Assignment(10, course.Id, "Different assignment", LearningTaskType.Assignment, true));
        await context.SaveChangesAsync(); SetTenant(accessor, 10);
        var response = await CreateService(context, new TestCurrentUser(10, 99, "Student")).GetHomeworkAsync(student.PublicId);
        response.Success.Should().BeTrue();
        response.Data.Should().ContainSingle();
        response.Data![0].Title.Should().Be("Visible homework");
        response.Data[0].SubjectName.Should().Be("Mathematics");
    }

    [Fact]
    public async Task Assignments_exclude_unpublished_and_courses_without_active_enrollment()
    {
        await using var context = CreateContext(out var accessor);
        var student = Student(10, 99, "OWN");
        var active = Course(10, "Active course"), inactive = Course(10, "Inactive course");
        context.Set<Student>().Add(student);
        context.Set<Course>().AddRange(active, inactive);
        await context.SaveChangesAsync();
        context.Set<CourseEnrollment>().AddRange(
            new CourseEnrollment { TenantId = 10, StudentId = student.Id, CourseId = active.Id, State = CourseEnrollmentState.Active, ClientRequestId = Guid.NewGuid() },
            new CourseEnrollment { TenantId = 10, StudentId = student.Id, CourseId = inactive.Id, State = CourseEnrollmentState.Dropped, ClientRequestId = Guid.NewGuid() });
        context.Set<Assignment>().AddRange(
            Assignment(10, active.Id, "Visible", LearningTaskType.Assignment, true),
            Assignment(10, active.Id, "Draft", LearningTaskType.Assignment, false),
            Assignment(10, inactive.Id, "Dropped", LearningTaskType.Assignment, true));
        await context.SaveChangesAsync(); SetTenant(accessor, 10);
        var result = await CreateService(context, new TestCurrentUser(10, 99, "Student")).GetAssignmentsAsync(student.PublicId);
        result.Success.Should().BeTrue();
        result.Data.Should().ContainSingle();
        result.Data![0].CourseTitle.Should().Be("Active course");
    }

    [Fact]
    public async Task Timetable_returns_active_current_enrollment_subject_in_correct_day_order()
    {
        await using var context = CreateContext(out var accessor);
        var student = Student(10, 99, "OWN"), subject = new Subject { TenantId = 10, Name = "Bangla", Code = "BAN" };
        var employee = new Employee { TenantId = 10, PersonId = 1, DesignationId = 1, EmployeeCode = "T-1", FullName = "Teacher",
            JoiningDate = DateOnly.FromDateTime(DateTime.UtcNow), State = EmployeeState.Active, CanTeach = true };
        var curriculumSubject = new CurriculumSubject { TenantId = 10, SubjectId = 1, AcademicCurriculumId = 1, AcademicLevelId = 1 };
        context.Set<Student>().Add(student); context.Set<Subject>().Add(subject); context.Set<Employee>().Add(employee);
        await context.SaveChangesAsync();
        curriculumSubject.SubjectId = subject.Id; context.Set<CurriculumSubject>().Add(curriculumSubject);
        context.Set<StudentEnrollment>().Add(new StudentEnrollment
        {
            TenantId = 10, StudentId = student.Id, ClientRequestId = Guid.NewGuid(),
            CampusId = 1, AcademicYearId = 1, AcademicProgramId = 1, AcademicLevelId = 1,
            AcademicBatchId = 5, AcademicCurriculumId = 1, RollNo = "1",
            EnrollmentDate = DateOnly.FromDateTime(DateTime.UtcNow), IsActive = true, IsCurrent = true,
            State = EnrollmentState.Active
        });
        await context.SaveChangesAsync();
        var offering = new SubjectOffering { TenantId = 10, AcademicYearId = 1, AcademicBatchId = 5,
            CurriculumSubjectId = curriculumSubject.Id, Code = "BAN", IsActive = true };
        var saturday = new RoutineTimeSlot { TenantId = 10, Name = "Morning", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 45) };
        var sunday = new RoutineTimeSlot { TenantId = 10, Name = "Afternoon", StartTime = new TimeOnly(11, 0), EndTime = new TimeOnly(11, 45) };
        context.AddRange(offering, saturday, sunday);
        await context.SaveChangesAsync();
        var assignment = new InstructorAssignment
        {
            TenantId = 10, EmployeeId = employee.Id, SubjectOfferingId = offering.Id,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)), IsActive = true
        };
        context.Set<InstructorAssignment>().Add(assignment);
        await context.SaveChangesAsync();
        var effective = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        context.Set<RoutineEntry>().AddRange(
            new RoutineEntry { TenantId = 10, SubjectOfferingId = offering.Id, RoutineTimeSlotId = sunday.Id,
                InstructorAssignmentId = assignment.Id, DayOfWeek = DayOfWeek.Sunday, EffectiveFrom = effective, IsActive = true },
            new RoutineEntry { TenantId = 10, SubjectOfferingId = offering.Id, RoutineTimeSlotId = saturday.Id,
                InstructorAssignmentId = assignment.Id, DayOfWeek = DayOfWeek.Saturday, EffectiveFrom = effective, IsActive = true });
        await context.SaveChangesAsync(); SetTenant(accessor, 10);
        var result = await CreateService(context, new TestCurrentUser(10, 99, "Student")).GetTimetableAsync(student.PublicId);
        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(2);
        result.Data!.Select(x => x.DayOfWeek).Should().ContainInOrder("Saturday", "Sunday");
        result.Data[0].TeacherName.Should().Be("Teacher");
    }

    private static EduOSDbContext CreateContext(out HttpContextAccessor accessor)
    {
        accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("student-portal-" + Guid.NewGuid().ToString("N")).Options, accessor);
    }

    private static void SetTenant(HttpContextAccessor accessor, long tenantId) => accessor.HttpContext!.Items["TenantId"] = tenantId;

    private static SelfServicePortalService CreateService(EduOSDbContext context, ICurrentUserService currentUser) => new(
        new GenericRepository<Student>(context),
        new GenericRepository<Guardian>(context),
        new GenericRepository<StudentGuardian>(context),
        new GenericRepository<StudentEnrollment>(context),
        new GenericRepository<AcademicBatch>(context),
        new GenericRepository<RoutineEntry>(context),
        new GenericRepository<RoutineTimeSlot>(context),
        new GenericRepository<SubjectOffering>(context),
        new GenericRepository<CurriculumSubject>(context),
        new GenericRepository<InstructorAssignment>(context),
        new GenericRepository<Employee>(context),
        new GenericRepository<Room>(context),
        new GenericRepository<StudentAttendance>(context),
        new GenericRepository<AttendanceSession>(context),
        new GenericRepository<StudentResultSummary>(context),
        new GenericRepository<ResultPublication>(context),
        new GenericRepository<Assessment>(context),
        new GenericRepository<StudentInvoice>(context),
        new GenericRepository<StudentPayment>(context),
        new GenericRepository<StudentTransport>(context),
        new GenericRepository<Route>(context),
        new GenericRepository<Vehicle>(context),
        new GenericRepository<RouteStop>(context),
        new GenericRepository<Course>(context),
        new GenericRepository<Subject>(context),
        new GenericRepository<Assignment>(context),
        new GenericRepository<CourseEnrollment>(context),
        currentUser, NullLogger<SelfServicePortalService>.Instance);

    private static Student Student(long tenantId, long? userId, string code) => new()
    {
        TenantId = tenantId, UserId = userId, PersonId = 1, StudentCode = code, FullName = code,
        AdmissionDate = DateOnly.FromDateTime(DateTime.UtcNow), IsActive = true
    };

    private static Course Course(long tenant, string title) => new()
    {
        TenantId = tenant, Title = title, Code = "COURSE-" + Guid.NewGuid().ToString("N")[..10], IsActive = true
    };

    private static Assignment Assignment(long tenant, long courseId, string title, LearningTaskType kind, bool published) => new()
    {
        TenantId = tenant, CourseId = courseId, Title = title, Type = kind, IsPublished = published,
        MaxMarks = 100, OpensAt = DateTime.UtcNow.AddDays(-1), DueAt = DateTime.UtcNow.AddDays(2)
    };

    private sealed class TestCurrentUser(long tenantId, long userId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => role;
        public string? Email => "portal@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string requestedRole) => string.Equals(requestedRole, role, StringComparison.OrdinalIgnoreCase);
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
