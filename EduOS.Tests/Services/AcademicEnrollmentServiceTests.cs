using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Employees;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Academic;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public class AcademicEnrollmentServiceTests
{
    [Fact]
    public async Task Enrollment_is_retry_safe_snapshots_required_subjects_and_enforces_capacity()
    {
        var options = CreateOptions();
        var seed = await SeedAsync(options, 1);
        await using var context = CreateContext(options, 101, 7, "TenantAdmin");
        var service = CreateService(context, new TestCurrentUser(101, 7, "TenantAdmin"));
        var request = new CreateAcademicStudentEnrollmentDto
        {
            ClientRequestId = Guid.NewGuid(),
            StudentReference = seed.StudentReference,
            AcademicBatchId = seed.BatchId,
            RollNo = " 01 ",
            EnrollmentDate = new DateTime(2026, 9, 21)
        };

        var created = await service.EnrollAsync(request);
        var replay = await service.EnrollAsync(request);
        var capacityConflict = await service.EnrollAsync(new CreateAcademicStudentEnrollmentDto
        {
            ClientRequestId = Guid.NewGuid(),
            StudentReference = seed.OtherStudentReference,
            AcademicBatchId = seed.BatchId,
            RollNo = "02",
            EnrollmentDate = new DateTime(2026, 9, 21)
        });

        created.Success.Should().BeTrue();
        created.StatusCode.Should().Be(201);
        created.Data!.RollNo.Should().Be("01");
        created.Data.Subjects.Should().ContainSingle(x => x.IsRequired && x.Status == SubjectRegistrationStatus.Approved);
        created.Data.Subjects.Single().SubjectCode.Should().Be("MATH");
        created.Data.Subjects.Single().FullMarks.Should().Be(100);
        replay.Success.Should().BeTrue();
        replay.Data!.Id.Should().Be(created.Data.Id);
        capacityConflict.Success.Should().BeFalse();
        capacityConflict.StatusCode.Should().Be(409);
        capacityConflict.Message.Should().Contain("capacity");
        (await context.StudentEnrollments.CountAsync()).Should().Be(1);
        (await context.StudentSubjectRegistrations.CountAsync()).Should().Be(1);
        (await context.StudentSubjectRegistrations.SingleAsync()).DecidedByUserId.Should().Be(7);
    }

    [Fact]
    public async Task Guardian_owned_elective_request_requires_approval_and_controls_timetable()
    {
        var options = CreateOptions();
        var seed = await SeedAsync(options, 10);
        long enrollmentId;
        await using (var managerContext = CreateContext(options, 101, 7, "TenantAdmin"))
        {
            var manager = CreateService(managerContext, new TestCurrentUser(101, 7, "TenantAdmin"));
            var enrollment = await manager.EnrollAsync(new CreateAcademicStudentEnrollmentDto
            {
                ClientRequestId = Guid.NewGuid(),
                StudentReference = seed.StudentReference,
                AcademicBatchId = seed.BatchId,
                RollNo = "01",
                EnrollmentDate = new DateTime(2026, 9, 21)
            });
            enrollmentId = enrollment.Data!.Id;
        }

        await using (var unrelatedContext = CreateContext(options, 101, 73, "Guardian"))
        {
            var unrelated = CreateService(unrelatedContext, new TestCurrentUser(101, 73, "Guardian"));
            var denied = await unrelated.RequestOptionalSubjectAsync(enrollmentId, new RequestOptionalSubjectDto
            {
                ClientRequestId = Guid.NewGuid(),
                SubjectId = seed.OptionalSubjectId
            });
            denied.Success.Should().BeFalse();
            denied.StatusCode.Should().Be(404);
        }

        long registrationId;
        await using (var guardianContext = CreateContext(options, 101, 72, "Guardian"))
        {
            var guardian = CreateService(guardianContext, new TestCurrentUser(101, 72, "Guardian"));
            var request = new RequestOptionalSubjectDto { ClientRequestId = Guid.NewGuid(), SubjectId = seed.OptionalSubjectId, Remarks = "Student choice" };
            var pending = await guardian.RequestOptionalSubjectAsync(enrollmentId, request);
            var replay = await guardian.RequestOptionalSubjectAsync(enrollmentId, request);
            var beforeApproval = await guardian.GetTimetableAsync(seed.StudentReference);

            pending.Success.Should().BeTrue();
            pending.StatusCode.Should().Be(201);
            pending.Data!.Status.Should().Be(SubjectRegistrationStatus.Pending);
            replay.Data!.Id.Should().Be(pending.Data.Id);
            beforeApproval.Data!.Should().ContainSingle(x => x.SubjectId == seed.RequiredSubjectId);
            registrationId = pending.Data.Id;
        }

        await using (var managerContext = CreateContext(options, 101, 7, "TenantAdmin"))
        {
            var registration = await managerContext.StudentSubjectRegistrations.SingleAsync(x => x.Id == registrationId);
            registration.RowVersion = [1, 2, 3, 4, 5, 6, 7, 8];
            await managerContext.SaveChangesAsync();
            var manager = CreateService(managerContext, new TestCurrentUser(101, 7, "TenantAdmin"));
            var stale = await manager.DecideSubjectAsync(registrationId, new DecideSubjectRegistrationDto
            {
                Status = SubjectRegistrationStatus.Approved,
                RowVersion = Convert.ToBase64String([8, 7, 6, 5, 4, 3, 2, 1])
            });
            var approved = await manager.DecideSubjectAsync(registrationId, new DecideSubjectRegistrationDto
            {
                Status = SubjectRegistrationStatus.Approved,
                RowVersion = Convert.ToBase64String(registration.RowVersion),
                Remarks = "Approved elective"
            });

            stale.Success.Should().BeFalse();
            stale.StatusCode.Should().Be(409);
            approved.Success.Should().BeTrue();
            approved.Data!.Status.Should().Be(SubjectRegistrationStatus.Approved);
            (await managerContext.StudentSubjectRegistrations.SingleAsync(x => x.Id == registrationId)).DecidedByUserId.Should().Be(7);
        }

        await using (var guardianContext = CreateContext(options, 101, 72, "Guardian"))
        {
            var guardian = CreateService(guardianContext, new TestCurrentUser(101, 72, "Guardian"));
            var timetable = await guardian.GetTimetableAsync(seed.StudentReference);
            timetable.Success.Should().BeTrue();
            timetable.Data!.Select(x => x.SubjectId).Should().BeEquivalentTo([seed.RequiredSubjectId, seed.OptionalSubjectId]);
        }
    }

    [Fact]
    public async Task Student_and_guardian_reads_hide_other_students_and_tenants()
    {
        var options = CreateOptions();
        var seed = await SeedAsync(options, 10);
        await using (var managerContext = CreateContext(options, 101, 7, "TenantAdmin"))
        {
            var manager = CreateService(managerContext, new TestCurrentUser(101, 7, "TenantAdmin"));
            (await manager.EnrollAsync(new CreateAcademicStudentEnrollmentDto
            {
                ClientRequestId = Guid.NewGuid(),
                StudentReference = seed.StudentReference,
                AcademicBatchId = seed.BatchId,
                RollNo = "01",
                EnrollmentDate = new DateTime(2026, 9, 21)
            })).Success.Should().BeTrue();
        }

        await using (var otherStudentContext = CreateContext(options, 101, 82, "Student"))
        {
            var otherStudent = CreateService(otherStudentContext, new TestCurrentUser(101, 82, "Student"));
            (await otherStudent.GetCurrentAsync(seed.StudentReference)).StatusCode.Should().Be(404);
            (await otherStudent.GetTimetableAsync(seed.StudentReference)).StatusCode.Should().Be(404);
        }

        await SeedTenantShellAsync(options, 202);
        await using var foreignTenantContext = CreateContext(options, 202, 8, "TenantAdmin");
        var foreignTenant = CreateService(foreignTenantContext, new TestCurrentUser(202, 8, "TenantAdmin"));
        var result = await foreignTenant.EnrollAsync(new CreateAcademicStudentEnrollmentDto
        {
            ClientRequestId = Guid.NewGuid(),
            StudentReference = seed.StudentReference,
            AcademicBatchId = seed.BatchId,
            RollNo = "01",
            EnrollmentDate = new DateTime(2026, 9, 21)
        });

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        (await foreignTenantContext.StudentEnrollments.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    private static AcademicEnrollmentService CreateService(EduOSDbContext context, ICurrentUserService currentUser) => new(
        new GenericRepository<StudentEnrollment>(context),
        new GenericRepository<StudentSubjectRegistration>(context),
        new GenericRepository<Student>(context),
        new GenericRepository<Guardian>(context),
        new GenericRepository<AcademicBatch>(context),
        new GenericRepository<AcademicYear>(context),
        new GenericRepository<AcademicCurriculum>(context),
        new GenericRepository<CurriculumSubject>(context),
        new GenericRepository<RoutineEntry>(context),
        new GenericRepository<Room>(context),
        context,
        currentUser,
        TimeProvider.System,
        NullLogger<AcademicEnrollmentService>.Instance);

    private static async Task<SeededAcademic> SeedAsync(DbContextOptions<EduOSDbContext> options, int capacity)
    {
        await using var context = CreateContext(options, 101, 7, "TenantAdmin");
        var campus = new Campus { TenantId = 101, Name = "Main Campus", Code = "MAIN", IsActive = true };
        var year = new AcademicYear { TenantId = 101, Name = "2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsCurrent = true, IsActive = true };
        var legacyClass = new Class { TenantId = 101, Name = "Class Nine", NumericValue = 9, IsActive = true };
        var program = new AcademicProgram { TenantId = 101, Name = "Secondary", Code = "SEC", IsActive = true };
        context.AddRange(campus, year, legacyClass, program);
        await context.SaveChangesAsync();
        program.CampusId = campus.Id;
        var section = new Section { TenantId = 101, ClassId = legacyClass.Id, Name = "A", Capacity = 40, IsActive = true };
        var level = new AcademicLevel { TenantId = 101, AcademicProgramId = program.Id, Name = "Grade Nine", Code = "G9", LevelNo = 9, IsActive = true };
        var required = new Subject { TenantId = 101, Name = "Mathematics", Code = "MATH", DefaultFullMarks = 100, DefaultPassMarks = 33, DefaultCreditHours = 4, IsActive = true };
        var optional = new Subject { TenantId = 101, Name = "Music", Code = "MUS", DefaultFullMarks = 50, DefaultPassMarks = 20, DefaultCreditHours = 2, IsOptional = true, IsActive = true };
        context.AddRange(section, level, required, optional);
        await context.SaveChangesAsync();
        var firstStudent = CreateStudent(101, 71, "S-001", "01", "Student One", year.Id, legacyClass.Id, section.Id);
        var otherStudent = CreateStudent(101, 82, "S-002", "02", "Student Two", year.Id, legacyClass.Id, section.Id);
        var guardian = new Guardian { TenantId = 101, Student = firstStudent, UserId = 72, Name = "Guardian One", Relation = "Mother", Phone = "01700000001", IsPrimary = true };
        var batch = new AcademicBatch { TenantId = 101, CampusId = campus.Id, AcademicYearId = year.Id, AcademicProgramId = program.Id, AcademicLevelId = level.Id, Name = "Grade Nine A", Code = "G9-A", Capacity = capacity, StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsActive = true };
        var curriculum = new AcademicCurriculum { TenantId = 101, AcademicProgramId = program.Id, Name = "Secondary 2026", Code = "SEC-2026", EffectiveFromAcademicYearId = year.Id, IsCurrent = true, IsActive = true };
        context.AddRange(firstStudent, otherStudent, guardian, batch, curriculum);
        await context.SaveChangesAsync();
        var requiredRegistration = new CurriculumSubject { TenantId = 101, AcademicCurriculumId = curriculum.Id, AcademicLevelId = level.Id, SubjectId = required.Id, FullMarks = 100, PassMarks = 33, CreditHours = 4, IsActive = true };
        var optionalRegistration = new CurriculumSubject { TenantId = 101, AcademicCurriculumId = curriculum.Id, AcademicLevelId = level.Id, SubjectId = optional.Id, FullMarks = 50, PassMarks = 20, CreditHours = 2, IsOptional = true, IsActive = true };
        var teacher = new Employee { TenantId = 101, EmployeeCode = "T-001", FullName = "Teacher One", Phone = "01700000002", DesignationId = 1, JoiningDate = new DateTime(2020, 1, 1), IsTeacher = true, IsActive = true };
        var slot = new RoutineTimeSlot { TenantId = 101, Name = "Period 1", StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(9, 45, 0), IsActive = true };
        context.AddRange(requiredRegistration, optionalRegistration, teacher, slot);
        await context.SaveChangesAsync();
        context.RoutineEntries.AddRange(
            new RoutineEntry { TenantId = 101, AcademicBatchId = batch.Id, AcademicYearId = year.Id, SubjectId = required.Id, EmployeeId = teacher.Id, RoutineTimeSlotId = slot.Id, DayOfWeek = DayOfWeek.Sunday, IsActive = true },
            new RoutineEntry { TenantId = 101, AcademicBatchId = batch.Id, AcademicYearId = year.Id, SubjectId = optional.Id, EmployeeId = teacher.Id, RoutineTimeSlotId = slot.Id, DayOfWeek = DayOfWeek.Monday, IsActive = true });
        await context.SaveChangesAsync();
        return new SeededAcademic(firstStudent.PublicId, otherStudent.PublicId, batch.Id, required.Id, optional.Id);
    }

    private static Student CreateStudent(long tenantId, long userId, string code, string roll, string name, long yearId, long classId, long sectionId) => new()
    {
        TenantId = tenantId,
        UserId = userId,
        StudentCode = code,
        Roll = roll,
        FullName = name,
        FatherName = "Father",
        MotherName = "Mother",
        DOB = new DateTime(2011, 1, 1),
        Gender = "Other",
        ClassId = classId,
        SectionId = sectionId,
        AcademicYearId = yearId,
        AdmissionDate = new DateTime(2026, 1, 1),
        Status = "Active",
        IsActive = true
    };

    private static async Task SeedTenantShellAsync(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        await using var context = CreateContext(options, tenantId, tenantId, "TenantAdmin");
        context.AcademicYears.Add(new AcademicYear { TenantId = tenantId, Name = "2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsCurrent = true, IsActive = true });
        await context.SaveChangesAsync();
    }

    private static DbContextOptions<EduOSDbContext> CreateOptions() => new DbContextOptionsBuilder<EduOSDbContext>()
        .UseInMemoryDatabase($"academic-enrollment-{Guid.NewGuid():N}").Options;

    private static EduOSDbContext CreateContext(DbContextOptions<EduOSDbContext> options, long tenantId, long userId, string role)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role), new Claim("TenantId", tenantId.ToString())
        ], "TestAuthentication")) };
        http.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed record SeededAcademic(Guid StudentReference, Guid OtherStudentReference, long BatchId, long RequiredSubjectId, long OptionalSubjectId);

    private sealed class TestCurrentUser(long tenantId, long userId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => "Academic User";
        public string? Email => "academic@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "EduOS tests";
    }
}
