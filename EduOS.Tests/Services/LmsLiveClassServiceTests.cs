using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.LMS;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LmsLiveClassServiceTests
{
    [Fact]
    public async Task Schedule_is_idempotent_and_rejects_instructor_overlap()
    {
        await using var db = Db();
        var now = DateTime.UtcNow;
        var from = DateOnly.FromDateTime(now.AddDays(-7));
        var to = DateOnly.FromDateTime(now.AddDays(90));
        var employee = new Employee { TenantId = 101, FullName = "Teacher A", EmployeeCode = "T-01", CanTeach = true, State = EmployeeState.Active };
        var course = new Course { TenantId = 101, Title = "Biology", Code = "BIO-01", AcademicProgramId = 12, SubjectId = 21 };
        var year = new AcademicYear { TenantId = 101, Name = "Current", Code = "2026", StartDate = from, EndDate = to, IsActive = true };
        var curriculum = new AcademicCurriculum { TenantId = 101, Name = "Science", Code = "SC-1", AcademicProgramId = 12, EffectiveFrom = from, IsActive = true };
        db.AddRange(employee, course, year, curriculum);await db.SaveChangesAsync();
        var item = new CurriculumSubject { TenantId = 101, AcademicCurriculumId = curriculum.Id, AcademicLevelId = 31, SubjectId = 21, IsActive = true };
        var batch = new AcademicBatch { TenantId = 101, CampusId = 41, AcademicYearId = year.Id, AcademicProgramId = 12, AcademicLevelId = 31, Name = "B1", Code = "B1", IsActive = true };
        var campus = new EmployeeCampusAssignment { TenantId = 101, EmployeeId = employee.Id, CampusId = 41, EffectiveFrom = from, IsCurrent = true };
        db.AddRange(item,batch,campus);await db.SaveChangesAsync();
        var offering = new SubjectOffering { TenantId = 101, AcademicBatchId = batch.Id, CurriculumSubjectId = item.Id, AcademicYearId = year.Id, Code = "BIO-01", IsActive = true };
        db.Add(offering);await db.SaveChangesAsync();
        db.Add(new InstructorAssignment { TenantId = 101, SubjectOfferingId = offering.Id, EmployeeId = employee.Id, EffectiveFrom = from, IsActive = true });
        await db.SaveChangesAsync();
        var service = Service(db,"TenantAdmin");
        var request = new SaveLiveClassRequestDto { CourseReference = course.PublicId, InstructorReference = employee.PublicId,
            Title = "Cells", StartsAt = now.AddDays(1), EndsAt = now.AddDays(1).AddHours(1), JoinUrl = "https://meet.example.com/room" };
        var first = await service.SaveLiveClassAsync(null, request);
        Assert.True(first.Success, first.Message);
        var repeat = await service.SaveLiveClassAsync(null, request);
        Assert.True(repeat.Success, repeat.Message);
        Assert.Equal(first.Data!.Id, repeat.Data!.Id);
        var overlapping = new SaveLiveClassRequestDto { CourseReference = course.PublicId, InstructorReference = employee.PublicId,
            Title = "Different topic", StartsAt = request.StartsAt.AddMinutes(30), EndsAt = request.EndsAt.AddMinutes(30) };
        Assert.Equal(409,(await service.SaveLiveClassAsync(null,overlapping)).StatusCode);
    }

    [Fact]
    public async Task Student_without_enrollment_cannot_read_class()
    {
        await using var db = Db();
        var course = new Course { TenantId = 101, Code = "MATH", Title = "Mathematics" };
        var employee = new Employee { TenantId = 101, EmployeeCode = "T1", FullName = "Teacher" };
        db.AddRange(course,employee);await db.SaveChangesAsync();
        var row = new LiveClass { TenantId = 101, CourseId = course.Id, InstructorEmployeeId = employee.Id,
            Title = "Session", StartsAt = DateTime.UtcNow.AddHours(2), EndsAt = DateTime.UtcNow.AddHours(3) };
        db.Add(row);await db.SaveChangesAsync();
        Assert.Equal(404,(await Service(db,"Student").GetLiveClassAsync(row.PublicId)).StatusCode);
        Assert.Equal(200,(await Service(db,"TenantAdmin").GetLiveClassAsync(row.PublicId)).StatusCode);
    }

    [Fact]
    public async Task Invalid_dates_and_foreign_tenant_references_are_rejected()
    {
        await using var db = Db();
        var foreign = new Course { TenantId = 202, Code = "PRIVATE", Title = "Other tenant" };
        db.Add(foreign);await db.SaveChangesAsync();
        var service = Service(db,"TenantAdmin");
        var invalid = new SaveLiveClassRequestDto { CourseReference = foreign.PublicId, Title = "Bad",
            StartsAt = DateTime.UtcNow.AddHours(2), EndsAt = DateTime.UtcNow.AddHours(1) };
        Assert.Equal(400,(await service.SaveLiveClassAsync(null,invalid)).StatusCode);
        invalid.EndsAt = invalid.StartsAt.AddHours(1);
        Assert.Equal(404,(await service.SaveLiveClassAsync(null,invalid)).StatusCode);
    }

    private static EduOSDbContext Db()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("live-class-"+Guid.NewGuid().ToString("N")).Options;
        var context = new DefaultHttpContext();
        context.Items["TenantId"] = 101L;
        return new EduOSDbContext(options,new HttpContextAccessor { HttpContext = context });
    }

    private static LmsLiveClassService Service(EduOSDbContext db,string role) => new(
        new GenericRepository<LiveClass>(db),new GenericRepository<Course>(db),
        new GenericRepository<Employee>(db),new GenericRepository<EmployeeCampusAssignment>(db),
        new GenericRepository<InstructorAssignment>(db),new GenericRepository<SubjectOffering>(db),
        new GenericRepository<CurriculumSubject>(db),new GenericRepository<AcademicCurriculum>(db),
        new GenericRepository<AcademicBatch>(db),new GenericRepository<AcademicYear>(db),
        new GenericRepository<AcademicTerm>(db),new GenericRepository<CourseEnrollment>(db),
        new GenericRepository<Student>(db),new GenericRepository<StudentEnrollment>(db),
        db,new TestUser(role),TimeProvider.System,NullLogger<LmsLiveClassService>.Instance);

    private sealed class TestUser(string role):ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 51;
        public long TenantId => 101;
        public string? FullName => "Test";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
