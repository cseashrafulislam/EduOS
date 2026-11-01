using EduOS.Core.DTOs.Alumni;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Alumni;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerCareerPlacementIntegrationTests
{
    [Fact]
    public async Task Employer_job_application_independent_placement_and_report_are_atomic_and_scoped()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(connection).Options;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        long tenantId, otherTenantId, campusId, makerId, checkerId, studentId;
        Guid studentReference;
        await using (var setup = new EduOSDbContext(options))
        {
            var tenant = new Tenant { Code = "CI-CJ-" + suffix, Name = "Placement SQL CI",
                Email = "career-job-" + suffix + "@example.test", State = TenantState.Active };
            var other = new Tenant { Code = "CI-CY-" + suffix, Name = "Other placement tenant",
                Email = "career-other-" + suffix + "@example.test", State = TenantState.Active };
            var maker = User("career-job-maker-" + suffix);
            var checker = User("career-job-checker-" + suffix);
            var studentUser = User("career-job-student-" + suffix);
            var person = new Person { FullName = "Student Candidate" };
            setup.AddRange(tenant, other, maker, checker, studentUser, person);
            await setup.SaveChangesAsync();
            tenantId = tenant.Id; otherTenantId = other.Id;
            makerId = maker.Id; checkerId = checker.Id; studentId = studentUser.Id;
            setup.ChangeTracker.Clear();
            using (setup.BeginSystemTenantScope(tenantId))
            {
                var campus = new Campus { TenantId = tenantId, Code = "MAIN", Name = "Main Campus" };
                var year = new AcademicYear { TenantId = tenantId, Code = "2026", Name = "2026",
                    StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
                var program = new AcademicProgram { TenantId = tenantId, Name = "Training", Code = "TRN" };
                var student = new Student { TenantId = tenantId, PersonId = person.Id, UserId = studentId,
                    FullName = "Student Candidate", StudentCode = "CP-" + suffix,
                    AdmissionDate = new DateOnly(2026, 1, 1) };
                setup.AddRange(campus, year, program, student);
                await setup.SaveChangesAsync();
                campusId = campus.Id; studentReference = student.PublicId;
                setup.UserCampusAccesses.AddRange(
                    new UserCampusAccess { TenantId = tenantId, UserId = makerId, CampusId = campusId, IsActive = true },
                    new UserCampusAccess { TenantId = tenantId, UserId = checkerId, CampusId = campusId, IsActive = true });
                var level = new AcademicLevel { TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Level", Code = "L1", LevelNo = 1 };
                var curriculum = new AcademicCurriculum { TenantId = tenantId, AcademicProgramId = program.Id,
                    Name = "Core", Code = "CORE", EffectiveFrom = new DateOnly(2026, 1, 1), IsCurrent = true };
                setup.AddRange(level, curriculum);
                await setup.SaveChangesAsync();
                var batch = new AcademicBatch { TenantId = tenantId, CampusId = campus.Id,
                    AcademicYearId = year.Id, AcademicProgramId = program.Id, AcademicLevelId = level.Id,
                    Name = "Batch A", Code = "A", Capacity = 30 };
                setup.Add(batch);
                await setup.SaveChangesAsync();
                setup.Add(new StudentEnrollment { TenantId = tenantId, StudentId = student.Id,
                    CampusId = campusId, AcademicYearId = year.Id, AcademicProgramId = program.Id,
                    AcademicLevelId = level.Id, AcademicBatchId = batch.Id,
                    AcademicCurriculumId = curriculum.Id, RollNo = "1", EnrollmentDate = new DateOnly(2026, 1, 1),
                    ClientRequestId = Guid.NewGuid(), State = EnrollmentState.Active });
                await setup.SaveChangesAsync();
            }
        }
        await using var db = TenantDb(options, tenantId);
        var makerService = Service(db, tenantId, makerId, true);
        var checkerService = Service(db, tenantId, checkerId, true);
        var studentService = Service(db, tenantId, studentId, false);
        var employer = await makerService.CreateEmployerAsync(new CareerEmployerDraftDto
        {
            ClientRequestId = Guid.NewGuid(), CampusId = campusId, Name = "Example Placement Ltd"
        });
        Assert.True(employer.Success, employer.Message);
        var verified = await checkerService.VerifyEmployerAsync(employer.Data!.Reference, new CareerEmployerVerifyDto
        {
            ClientRequestId = Guid.NewGuid(), RowVersion = employer.Data.RowVersion
        });
        Assert.True(verified.Success, verified.Message);
        var draftRequest = new CareerOpportunityDraftDto
        {
            ClientRequestId = Guid.NewGuid(), EmployerReference = employer.Data.Reference,
            Title = "Software Internship", Kind = 2, ApplyUntilUtc = DateTime.UtcNow.AddDays(15)
        };
        var draft = await makerService.CreateOpportunityAsync(draftRequest);
        Assert.True(draft.Success, draft.Message);
        var replay = await makerService.CreateOpportunityAsync(draftRequest);
        Assert.True(replay.Success, replay.Message);
        Assert.Equal(draft.Data!.Reference, replay.Data!.Reference);
        var publish = new CareerActionDto { ClientRequestId = Guid.NewGuid(), RowVersion = draft.Data.RowVersion };
        Assert.Equal(403, (await makerService.ChangeOpportunityAsync(draft.Data.Reference, publish, 2)).StatusCode);
        var published = await checkerService.ChangeOpportunityAsync(draft.Data.Reference, publish, 2);
        Assert.True(published.Success, published.Message);
        Assert.Equal(2, published.Data!.Status);
        var ownStudents = await studentService.CareerStudentsAsync();
        Assert.Contains(ownStudents.Data!, x => x.StudentReference == studentReference && x.CampusId == campusId);
        var applyRequest = new CareerApplyDto { ClientRequestId = Guid.NewGuid(), StudentReference = studentReference };
        var submitted = await studentService.ApplyCareerAsync(draft.Data.Reference, applyRequest);
        Assert.True(submitted.Success, submitted.Message);
        Assert.Equal(1, submitted.Data!.Status);
        Assert.True((await studentService.ApplyCareerAsync(draft.Data.Reference, applyRequest)).Success);
        Assert.Equal(409, (await studentService.ApplyCareerAsync(draft.Data.Reference, new CareerApplyDto
        {
            ClientRequestId = Guid.NewGuid(), StudentReference = studentReference
        })).StatusCode);
        var proposed = await makerService.ChangeApplicationAsync(submitted.Data.Reference, new CareerDecisionDto
        {
            ClientRequestId = Guid.NewGuid(), RowVersion = submitted.Data.RowVersion, Outcome = 3
        }, 1, 3);
        Assert.True(proposed.Success, proposed.Message);
        Assert.Equal(1, proposed.Data!.Status);
        var pendingReport = await makerService.CareerReportAsync(new CareerOpportunityQueryDto());
        Assert.Equal(0, Assert.Single(pendingReport.Data!.Items.Where(x => x.OpportunityReference == draft.Data.Reference)).Placed);
        var approval = new CareerActionDto { ClientRequestId = Guid.NewGuid(), RowVersion = proposed.Data.RowVersion };
        Assert.Equal(403, (await makerService.ChangeApplicationAsync(submitted.Data.Reference, approval, 2, 0)).StatusCode);
        var placed = await checkerService.ChangeApplicationAsync(submitted.Data.Reference, approval, 2, 0);
        Assert.True(placed.Success, placed.Message);
        Assert.Equal(3, placed.Data!.Status);
        var report = await makerService.CareerReportAsync(new CareerOpportunityQueryDto());
        var row = Assert.Single(report.Data!.Items.Where(x => x.OpportunityReference == draft.Data.Reference));
        Assert.Equal(1, row.Applications); Assert.Equal(1, row.Placed); Assert.Equal(0, row.Pending);
        var personal = await studentService.CareerApplicationsAsync(new CareerApplicationQueryDto());
        Assert.Contains(personal.Data!.Items, x => x.Reference == placed.Data.Reference);
        await using var otherDb = TenantDb(options, otherTenantId);
        var outsider = Service(otherDb, otherTenantId, studentId, false);
        Assert.Empty((await outsider.CareerApplicationsAsync(new CareerApplicationQueryDto())).Data!.Items);
        Assert.Equal(403, (await outsider.ApplyCareerAsync(draft.Data.Reference, new CareerApplyDto
        {
            ClientRequestId = Guid.NewGuid(), StudentReference = studentReference
        })).StatusCode);
    }

    private static ApplicationUser User(string name)
    {
        var email = name + "@example.test";
        return new ApplicationUser { UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            Email = email, NormalizedEmail = email.ToUpperInvariant(), FullName = name, EmailConfirmed = true,
            IsActive = true, SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString() };
    }
    private static EduOSDbContext TenantDb(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, accessor);
    }
    private static AlumniService Service(EduOSDbContext db, long tenant, long actor, bool admin) =>
        new(db, new Actor(tenant, actor, admin), NullLogger<AlumniService>.Instance, new FakeTransport());
    private sealed class FakeTransport : IAlumniInvitationTransport
    {
        public Task<AlumniGatewaySendResult> SendAsync(CommunicationGateway gateway,
            string destination, string subject, string text, CancellationToken ct) =>
            Task.FromResult(new AlumniGatewaySendResult(false, "Not invoked by Career."));
    }
    private sealed class Actor(long tenant, long actor, bool admin) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => actor;
        public long TenantId => tenant;
        public string? FullName => "Career SQL CI";
        public string? Email => "career@example.test";
        public bool IsTenantAdmin => admin;
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => admin ? ["TenantAdmin"] : ["Student"];
        public bool IsInRole(string role) => admin ? role == "TenantAdmin" : role == "Student";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Career SQL CI";
    }
}
