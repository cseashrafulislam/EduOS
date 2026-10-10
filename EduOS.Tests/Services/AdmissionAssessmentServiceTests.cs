using EduOS.Core.DTOs.Admission;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Admission;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AdmissionAssessmentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Assessment_creation_validates_intake_scope_and_marks()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var scope = await SeedAsync(db, 101);
        var service = Service(db, new TestUser(101));
        var request = Request(scope);
        var created = await service.CreateTestAsync(request);
        var invalid = Request(scope);
        invalid.CampusId = scope.CampusId + 99;
        var rejected = await service.CreateTestAsync(invalid);
        created.StatusCode.Should().Be(201);
        rejected.StatusCode.Should().Be(409);
        var saved = await db.Set<AdmissionTest>().SingleAsync();
        saved.TenantId.Should().Be(101);
        saved.PassMarks.Should().Be(40m);
    }

    [Fact]
    public async Task Marks_are_scoped_to_same_admission_form()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var scope = await SeedAsync(db, 101);
        var applicant = await ApplicantAsync(db, scope.FormId, 101, "Rahim");
        var otherForm = new AdmissionIntakeForm { TenantId = 101, Code = "SECOND",
            Title = "Other", CampusId = scope.CampusId, AcademicYearId = scope.YearId,
            AcademicProgramId = scope.ProgramId, AcademicLevelId = scope.LevelId,
            State = AdmissionFormState.Published };
        db.Add(otherForm); await db.SaveChangesAsync();
        var outsider = await ApplicantAsync(db, otherForm.Id, 101, "Karim");
        var service = Service(db, new TestUser(101));
        var test = await service.CreateTestAsync(Request(scope));
        var valid = await service.SaveResultsAsync(test.Data!.Id, new SaveAdmissionResultsDto
        {
            Results = [new SaveAdmissionResultItemDto { ApplicantId = applicant.Id, ObtainedMarks = 75m }]
        });
        var invalid = await service.SaveResultsAsync(test.Data.Id, new SaveAdmissionResultsDto
        {
            Results = [new SaveAdmissionResultItemDto { ApplicantId = outsider.Id, ObtainedMarks = 80m }]
        });
        valid.Success.Should().BeTrue();
        valid.Data!.Single().ObtainedMarks.Should().Be(75m);
        valid.Data.Single().IsPassed.Should().BeTrue();
        invalid.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Publication_ranks_only_passed_applicants_and_blocks_edits()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var scope = await SeedAsync(db, 101);
        var first = await ApplicantAsync(db, scope.FormId, 101, "First");
        var second = await ApplicantAsync(db, scope.FormId, 101, "Second");
        var failed = await ApplicantAsync(db, scope.FormId, 101, "Failed");
        var service = Service(db, new TestUser(101));
        var test = await service.CreateTestAsync(Request(scope));
        var written = await service.SaveResultsAsync(test.Data!.Id, new SaveAdmissionResultsDto
        {
            Results = [
                new SaveAdmissionResultItemDto { ApplicantId = second.Id, ObtainedMarks = 80m },
                new SaveAdmissionResultItemDto { ApplicantId = first.Id, ObtainedMarks = 90m },
                new SaveAdmissionResultItemDto { ApplicantId = failed.Id, ObtainedMarks = 35m }
            ]
        });
        written.Success.Should().BeTrue();
        var published = await service.PublishMeritListAsync(test.Data.Id);
        var edited = await service.SaveResultsAsync(test.Data.Id, new SaveAdmissionResultsDto
        {
            Results = [new SaveAdmissionResultItemDto { ApplicantId = first.Id, ObtainedMarks = 95m }]
        });
        published.Success.Should().BeTrue(published.Message);
        published.Data!.Test.IsPublished.Should().BeTrue();
        published.Data.Results.Single(x => x.AdmissionApplicantReference == first.PublicId)
            .MeritPosition.Should().Be(1);
        published.Data.Results.Single(x => x.AdmissionApplicantReference == second.PublicId)
            .MeritPosition.Should().Be(2);
        published.Data.Results.Single(x => x.AdmissionApplicantReference == failed.PublicId)
            .MeritPosition.Should().BeNull();
        edited.StatusCode.Should().Be(409);
        await using var verify = Context(options, 101);
        var persisted = await verify.Set<AdmissionResult>().AsNoTracking()
            .Where(x => x.AdmissionTestId == test.Data.Id).ToListAsync();
        persisted.Single(x => x.AdmissionApplicantId == first.Id).MeritPosition.Should().Be(1);
        persisted.Single(x => x.AdmissionApplicantId == second.Id).MeritPosition.Should().Be(2);
        persisted.Single(x => x.AdmissionApplicantId == failed.Id).MeritPosition.Should().BeNull();
    }

    [Fact]
    public async Task Other_tenant_cannot_access_merit_list()
    {
        var options = Options();
        long id;
        await using (var db = Context(options, 202))
        {
            var scope = await SeedAsync(db, 202);
            id = (await Service(db, new TestUser(202)).CreateTestAsync(Request(scope))).Data!.Id;
        }
        await using var own = Context(options, 101);
        (await Service(own, new TestUser(101)).GetMeritListAsync(id)).StatusCode.Should().Be(404);
        (await Service(own, new TestUser(101)).PublishMeritListAsync(id)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Teacher_without_assessment_permission_is_denied()
    {
        await using var db = Context(Options(), 101);
        (await Service(db, new TestUser(101, "Teacher")).GetTestsAsync()).StatusCode.Should().Be(403);
    }

    private static AdmissionAssessmentService Service(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<AdmissionTest>(db), new GenericRepository<AdmissionResult>(db),
        new GenericRepository<AdmissionApplicant>(db), new GenericRepository<AdmissionIntakeForm>(db),
        db, user, new FixedTime(Now), NullLogger<AdmissionAssessmentService>.Instance);

    private static SaveAdmissionTestDto Request(Scope scope) => new()
    {
        Name = "Admission Assessment", AdmissionIntakeFormReference = scope.FormReference,
        CampusId = scope.CampusId, AcademicYearId = scope.YearId, AcademicLevelId = scope.LevelId,
        TestDate = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc),
        TotalMarks = 100m, PassMarks = 40m, DurationMinutes = 90
    };

    private static async Task<Scope> SeedAsync(EduOSDbContext db, long tenant)
    {
        var year = new AcademicYear { TenantId = tenant, Code = "2026",
            Name = "2026", StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31), IsActive = true };
        var campus = new Campus { TenantId = tenant, Name = "Main", Code = "MAIN", IsActive = true };
        var program = new AcademicProgram { TenantId = tenant, Name = "School", Code = "SCHOOL" };
        db.AddRange(year, campus, program); await db.SaveChangesAsync();
        var level = new AcademicLevel { TenantId = tenant, Name = "Class Six",
            Code = "C6", AcademicProgramId = program.Id, LevelNo = 6 };
        db.Add(level); await db.SaveChangesAsync();
        var form = new AdmissionIntakeForm { TenantId = tenant, Code = "INTAKE",
            Title = "Main Intake", CampusId = campus.Id, AcademicYearId = year.Id,
            AcademicLevelId = level.Id, AcademicProgramId = program.Id,
            State = AdmissionFormState.Published };
        db.Add(form); await db.SaveChangesAsync();
        return new Scope(campus.Id, year.Id, level.Id, program.Id, form.Id, form.PublicId);
    }

    private static async Task<AdmissionApplicant> ApplicantAsync(
        EduOSDbContext db, long formId, long tenant, string name)
    {
        var applicant = new AdmissionApplicant
        {
            TenantId = tenant, ClientRequestId = Guid.NewGuid(), AdmissionIntakeFormId = formId,
            PublicId = Guid.NewGuid(), ApplicationNumber = "APP-" + Guid.NewGuid().ToString("N"),
            FullName = name, DateOfBirth = new DateOnly(2012, 1, 1),
            Gender = "Male", Phone = "+8801712345678",
            State = AdmissionApplicantState.Submitted, SubmittedAt = Now.UtcDateTime
        };
        db.Add(applicant); await db.SaveChangesAsync();
        return applicant;
    }

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("admission-assessment-" + Guid.NewGuid().ToString("N")).Options;
    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
    private sealed record Scope(long CampusId, long YearId, long LevelId, long ProgramId,
        long FormId, Guid FormReference);
    private sealed class FixedTime(DateTimeOffset current) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => current;
    }
    private sealed class TestUser(long tenant, string role = "TenantAdmin") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => "Admission officer";
        public string? Email => "admission@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string required) => role == required;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Tests";
    }
}
