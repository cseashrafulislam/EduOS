using EduOS.Core.DTOs.Exams;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Employees;
using EduOS.Core.Entities.Exams;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Exams;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class ExamWorkflowServiceTests
{
    [Fact]
    public async Task Assigned_teacher_can_open_only_owned_mark_roster()
    {
        await using var context = await CreateContextAsync(isClassTeacher: false);
        var service = CreateService(context, new TestCurrentUser(10, 900, "Teacher"));

        var owned = await service.GetMarkRosterAsync(MarkScope(context, "MATH"));
        var unowned = await service.GetMarkRosterAsync(MarkScope(context, "SCI"));

        owned.Success.Should().BeTrue();
        unowned.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Subject_teacher_cannot_read_whole_section_result_unless_class_teacher()
    {
        await using var subjectContext = await CreateContextAsync(isClassTeacher: false);
        var denied = await CreateService(subjectContext, new TestCurrentUser(10, 900, "Teacher")).GetResultsAsync(ResultScope(subjectContext));
        denied.StatusCode.Should().Be(403);

        await using var classContext = await CreateContextAsync(isClassTeacher: true);
        var allowed = await CreateService(classContext, new TestCurrentUser(10, 900, "Teacher")).GetResultsAsync(ResultScope(classContext));
        allowed.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Exam_manager_bypasses_teacher_assignment_scope()
    {
        await using var context = await CreateContextAsync(isClassTeacher: false);
        var result = await CreateService(context, new TestCurrentUser(10, 901, "ExamController")).GetMarkRosterAsync(MarkScope(context, "SCI"));
        result.Success.Should().BeTrue();
    }

    private static async Task<EduOSDbContext> CreateContextAsync(bool isClassTeacher)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = 10L;
        var context = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>().UseInMemoryDatabase($"exam-workflow-{Guid.NewGuid():N}").EnableServiceProviderCaching(false).Options, accessor);
        var year = new AcademicYear { TenantId = 10, Name = "2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsActive = true };
        var academicClass = new Class { TenantId = 10, Name = "Class 8", IsActive = true };
        context.AddRange(year, academicClass); await context.SaveChangesAsync();
        var section = new Section { TenantId = 10, ClassId = academicClass.Id, Name = "A", Capacity = 40, IsActive = true };
        var math = new Subject { TenantId = 10, ClassId = academicClass.Id, Name = "Mathematics", Code = "MATH", IsActive = true };
        var science = new Subject { TenantId = 10, ClassId = academicClass.Id, Name = "Science", Code = "SCI", IsActive = true };
        var exam = new Exam { TenantId = 10, AcademicYearId = year.Id, Name = "Final", Type = "Final", StartDate = new DateTime(2026, 11, 1), EndDate = new DateTime(2026, 11, 10), IsActive = true };
        var employee = new Employee { TenantId = 10, UserId = 900, EmployeeCode = "T-1", FullName = "Teacher", Phone = "01700000000", DesignationId = 1, JoiningDate = new DateTime(2020, 1, 1), Salary = 1, IsTeacher = true, IsActive = true };
        var student = new Student { TenantId = 10, StudentCode = "S-1", Roll = "1", FullName = "Student", FatherName = "Father", MotherName = "Mother", DOB = new DateTime(2012, 1, 1), Gender = "Male", ClassId = academicClass.Id, SectionId = 1, AcademicYearId = year.Id, AdmissionDate = new DateTime(2026, 1, 1), IsActive = true };
        context.AddRange(section, math, science, exam, employee, student); await context.SaveChangesAsync();
        student.SectionId = section.Id;
        context.ExamSchedules.AddRange(Schedule(exam.Id, academicClass.Id, math.Id), Schedule(exam.Id, academicClass.Id, science.Id));
        context.SubjectTeachers.Add(new SubjectTeacher { TenantId = 10, AcademicYearId = year.Id, ClassId = academicClass.Id, SectionId = section.Id, SubjectId = math.Id, TeacherId = employee.Id, IsClassTeacher = isClassTeacher });
        context.Enrollments.Add(new Enrollment { TenantId = 10, StudentId = student.Id, AcademicYearId = year.Id, ClassId = academicClass.Id, SectionId = section.Id, Roll = "1", EnrollmentDate = new DateTime(2026, 1, 1), IsActive = true });
        await context.SaveChangesAsync();
        return context;
    }

    private static ExamSchedule Schedule(long examId, long classId, long subjectId) => new() { TenantId = 10, ExamId = examId, ClassId = classId, SubjectId = subjectId, ExamDate = new DateTime(2026, 11, 1), StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(10, 0, 0), FullMark = 100, PassMark = 33 };
    private static ExamMarkRosterQueryDto MarkScope(EduOSDbContext context, string subjectCode)
    {
        var examId = context.Exams.IgnoreQueryFilters().Single().Id; var classId = context.Classes.IgnoreQueryFilters().Single().Id; var sectionId = context.Sections.IgnoreQueryFilters().Single().Id; var subjectId = context.Subjects.IgnoreQueryFilters().Single(x => x.Code == subjectCode).Id;
        return new ExamMarkRosterQueryDto { ExamId = examId, ClassId = classId, SectionId = sectionId, SubjectId = subjectId };
    }
    private static ExamScopeDto ResultScope(EduOSDbContext context)
    {
        var mark = MarkScope(context, "MATH"); return new ExamScopeDto { ExamId = mark.ExamId, ClassId = mark.ClassId, SectionId = mark.SectionId };
    }
    private static ExamWorkflowService CreateService(EduOSDbContext context, ICurrentUserService user) => new(
        new GenericRepository<Exam>(context), new GenericRepository<ExamSchedule>(context), new GenericRepository<MarkEntry>(context), new GenericRepository<ExamResult>(context), new GenericRepository<GradeRule>(context), new GenericRepository<Enrollment>(context), new GenericRepository<Section>(context), new GenericRepository<Employee>(context), new GenericRepository<SubjectTeacher>(context), context, user, TimeProvider.System, NullLogger<ExamWorkflowService>.Instance);

    private sealed class TestCurrentUser(long tenantId, long userId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true; public long UserId => userId; public long TenantId => tenantId; public string? FullName => role; public string? Email => "exam@example.test"; public bool IsSuperAdmin => false; public bool IsTenantAdmin => false; public IReadOnlyList<string> Roles => [role]; public bool IsInRole(string value) => string.Equals(value, role, StringComparison.OrdinalIgnoreCase); public string? IpAddress => "127.0.0.1"; public string? UserAgent => "Tests";
    }
}
