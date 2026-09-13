using EduOS.Core.DTOs.Admission;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Admission;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public class AdmissionAssessmentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_test_is_tenant_scoped_and_validates_academic_references()
    {
        var options = CreateOptions();
        await using var context = CreateContext(options, 101);
        var refs = await SeedReferencesAsync(context, 101);
        var service = CreateService(context, new TestCurrentUser(101));

        var created = await service.CreateTestAsync(CreateTestRequest(refs));
        var invalid = CreateTestRequest(refs);
        invalid.CampusId = refs.CampusId + 99999;
        var rejected = await service.CreateTestAsync(invalid);

        created.Success.Should().BeTrue();
        created.StatusCode.Should().Be(201);
        rejected.StatusCode.Should().Be(409);
        var saved = await context.Set<AdmissionTest>().SingleAsync();
        saved.TenantId.Should().Be(101);
        saved.PassMarks.Should().Be(40);
    }

    [Fact]
    public async Task Save_results_calculates_pass_percentage_and_blocks_foreign_scope()
    {
        var options = CreateOptions();
        await using var context = CreateContext(options, 101);
        var refs = await SeedReferencesAsync(context, 101);
        var applicant = await SeedApplicantAsync(context, 101, refs, "APP-101-1", "Rahim");
        var foreignScope = await SeedApplicantAsync(context, 101, refs with { CampusId = refs.OtherCampusId }, "APP-101-2", "Karim");
        var service = CreateService(context, new TestCurrentUser(101));
        var test = await service.CreateTestAsync(CreateTestRequest(refs));

        var saved = await service.SaveResultsAsync(test.Data!.Id, new SaveAdmissionResultsDto
        {
            Results = [new SaveAdmissionResultItemDto { ApplicantId = applicant.Id, ObtainedMarks = 75 }]
        });
        var invalid = await service.SaveResultsAsync(test.Data.Id, new SaveAdmissionResultsDto
        {
            Results = [new SaveAdmissionResultItemDto { ApplicantId = foreignScope.Id, ObtainedMarks = 70 }]
        });

        saved.Success.Should().BeTrue();
        saved.Data!.Single().Percentage.Should().Be(75);
        saved.Data.Single().IsPassed.Should().BeTrue();
        invalid.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Publish_assigns_merit_only_to_passed_applicants_and_locks_changes()
    {
        var options = CreateOptions();
        await using var context = CreateContext(options, 101);
        var refs = await SeedReferencesAsync(context, 101);
        var first = await SeedApplicantAsync(context, 101, refs, "APP-101-1", "First");
        var second = await SeedApplicantAsync(context, 101, refs, "APP-101-2", "Second");
        var failed = await SeedApplicantAsync(context, 101, refs, "APP-101-3", "Failed");
        var service = CreateService(context, new TestCurrentUser(101));
        var test = await service.CreateTestAsync(CreateTestRequest(refs));

        await service.SaveResultsAsync(test.Data!.Id, new SaveAdmissionResultsDto
        {
            Results =
            [
                new SaveAdmissionResultItemDto { ApplicantId = second.Id, ObtainedMarks = 80 },
                new SaveAdmissionResultItemDto { ApplicantId = first.Id, ObtainedMarks = 90 },
                new SaveAdmissionResultItemDto { ApplicantId = failed.Id, ObtainedMarks = 35 }
            ]
        });
        var published = await service.PublishMeritListAsync(test.Data.Id);
        var afterPublish = await service.SaveResultsAsync(test.Data.Id, new SaveAdmissionResultsDto
        {
            Results = [new SaveAdmissionResultItemDto { ApplicantId = first.Id, ObtainedMarks = 95 }]
        });

        published.Success.Should().BeTrue();
        published.Data!.Test.IsPublished.Should().BeTrue();
        published.Data.Results.Single(x => x.ApplicantId == first.Id).MeritPosition.Should().Be(1);
        published.Data.Results.Single(x => x.ApplicantId == second.Id).MeritPosition.Should().Be(2);
        published.Data.Results.Single(x => x.ApplicantId == failed.Id).MeritPosition.Should().BeNull();
        afterPublish.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Other_tenant_cannot_read_or_publish_test()
    {
        var options = CreateOptions();
        long testId;
        await using (var tenant202 = CreateContext(options, 202))
        {
            var refs = await SeedReferencesAsync(tenant202, 202);
            var service = CreateService(tenant202, new TestCurrentUser(202));
            testId = (await service.CreateTestAsync(CreateTestRequest(refs))).Data!.Id;
        }

        await using var tenant101 = CreateContext(options, 101);
        var service101 = CreateService(tenant101, new TestCurrentUser(101));
        var merit = await service101.GetMeritListAsync(testId);
        var publish = await service101.PublishMeritListAsync(testId);

        merit.StatusCode.Should().Be(404);
        publish.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Teacher_role_is_denied()
    {
        var options = CreateOptions();
        await using var context = CreateContext(options, 101);
        var service = CreateService(context, new TestCurrentUser(101, "Teacher"));

        var result = await service.GetTestsAsync();

        result.StatusCode.Should().Be(403);
    }

    private static AdmissionAssessmentService CreateService(EduOSDbContext context, ICurrentUserService user) => new(
        new GenericRepository<AdmissionTest>(context),
        new GenericRepository<AdmissionResult>(context),
        new GenericRepository<AdmissionApplicant>(context),
        new GenericRepository<AcademicYear>(context),
        new GenericRepository<Campus>(context),
        new GenericRepository<Class>(context),
        context,
        user,
        new FixedTimeProvider(Now),
        NullLogger<AdmissionAssessmentService>.Instance);

    private static SaveAdmissionTestDto CreateTestRequest(ReferenceIds refs) => new()
    {
        Name = "Admission Assessment",
        AcademicYearId = refs.YearId,
        CampusId = refs.CampusId,
        AcademicUnitId = refs.UnitId,
        TestDate = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc),
        TotalMarks = 100,
        PassMarks = 40,
        DurationMinutes = 90,
        Venue = "Main Hall"
    };

    private static async Task<AdmissionApplicant> SeedApplicantAsync(EduOSDbContext context, long tenantId, ReferenceIds refs, string no, string name)
    {
        var applicant = new AdmissionApplicant
        {
            TenantId = tenantId,
            PublicId = Guid.NewGuid(),
            ClientRequestId = Guid.NewGuid(),
            ApplicationNumber = no,
            AcademicYearId = refs.YearId,
            CampusId = refs.CampusId,
            AcademicUnitId = refs.UnitId,
            ApplicantName = name,
            DateOfBirth = new DateTime(2012, 1, 1),
            Gender = Gender.Male,
            PrimaryMobile = "+8801712345678",
            PreferredLanguage = "bn-BD",
            Status = AdmissionApplicationStatus.UnderReview,
            SubmittedAtUtc = Now.UtcDateTime
        };
        context.Add(applicant);
        await context.SaveChangesAsync();
        return applicant;
    }

    private static async Task<ReferenceIds> SeedReferencesAsync(EduOSDbContext context, long tenantId)
    {
        var year = new AcademicYear { TenantId = tenantId, Name = "2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsActive = true };
        var campus = new Campus { TenantId = tenantId, Name = "Main", Code = $"M{tenantId}", IsActive = true };
        var otherCampus = new Campus { TenantId = tenantId, Name = "Other", Code = $"O{tenantId}", IsActive = true };
        var unit = new Class { TenantId = tenantId, Name = "Class Six", NumericValue = 6, IsActive = true };
        context.AddRange(year, campus, otherCampus, unit);
        await context.SaveChangesAsync();
        return new ReferenceIds(year.Id, campus.Id, otherCampus.Id, unit.Id);
    }

    private static DbContextOptions<EduOSDbContext> CreateOptions() => new DbContextOptionsBuilder<EduOSDbContext>()
        .UseInMemoryDatabase($"admission-assessment-{Guid.NewGuid():N}")
        .Options;

    private static EduOSDbContext CreateContext(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "7"),
                new Claim(ClaimTypes.Role, "TenantAdmin"),
                new Claim("TenantId", tenantId.ToString())
            ], "TestAuthentication"))
        };
        httpContext.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = httpContext });
    }

    private sealed record ReferenceIds(long YearId, long CampusId, long OtherCampusId, long UnitId);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestCurrentUser(long tenantId, string role = "TenantAdmin") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenantId;
        public string? FullName => "Admission User";
        public string? Email => "admission@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string requestedRole) => requestedRole == role;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "EduOS tests";
    }
}
