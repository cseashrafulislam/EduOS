using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.LMS;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TeacherCpdServiceTests
{
    private sealed class Current(string role, long id = 21, long tenant = 20) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => id;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private static EduOSDbContext Db(string name, long tenant = 20)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseInMemoryDatabase(name).Options;
        var accessor = new MockHttpContextAccessor(tenant);
        return new EduOSDbContext(options, accessor);
    }

    private sealed class MockHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
        public MockHttpContextAccessor(long tenant)
        {
            var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "21"), new Claim("TenantId", tenant.ToString()),
                new Claim(ClaimTypes.Role, "Teacher")], "Test")) };
            http.Items["TenantId"] = tenant;
            HttpContext = http;
        }
    }

    private static TeacherCpdService Service(EduOSDbContext db, string role = "Teacher", long id = 21,
        long tenant = 20) => new(db, new Current(role, id, tenant), TimeProvider.System,
            NullLogger<TeacherCpdService>.Instance);

    [Fact]
    public async Task TeacherEnrolmentIsIdempotentAndStoresNoFakeStudent()
    {
        var name = Guid.NewGuid().ToString("N");
        await using var db = Db(name);
        db.Employees.Add(new Employee { TenantId = 20, UserId = 21, EmployeeCode = "T-21",
            FullName = "Teacher", CanTeach = true, State = EmployeeState.Active });
        var course = new Course { TenantId = 20, Title = "Teaching practice", Code = "CPD-1",
            IsTeacherCpd = true, IsActive = true };
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        var service = Service(db);
        var request = new TeacherCpdEnrollRequestDto
        { CourseReference = course.PublicId, ClientRequestId = Guid.NewGuid() };
        (await service.EnrollAsync(request)).Success.Should().BeTrue();
        (await service.EnrollAsync(request)).Success.Should().BeTrue();
        (await service.EnrollAsync(new TeacherCpdEnrollRequestDto
        { CourseReference = course.PublicId, ClientRequestId = Guid.NewGuid() })).Success.Should().BeTrue();
        (await db.CourseEnrollments.CountAsync()).Should().Be(1);
        var enrollment = await db.CourseEnrollments.SingleAsync();
        enrollment.StudentId.Should().BeNull();
        enrollment.EmployeeId.Should().NotBeNull();
    }

    [Fact]
    public async Task RoleAndAudienceAreEnforcedAndRequestReplayCannotChangeParticipant()
    {
        await using var db = Db(Guid.NewGuid().ToString("N"));
        db.Employees.AddRange(
            new Employee { TenantId = 20, UserId = 21, EmployeeCode = "T-21", FullName = "Teacher 1", CanTeach = true },
            new Employee { TenantId = 20, UserId = 22, EmployeeCode = "T-22", FullName = "Teacher 2", CanTeach = true });
        var cpd = new Course { TenantId = 20, Title = "CPD", Code = "CPD", IsTeacherCpd = true };
        var academic = new Course { TenantId = 20, Title = "Math", Code = "MATH" };
        db.Courses.AddRange(cpd, academic);
        await db.SaveChangesAsync();
        var reference = Guid.NewGuid();
        var owner = new TeacherCpdEnrollRequestDto { CourseReference = cpd.PublicId, ClientRequestId = reference };
        (await Service(db, "Student").EnrollAsync(owner)).StatusCode.Should().Be(403);
        (await Service(db).EnrollAsync(new TeacherCpdEnrollRequestDto
        { CourseReference = academic.PublicId, ClientRequestId = Guid.NewGuid() })).StatusCode.Should().Be(404);
        (await Service(db).EnrollAsync(owner)).Success.Should().BeTrue();
        (await Service(db, id: 22).EnrollAsync(owner)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task TeacherMayOnlyUpdateOwnPublishedLessonAndCannotUndoCompletion()
    {
        await using var db = Db(Guid.NewGuid().ToString("N"));
        db.Employees.AddRange(
            new Employee { TenantId = 20, UserId = 21, EmployeeCode = "T-21", FullName = "Owner", CanTeach = true },
            new Employee { TenantId = 20, UserId = 22, EmployeeCode = "T-22", FullName = "Other", CanTeach = true });
        var course = new Course { TenantId = 20, Title = "CPD", Code = "CPD", IsTeacherCpd = true };
        var other = new Course { TenantId = 20, Title = "Other", Code = "OTHER", IsTeacherCpd = true };
        db.Courses.AddRange(course, other);await db.SaveChangesAsync();
        var lesson = new Lesson { TenantId = 20, CourseId = course.Id, Title = "Classroom", IsPublished = true };
        var foreign = new Lesson { TenantId = 20, CourseId = other.Id, Title = "Unrelated", IsPublished = true };
        db.Lessons.AddRange(lesson, foreign);await db.SaveChangesAsync();
        var service = Service(db);
        var response = await service.EnrollAsync(new TeacherCpdEnrollRequestDto
        { CourseReference = course.PublicId, ClientRequestId = Guid.NewGuid() });
        var enrollmentId = response.Data!.EnrollmentId!.Value;
        (await Service(db, id: 22).SaveLessonAsync(enrollmentId, new TeacherCpdLessonRequestDto
        { LessonReference = lesson.PublicId, IsCompleted = true })).StatusCode.Should().Be(409);
        (await service.SaveLessonAsync(enrollmentId, new TeacherCpdLessonRequestDto
        { LessonReference = foreign.PublicId, IsCompleted = true })).StatusCode.Should().Be(404);
        (await service.SaveLessonAsync(enrollmentId, new TeacherCpdLessonRequestDto
        { LessonReference = lesson.PublicId, IsCompleted = true })).Success.Should().BeTrue();
        (await service.SaveLessonAsync(enrollmentId, new TeacherCpdLessonRequestDto
        { LessonReference = lesson.PublicId, IsCompleted = false, ProgressPercent = 0 })).Success.Should().BeTrue();
        (await db.LessonProgresses.SingleAsync()).IsCompleted.Should().BeTrue();
    }
}
