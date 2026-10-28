using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.AssessmentAdministration;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Exercises the real assessment service against disposable SQL Server, including
/// instructor ownership, reviewer separation, rejected-mark correction, replay,
/// optimistic versions and frozen approved evidence. No production records.
/// </summary>
[Collection("SqlServerIntegration")]
public sealed class SqlServerAssessmentMarkReviewLifecycleIntegrationTests
{
    [Fact]
    public async Task Teacher_submission_rejection_correction_resubmission_and_independent_approval_are_atomic()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var seeded = await SeedAsync(options);
        var tenant = seeded.TenantId;
        var subjectId = seeded.AssessmentSubjectId;
        var teacher = new Actor(tenant, seeded.TeacherId, "Teacher");
        var reviewer = new Actor(tenant, seeded.ReviewerId, "TenantAdmin");
        var stranger = new Actor(tenant, seeded.UnassignedId, "Teacher");

        await using (var db = TenantDb(options, tenant))
        {
            var unassigned = await Service(db, stranger).GetMarkReviewAsync(subjectId);
            Assert.Equal(403, unassigned.StatusCode);
        }
        await using (var db = TenantDb(options, tenant + 1))
        {
            var foreign = await Service(db, new Actor(tenant + 1, seeded.ReviewerId, "TenantAdmin"))
                .GetMarkReviewAsync(subjectId);
            Assert.Equal(404, foreign.StatusCode);
        }

