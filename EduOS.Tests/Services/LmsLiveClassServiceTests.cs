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
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LmsLiveClassServiceTests
{
    [Fact]
    public async Task Schedule_is_idempotent_and_rejects_instructor_overlap()
    {
        var databaseName = "live-class-" + Guid.NewGuid().ToString("N");
        await using var db = Db(101, databaseName);
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
        // A completed enrollment locks all new and existing live-class authoring.
        db.Add(new CourseEnrollment { TenantId = 101, CourseId = course.Id, StudentId = 901,
            State = CourseEnrollmentState.Completed, CompletedAt = now });
        await db.SaveChangesAsync();
        var additional = new SaveLiveClassRequestDto { CourseReference = course.PublicId,
            InstructorReference = employee.PublicId, Title = "New session",
            StartsAt = request.StartsAt.AddHours(3), EndsAt = request.EndsAt.AddHours(3) };
        var blockedCreate = await service.SaveLiveClassAsync(null, additional);
        Assert.Equal(409, blockedCreate.StatusCode);
        Assert.Contains("locked", blockedCreate.Message, StringComparison.OrdinalIgnoreCase);
        var existing = await db.Set<LiveClass>().SingleAsync();
        existing.RowVersion = [1, 2, 3]; await db.SaveChangesAsync();
        var update = new SaveLiveClassRequestDto { CourseReference = course.PublicId,
            InstructorReference = employee.PublicId, Title = "Edited session",
            StartsAt = request.StartsAt, EndsAt = request.EndsAt,
            RowVersion = Convert.ToBase64String(existing.RowVersion) };
        var blockedUpdate = await service.SaveLiveClassAsync(existing.PublicId, update);
        Assert.Equal(409, blockedUpdate.StatusCode);
        Assert.Contains("locked", blockedUpdate.Message, StringComparison.OrdinalIgnoreCase);
        var blockedCancel = await service.CancelLiveClassAsync(existing.PublicId,
            Convert.ToBase64String(existing.RowVersion));
        Assert.Equal(409, blockedCancel.StatusCode);
        Assert.Contains("locked", blockedCancel.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await db.Set<LiveClass>().Where(x => !x.IsDeleted).ToListAsync());
        // Replaying an identical create must not mutate completed-course evidence.
        Assert.Equal(existing.Id, (await service.SaveLiveClassAsync(null, request)).Data?.Id);
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
        var databaseName = "live-class-" + Guid.NewGuid().ToString("N");
        await using var db = Db(101, databaseName);
        var foreign = new Course { TenantId = 202, Code = "PRIVATE", Title = "Other tenant" };
        await using (var otherTenant = Db(202, databaseName))
        {
            otherTenant.Add(foreign);
            await otherTenant.SaveChangesAsync();
        }
        var service = Service(db,"TenantAdmin");
        var invalid = new SaveLiveClassRequestDto { CourseReference = foreign.PublicId, Title = "Bad",
            StartsAt = DateTime.UtcNow.AddHours(2), EndsAt = DateTime.UtcNow.AddHours(1) };
        Assert.Equal(400,(await service.SaveLiveClassAsync(null,invalid)).StatusCode);
        invalid.EndsAt = invalid.StartsAt.AddHours(1);
        Assert.Equal(404,(await service.SaveLiveClassAsync(null,invalid)).StatusCode);
    }

    [Fact]
    public async Task Completed_student_retains_historical_recording_after_promotion_but_not_foreign_campus()
    {
        await using var db = Db();
        var now = DateTime.UtcNow;
        var from = DateOnly.FromDateTime(now.AddDays(-180));
        var to = DateOnly.FromDateTime(now.AddDays(-20));
        var teacher = new Employee { TenantId = 101, EmployeeCode = "HIST-T", FullName = "Historical teacher" };
        var student = new Student { TenantId = 101, UserId = 51, StudentCode = "HIST-S", FullName = "Student" };
        var course = new Course { TenantId = 101, AcademicProgramId = 12, SubjectId = 21,
            Code = "HIST-C", Title = "Historical course" };
        var year = new AcademicYear { TenantId = 101, Name = "Previous", Code = "OLD",
            StartDate = from, EndDate = to, IsActive = false };
        var curriculum = new AcademicCurriculum { TenantId = 101, Name = "Previous curriculum",
            Code = "OLD-C", AcademicProgramId = 12, EffectiveFrom = from, IsActive = false };
        db.AddRange(teacher, student, course, year, curriculum);
        await db.SaveChangesAsync();
        var item = new CurriculumSubject { TenantId = 101, AcademicCurriculumId = curriculum.Id,
            AcademicLevelId = 31, SubjectId = 21, IsActive = false };
        var batch = new AcademicBatch { TenantId = 101, CampusId = 41,
            AcademicYearId = year.Id, AcademicProgramId = 12, AcademicLevelId = 31,
            Name = "Old batch", Code = "OLD-B", IsActive = false };
        var campus = new EmployeeCampusAssignment { TenantId = 101, EmployeeId = teacher.Id,
            CampusId = 41, EffectiveFrom = from, IsCurrent = false };
        db.AddRange(item, batch, campus);
        await db.SaveChangesAsync();
        var offering = new SubjectOffering { TenantId = 101, AcademicBatchId = batch.Id,
            AcademicYearId = year.Id, CurriculumSubjectId = item.Id, Code = "OLD-O", IsActive = false };
        var academic = new StudentEnrollment { TenantId = 101, StudentId = student.Id,
            CampusId = 41, AcademicYearId = year.Id, AcademicProgramId = 12, AcademicLevelId = 31,
            AcademicBatchId = batch.Id, AcademicCurriculumId = curriculum.Id,
            RollNo = "001", EnrollmentDate = from, EndDate = to,
            State = EnrollmentState.Promoted, IsCurrent = false };
        db.AddRange(offering, academic);
        await db.SaveChangesAsync();
        db.Add(new InstructorAssignment { TenantId = 101, SubjectOfferingId = offering.Id,
            EmployeeId = teacher.Id, EffectiveFrom = from, IsActive = false });
        db.Add(new CourseEnrollment { TenantId = 101, CourseId = course.Id,
            StudentId = student.Id, StudentEnrollmentId = academic.Id,
            State = CourseEnrollmentState.Completed, EnrolledAt = now.AddDays(-120),
            CompletedAt = now.AddDays(-10) });
        var session = new LiveClass { TenantId = 101, CourseId = course.Id,
            InstructorEmployeeId = teacher.Id, Title = "Archived class",
            StartsAt = now.AddDays(-90), EndsAt = now.AddDays(-90).AddHours(1),
            JoinUrl = "https://meet.example.com/old",
            RecordingUrl = "https://videos.example.com/old" };
        db.Add(session);
        await db.SaveChangesAsync();

        var service = Service(db, "Student");
        var detail = await service.GetLiveClassAsync(session.PublicId);
        Assert.True(detail.Success, detail.Message);
        Assert.Null(detail.Data!.JoinUrl);
        Assert.Equal(session.RecordingUrl, detail.Data.RecordingUrl);
        var register = await service.GetCourseRecordingsAsync(course.PublicId);
        Assert.True(register.Success, register.Message);
        Assert.Equal(1, register.Data!.TotalCount);
        Assert.Single(register.Data.Items);
        Assert.Equal(session.PublicId, register.Data.Items[0].Reference);
        Assert.Equal(session.RecordingUrl, register.Data.Items[0].RecordingUrl);
        Assert.Empty((await service.GetCourseLiveClassesAsync(course.PublicId)).Data!);

        // A historical course enrollment is not a bypass for a different campus.
        academic.CampusId = 42;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await service.GetLiveClassAsync(session.PublicId)).StatusCode);
        Assert.Equal(0, (await service.GetCourseRecordingsAsync(course.PublicId)).Data!.TotalCount);
    }

    [Fact]
    public async Task Recording_register_validates_pagination_and_does_not_disclose_foreign_courses()
    {
        var databaseName = "live-class-" + Guid.NewGuid().ToString("N");
        await using var db = Db(101, databaseName);
        var foreign = new Course { TenantId = 202, Code = "OTHER", Title = "Foreign course" };
        await using (var otherTenant = Db(202, databaseName))
        {
            otherTenant.Add(foreign);
            await otherTenant.SaveChangesAsync();
        }
        var service = Service(db, "Student");
        Assert.Equal(400, (await service.GetCourseRecordingsAsync(foreign.PublicId, 0, 20)).StatusCode);
        Assert.Equal(400, (await service.GetCourseRecordingsAsync(foreign.PublicId, 1, 100)).StatusCode);
        Assert.Equal(404, (await service.GetCourseRecordingsAsync(foreign.PublicId, 1, 20)).StatusCode);
    }


    [Fact]
    public async Task Post_session_recording_publication_is_append_only_idempotent_and_completion_safe()
    {
        await using var db = Db();
        var now = DateTime.UtcNow;
        var from = DateOnly.FromDateTime(now.AddDays(-30));
        var to = DateOnly.FromDateTime(now.AddDays(90));
        var teacher = new Employee { TenantId = 101, EmployeeCode = "PUB-T", FullName = "Teacher", CanTeach = true, State = EmployeeState.Active };
        var course = new Course { TenantId = 101, AcademicProgramId = 12, SubjectId = 21, Code = "PUB-C", Title = "Course" };
        var year = new AcademicYear { TenantId = 101, Code = "PUB-Y", Name = "Year", StartDate = from, EndDate = to, IsActive = true };
        var curriculum = new AcademicCurriculum { TenantId = 101, Code = "PUB-CUR", Name = "Curriculum", AcademicProgramId = 12, EffectiveFrom = from, IsActive = true };
        db.AddRange(teacher, course, year, curriculum);
        await db.SaveChangesAsync();
        var item = new CurriculumSubject { TenantId = 101, AcademicCurriculumId = curriculum.Id, AcademicLevelId = 31, SubjectId = 21, IsActive = true };
        var batch = new AcademicBatch { TenantId = 101, CampusId = 41, AcademicYearId = year.Id,
            AcademicProgramId = 12, AcademicLevelId = 31, Code = "PUB-B", Name = "Batch", IsActive = true };
        var campus = new EmployeeCampusAssignment { TenantId = 101, EmployeeId = teacher.Id,
            CampusId = 41, EffectiveFrom = from, IsCurrent = true };
        db.AddRange(item, batch, campus);
        await db.SaveChangesAsync();
        var offering = new SubjectOffering { TenantId = 101, AcademicBatchId = batch.Id,
            AcademicYearId = year.Id, CurriculumSubjectId = item.Id, Code = "PUB-O", IsActive = true };
        db.Add(offering);
        await db.SaveChangesAsync();
        db.Add(new InstructorAssignment { TenantId = 101, SubjectOfferingId = offering.Id,
            EmployeeId = teacher.Id, EffectiveFrom = from, IsActive = true });
        var past = new LiveClass { TenantId = 101, CourseId = course.Id, InstructorEmployeeId = teacher.Id,
            Title = "Finished session", StartsAt = now.AddDays(-1), EndsAt = now.AddDays(-1).AddHours(1),
            RowVersion = [1, 2, 3] };
        var second = new LiveClass { TenantId = 101, CourseId = course.Id, InstructorEmployeeId = teacher.Id,
            Title = "Second session", StartsAt = now.AddDays(-2), EndsAt = now.AddDays(-2).AddHours(1),
            RowVersion = [4, 5, 6] };
        db.AddRange(past, second);
        await db.SaveChangesAsync();
        var service = Service(db, "TenantAdmin");
        var pending = await service.GetPendingRecordingsAsync(course.PublicId);
        Assert.True(pending.Success, pending.Message);
        Assert.Equal(2, pending.Data!.TotalCount);
        Assert.Equal(400, (await service.GetPendingRecordingsAsync(course.PublicId, 0)).StatusCode);
        Assert.Equal(403, (await Service(db, "Student").GetPendingRecordingsAsync(course.PublicId)).StatusCode);
        var request = new PublishLiveClassRecordingRequestDto {
            RecordingUrl = "https://videos.example.com/lesson", RowVersion = Convert.ToBase64String(past.RowVersion) };
        Assert.Equal(409, (await service.PublishRecordingAsync(past.PublicId,
            new PublishLiveClassRecordingRequestDto { RecordingUrl = request.RecordingUrl,
                RowVersion = Convert.ToBase64String(new byte[] { 99 }) })).StatusCode);
        var future = new LiveClass { TenantId = 101, CourseId = course.Id, InstructorEmployeeId = teacher.Id,
            Title = "Upcoming session", StartsAt = now.AddDays(1), EndsAt = now.AddDays(1).AddHours(1),
            RowVersion = [7, 8, 9] };
        db.Add(future);
        await db.SaveChangesAsync();
        Assert.Equal(409, (await service.PublishRecordingAsync(future.PublicId,
            new PublishLiveClassRecordingRequestDto { RecordingUrl = request.RecordingUrl,
                RowVersion = Convert.ToBase64String(future.RowVersion) })).StatusCode);
        var published = await service.PublishRecordingAsync(past.PublicId, request);
        Assert.True(published.Success, published.Message);
        Assert.Equal(request.RecordingUrl, published.Data!.RecordingUrl);
        Assert.True((await service.PublishRecordingAsync(past.PublicId, request)).Success);
        Assert.Equal(409, (await service.PublishRecordingAsync(past.PublicId,
            new PublishLiveClassRecordingRequestDto { RecordingUrl = "https://videos.example.com/other",
                RowVersion = request.RowVersion })).StatusCode);
        Assert.Equal(400, (await service.PublishRecordingAsync(second.PublicId,
            new PublishLiveClassRecordingRequestDto { RecordingUrl = "http://unsafe.example.com",
                RowVersion = Convert.ToBase64String(second.RowVersion) })).StatusCode);
        Assert.Equal(403, (await Service(db, "Student").PublishRecordingAsync(second.PublicId, request)).StatusCode);
        Assert.Equal(1, (await service.GetPendingRecordingsAsync(course.PublicId)).Data!.TotalCount);
        db.Add(new CourseEnrollment { TenantId = 101, CourseId = course.Id, StudentId = 901,
            State = CourseEnrollmentState.Completed, CompletedAt = now });
        await db.SaveChangesAsync();
        var locked = await service.PublishRecordingAsync(second.PublicId,
            new PublishLiveClassRecordingRequestDto {
                RecordingUrl = "https://videos.example.com/second",
                RowVersion = Convert.ToBase64String(second.RowVersion) });
        Assert.Equal(409, locked.StatusCode);
        Assert.Contains("locked", locked.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True((await service.PublishRecordingAsync(past.PublicId, request)).Success);
        Assert.Null((await db.Set<LiveClass>().SingleAsync(x => x.Id == second.Id)).RecordingUrl);
    }

    private static readonly InMemoryDatabaseRoot DatabaseRoot = new();

    private static EduOSDbContext Db(long tenantId = 101, string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase(databaseName ?? "live-class-" + Guid.NewGuid().ToString("N"), DatabaseRoot).Options;
        var context = new DefaultHttpContext();
        context.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = context });
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