        var submissionKey = Guid.NewGuid();
        string initialAssessmentVersion;
        await using (var db = TenantDb(options, tenant))
        {
            var service = Service(db, teacher);
            var before = await service.GetMarkReviewAsync(subjectId);
            Assert.True(before.Success, before.Message);
            Assert.True(before.Data!.CanSubmit);
            initialAssessmentVersion = before.Data.AssessmentRowVersion;
            var submitted = await service.DecideMarkReviewAsync(subjectId,
                Decision(submissionKey, initialAssessmentVersion, AssessmentMarkReviewDecision.Submitted));
            Assert.True(submitted.Success, submitted.Message);
            Assert.Equal(AssessmentMarkReviewDecision.Submitted, submitted.Data!.CurrentDecision);
            Assert.False(submitted.Data.CanSubmit);
        }
        await using (var db = TenantDb(options, tenant))
        {
            var service = Service(db, teacher);
            var replay = await service.DecideMarkReviewAsync(subjectId,
                Decision(submissionKey, initialAssessmentVersion, AssessmentMarkReviewDecision.Submitted));
            Assert.True(replay.Success, replay.Message);
            var changedPayload = await service.DecideMarkReviewAsync(subjectId,
                Decision(submissionKey, initialAssessmentVersion, AssessmentMarkReviewDecision.Rejected, "Altered"));
            // Authorization rejects a teacher trying to forge a reviewer decision before replay checks.
            Assert.Equal(403, changedPayload.StatusCode);
            var mark = await db.StudentAssessmentMarks.AsNoTracking()
                .SingleAsync(x => x.AssessmentSubjectId == subjectId);
            var blocked = await service.SaveMarksRegisterAsync(MarkChange(
                subjectId, seeded.RegistrationId, mark, 80m));
            Assert.Equal(409, blocked.StatusCode);
            var selfApproval = await service.DecideMarkReviewAsync(subjectId,
                Decision(Guid.NewGuid(), (await service.GetMarkReviewAsync(subjectId)).Data!
                    .AssessmentRowVersion, AssessmentMarkReviewDecision.Approved));
            Assert.Equal(403, selfApproval.StatusCode);
        }
        await using (var db = TenantDb(options, tenant))
        {
            var service = Service(db, reviewer);
            var pending = await service.GetMarkReviewAsync(subjectId);
            Assert.True(pending.Success, pending.Message);
            Assert.True(pending.Data!.CanReview);
            var reusedTeacherKey = await service.DecideMarkReviewAsync(subjectId,
                Decision(submissionKey, pending.Data.AssessmentRowVersion,
                    AssessmentMarkReviewDecision.Rejected, "Forged review"));
            Assert.Equal(409, reusedTeacherKey.StatusCode);
            var rejected = await service.DecideMarkReviewAsync(subjectId,
                Decision(Guid.NewGuid(), pending.Data.AssessmentRowVersion,
                    AssessmentMarkReviewDecision.Rejected, "Component totals require checking"));
            Assert.True(rejected.Success, rejected.Message);
            Assert.Equal(AssessmentMarkReviewDecision.Rejected, rejected.Data!.CurrentDecision);
        }
        await using (var db = TenantDb(options, tenant))
        {
            var service = Service(db, teacher);
            var mark = await db.StudentAssessmentMarks.AsNoTracking()
                .SingleAsync(x => x.AssessmentSubjectId == subjectId);
            var corrected = await service.SaveMarksRegisterAsync(
                MarkChange(subjectId, seeded.RegistrationId, mark, 80m));
            Assert.True(corrected.Success, corrected.Message);
        }
        var resubmissionKey = Guid.NewGuid();
        await using (var db = TenantDb(options, tenant))
        {
            var service = Service(db, teacher);
            var correctedReview = await service.GetMarkReviewAsync(subjectId);
            Assert.True(correctedReview.Data!.CanSubmit);
            var resubmitted = await service.DecideMarkReviewAsync(subjectId,
                Decision(resubmissionKey, correctedReview.Data.AssessmentRowVersion,
                    AssessmentMarkReviewDecision.Submitted));
            Assert.True(resubmitted.Success, resubmitted.Message);
            Assert.Equal(AssessmentMarkReviewDecision.Submitted, resubmitted.Data!.CurrentDecision);
            // Old request keys cannot acknowledge a superseded decision.
            var obsolete = await service.DecideMarkReviewAsync(subjectId,
                Decision(submissionKey, initialAssessmentVersion, AssessmentMarkReviewDecision.Submitted));
            Assert.Equal(409, obsolete.StatusCode);
        }
        await using (var db = TenantDb(options, tenant))
        {
            var service = Service(db, reviewer);
            var pending = await service.GetMarkReviewAsync(subjectId);
            Assert.True(pending.Data!.CanReview);
            var stale = await service.DecideMarkReviewAsync(subjectId,
                Decision(Guid.NewGuid(), initialAssessmentVersion, AssessmentMarkReviewDecision.Approved));
            Assert.Equal(409, stale.StatusCode);
            var approved = await service.DecideMarkReviewAsync(subjectId,
                Decision(Guid.NewGuid(), pending.Data.AssessmentRowVersion,
                    AssessmentMarkReviewDecision.Approved));
            Assert.True(approved.Success, approved.Message);
            Assert.Equal(AssessmentMarkReviewDecision.Approved, approved.Data!.CurrentDecision);
        }
        await using (var db = TenantDb(options, tenant))
        {
            var service = Service(db, teacher);
            var mark = await db.StudentAssessmentMarks.AsNoTracking()
                .SingleAsync(x => x.AssessmentSubjectId == subjectId);
            var frozen = await service.SaveMarksRegisterAsync(
                MarkChange(subjectId, seeded.RegistrationId, mark, 90m));
            Assert.Equal(409, frozen.StatusCode);
            var status = await service.GetMarkReviewAsync(subjectId);
            Assert.False(status.Data!.CanSubmit);
            Assert.False(status.Data.CanReview);
        }
        await using (var db = TenantDb(options, tenant))
        {
            var decisions = await db.AssessmentMarkReviewEvents.AsNoTracking()
                .Where(x => x.TenantId == tenant && x.AssessmentSubjectId == subjectId)
                .OrderBy(x => x.Id).Select(x => x.Decision).ToArrayAsync();
            Assert.Equal(new[] {
                AssessmentMarkReviewDecision.Submitted, AssessmentMarkReviewDecision.Rejected,
                AssessmentMarkReviewDecision.Submitted, AssessmentMarkReviewDecision.Approved
            }, decisions);
            Assert.Equal(80m, (await db.StudentAssessmentMarks.AsNoTracking()
                .SingleAsync(x => x.AssessmentSubjectId == subjectId)).ObtainedMarks);
            Assert.Equal(4, await db.AssessmentMarkReviewEvents.AsNoTracking()
                .CountAsync(x => x.AssessmentSubjectId == subjectId));
        }
    }

    private static AssessmentMarkReviewRequestDto Decision(Guid key, string version,
        AssessmentMarkReviewDecision decision, string? reason = null) => new()
    {
        ClientRequestId = key, AssessmentRowVersion = version,
        Decision = decision, Reason = reason
    };

    private static SaveMarksRegisterRequestDto MarkChange(long subjectId, long registrationId,
        StudentAssessmentMark mark, decimal marks) => new()
    {
        AssessmentSubjectId = subjectId,
        Marks = [new SaveStudentAssessmentMarkRequestDto
        {
            StudentSubjectRegistrationId = registrationId, ObtainedMarks = marks,
            RowVersion = Convert.ToBase64String(mark.RowVersion)
        }]
    };

    private static AssessmentAdministrationService Service(EduOSDbContext db, Actor actor)
    {
        GenericRepository<T> Repo<T>() where T : class => new(db);
        return new AssessmentAdministrationService(
            Repo<Assessment>(), Repo<AssessmentSubject>(), Repo<AssessmentMarkReviewEvent>(),
            Repo<AssessmentSchedule>(), Repo<StudentAssessmentMark>(), Repo<ResultPublication>(),
            Repo<StudentResultSummary>(), Repo<GradeScheme>(), Repo<GradeRule>(),
            Repo<StudentEnrollment>(), Repo<Subject>(), Repo<StudentSubjectRegistration>(),
            Repo<SubjectOffering>(), Repo<CurriculumSubject>(), Repo<AcademicBatch>(),
            Repo<AcademicYear>(), Repo<AcademicTerm>(), Repo<Campus>(), Repo<UserCampusAccess>(),
            Repo<Room>(), Repo<Student>(), Repo<Employee>(), Repo<InstructorAssignment>(),
            Repo<AssessmentComponent>(), Repo<StudentAssessmentComponentMark>(),
            Repo<CertificateTemplate>(), Repo<CertificateIssue>(), Repo<TranscriptIssue>(),
            Repo<EduOS.Core.Entities.System.AuditLog>(), db, actor, TimeProvider.System,
            NullLogger<AssessmentAdministrationService>.Instance);
    }

    private static EduOSDbContext TenantDb(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, accessor);
    }

    private static async Task<Seeded> SeedAsync(DbContextOptions<EduOSDbContext> options)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var setup = new EduOSDbContext(options);
        var tenant = new Tenant { Code = "CI-EXFLOW-" + suffix, Name = "Review lifecycle",
            Email = "exflow-" + suffix + "@example.test", State = TenantState.Active };
        ApplicationUser User(string role) {
            var name = "exflow-" + role + "-" + suffix + "@example.test";
            return new ApplicationUser { UserName = name,
                NormalizedUserName = name.ToUpperInvariant(), Email = name,
                NormalizedEmail = name.ToUpperInvariant(), FullName = role,
                IsActive = true, EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString(),
                ConcurrencyStamp = Guid.NewGuid().ToString() };
        }
        var teacher = User("teacher");
        var reviewer = User("reviewer");
        var unassigned = User("unassigned");
        var teacherPerson = new Person { FullName = "Synthetic teacher" };
        var studentPerson = new Person { FullName = "Synthetic student" };
        setup.AddRange(tenant, teacher, reviewer, unassigned, teacherPerson, studentPerson);
        await setup.SaveChangesAsync();
        var tenantId = tenant.Id;
        setup.ChangeTracker.Clear();
        using (setup.BeginSystemTenantScope(tenantId))
        {
            var campus = new Campus { TenantId = tenantId, Name = "Main", Code = "MAIN" };
            var year = new AcademicYear { TenantId = tenantId, Name = "2026", Code = "2026",
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
            var program = new AcademicProgram { TenantId = tenantId, Name = "School", Code = "SCH" };
            var subject = new Subject { TenantId = tenantId, Name = "Math", Code = "MATH" };
            var designation = new Designation { TenantId = tenantId, Name = "Teacher", Code = "TCHR" };
            var student = new Student { TenantId = tenantId, PersonId = studentPerson.Id,
                FullName = "Synthetic student", StudentCode = "S-" + suffix,
                StatusCode = "Active", AdmissionDate = new DateOnly(2026, 1, 1) };
            setup.AddRange(campus, year, program, subject, designation, student);
            await setup.SaveChangesAsync();
            var employee = new Employee { TenantId = tenantId, PersonId = teacherPerson.Id,
                UserId = teacher.Id, DesignationId = designation.Id, EmployeeCode = "T-" + suffix,
                FullName = "Synthetic teacher", CanTeach = true, State = EmployeeState.Active,
                JoiningDate = new DateOnly(2026, 1, 1) };
            var level = new AcademicLevel { TenantId = tenantId, AcademicProgramId = program.Id,
                Name = "One", Code = "ONE", LevelNo = 1 };
            var curriculum = new AcademicCurriculum { TenantId = tenantId,
                AcademicProgramId = program.Id, Name = "Curriculum", Code = "CUR",
                EffectiveFrom = new DateOnly(2026, 1, 1), IsCurrent = true };
            setup.AddRange(employee, level, curriculum);
            await setup.SaveChangesAsync();
            var batch = new AcademicBatch { TenantId = tenantId, CampusId = campus.Id,
                AcademicYearId = year.Id, AcademicProgramId = program.Id,
                AcademicLevelId = level.Id, Name = "Class One", Code = "C1" };
            var curriculumSubject = new CurriculumSubject { TenantId = tenantId,
                AcademicCurriculumId = curriculum.Id, AcademicLevelId = level.Id,
                SubjectId = subject.Id };
            setup.AddRange(batch, curriculumSubject);
            await setup.SaveChangesAsync();
            var offering = new SubjectOffering { TenantId = tenantId,
                AcademicBatchId = batch.Id, AcademicYearId = year.Id,
                CurriculumSubjectId = curriculumSubject.Id, Code = "MATH" };
            var exam = new Assessment { TenantId = tenantId, CampusId = campus.Id,
                AcademicYearId = year.Id, Name = "Final", Code = "FINAL",
                StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 2),
                State = AssessmentState.MarksEntry };
            var enrollment = new StudentEnrollment { TenantId = tenantId, StudentId = student.Id,
                AcademicYearId = year.Id, CampusId = campus.Id,
                AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                AcademicBatchId = batch.Id, AcademicCurriculumId = curriculum.Id,
                RollNo = "01", EnrollmentDate = new DateOnly(2026, 1, 1),
                State = EnrollmentState.Active, IsCurrent = true, ClientRequestId = Guid.NewGuid() };
            setup.AddRange(offering, exam, enrollment);
            await setup.SaveChangesAsync();
            var assessmentSubject = new AssessmentSubject { TenantId = tenantId,
                AssessmentId = exam.Id, SubjectOfferingId = offering.Id };
            var registration = new StudentSubjectRegistration { TenantId = tenantId,
                StudentEnrollmentId = enrollment.Id, SubjectOfferingId = offering.Id,
                ClientRequestId = Guid.NewGuid(), State = SubjectRegistrationState.Approved };
            var assignment = new InstructorAssignment { TenantId = tenantId,
                SubjectOfferingId = offering.Id, EmployeeId = employee.Id,
                EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true };
            setup.AddRange(assessmentSubject, registration, assignment);
            await setup.SaveChangesAsync();
            var mark = new StudentAssessmentMark { TenantId = tenantId,
                AssessmentSubjectId = assessmentSubject.Id,
                StudentSubjectRegistrationId = registration.Id, ObtainedMarks = 75m,
                EnteredByUserId = teacher.Id, EnteredAt = DateTime.UtcNow };
            setup.StudentAssessmentMarks.Add(mark);
            await setup.SaveChangesAsync();
            return new Seeded(tenantId, teacher.Id, reviewer.Id, unassigned.Id,
                assessmentSubject.Id, registration.Id);
        }
    }

    private sealed record Seeded(long TenantId, long TeacherId, long ReviewerId,
        long UnassignedId, long AssessmentSubjectId, long RegistrationId);

    private sealed class Actor(long tenantId, long userId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => "SQL exam test";
        public string? Email => "sql-exam@example.test";
        public bool IsTenantAdmin => role == "TenantAdmin";
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "SQL exam lifecycle";
    }
}
