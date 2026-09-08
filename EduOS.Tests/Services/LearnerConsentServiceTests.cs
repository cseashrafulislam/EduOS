using EduOS.Core.DTOs.Student;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Core.Settings;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Students;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public class LearnerConsentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Authorized_parent_can_approve_cross_tenant_request_without_receiving_person_data()
    {
        var options = CreateOptions();
        var scenario = await SeedScenarioAsync(options, LearnerDataScope.BasicIdentity | LearnerDataScope.AcademicSummary);
        await using var context = CreateContext(options, 101, 77, "Parent");
        var service = CreateService(context, new TestCurrentUser(101, 77, "Parent"));

        var pending = await service.GetPendingAsync();
        var result = await service.ResolveAsync(scenario.RequestReference,
            new ResolveLearnerConsentRequestDto { Decision = LearnerConsentDecision.Approve });

        pending.Success.Should().BeTrue();
        pending.Data.Should().ContainSingle();
        pending.Data![0].RequestingInstitution.Should().Be("Destination School");
        result.Success.Should().BeTrue();
        result.Data!.State.Should().Be("Approved");
        result.Data.GrantReference.Should().NotBeNull();
        result.Data.GrantReference!.Value.Should().NotBeEmpty();
        result.Data.GrantExpiresAt.Should().Be(Now.UtcDateTime.AddDays(365));

        var request = await context.LearnerConsentRequests.IgnoreQueryFilters().SingleAsync();
        request.Status.Should().Be(LearnerConsentRequestStatus.Approved);
        request.ResolvedByUserId.Should().Be(77);

        var grant = await context.LearnerDataGrants.IgnoreQueryFilters().SingleAsync();
        grant.TenantId.Should().Be(202);
        grant.PersonId.Should().Be(scenario.PersonId);
        grant.StudentId.Should().Be(scenario.TargetStudentId);
        grant.GrantedScopes.Should().Be(LearnerDataScope.BasicIdentity | LearnerDataScope.AcademicSummary);
        (await context.StudentPersonLinks.IgnoreQueryFilters()
            .CountAsync(x => x.PersonId == scenario.PersonId)).Should().Be(2);

        var log = await context.LearnerIdentityAccessLogs.IgnoreQueryFilters().SingleAsync();
        log.Outcome.Should().Be(LearnerIdentityAccessOutcome.Approved);
        log.ReasonCode.Should().Be("CONSENT_APPROVED");
    }

    [Fact]
    public async Task Parent_without_guardianship_gets_neutral_not_found_and_cannot_create_grant()
    {
        var options = CreateOptions();
        var scenario = await SeedScenarioAsync(options);
        await using var context = CreateContext(options, 101, 88, "Parent");
        var service = CreateService(context, new TestCurrentUser(101, 88, "Parent"));

        var result = await service.ResolveAsync(scenario.RequestReference,
            new ResolveLearnerConsentRequestDto { Decision = LearnerConsentDecision.Approve });

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        (await context.LearnerDataGrants.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await context.LearnerConsentRequests.IgnoreQueryFilters().SingleAsync()).Status
            .Should().Be(LearnerConsentRequestStatus.Pending);
        (await context.LearnerIdentityAccessLogs.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Denial_is_idempotent_and_never_links_or_grants()
    {
        var options = CreateOptions();
        var scenario = await SeedScenarioAsync(options);
        await using var context = CreateContext(options, 101, 77, "Parent");
        var service = CreateService(context, new TestCurrentUser(101, 77, "Parent"));
        var decision = new ResolveLearnerConsentRequestDto { Decision = LearnerConsentDecision.Deny };

        var first = await service.ResolveAsync(scenario.RequestReference, decision);
        var second = await service.ResolveAsync(scenario.RequestReference, decision);

        first.Success.Should().BeTrue();
        first.Data!.State.Should().Be("Denied");
        second.Success.Should().BeTrue();
        second.Data!.AlreadyProcessed.Should().BeTrue();
        (await context.LearnerDataGrants.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await context.StudentPersonLinks.IgnoreQueryFilters()
            .CountAsync(x => x.TenantId == 202 && x.StudentId == scenario.TargetStudentId)).Should().Be(0);
        (await context.LearnerIdentityAccessLogs.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Expired_request_fails_closed_and_records_expiry()
    {
        var options = CreateOptions();
        var scenario = await SeedScenarioAsync(options, expiresAt: Now.UtcDateTime.AddMinutes(-1));
        await using var context = CreateContext(options, 101, 77, "Parent");
        var service = CreateService(context, new TestCurrentUser(101, 77, "Parent"));

        var result = await service.ResolveAsync(scenario.RequestReference,
            new ResolveLearnerConsentRequestDto { Decision = LearnerConsentDecision.Approve });

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(410);
        (await context.LearnerConsentRequests.IgnoreQueryFilters().SingleAsync()).Status
            .Should().Be(LearnerConsentRequestStatus.Expired);
        (await context.LearnerDataGrants.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await context.LearnerIdentityAccessLogs.IgnoreQueryFilters().SingleAsync()).Outcome
            .Should().Be(LearnerIdentityAccessOutcome.Expired);
    }

    [Fact]
    public async Task Approved_grant_can_be_listed_and_revoked_idempotently()
    {
        var options = CreateOptions();
        var scenario = await SeedScenarioAsync(options);
        await using var context = CreateContext(options, 101, 77, "Parent");
        var service = CreateService(context, new TestCurrentUser(101, 77, "Parent"));
        var approval = await service.ResolveAsync(scenario.RequestReference,
            new ResolveLearnerConsentRequestDto { Decision = LearnerConsentDecision.Approve });
        var grantReference = approval.Data!.GrantReference!.Value;

        var active = await service.GetActiveGrantsAsync();
        var first = await service.RevokeAsync(grantReference);
        var second = await service.RevokeAsync(grantReference);

        active.Data.Should().ContainSingle(x => x.Reference == grantReference);
        first.Success.Should().BeTrue();
        first.Data!.State.Should().Be("Revoked");
        second.Success.Should().BeTrue();
        second.Data!.AlreadyProcessed.Should().BeTrue();
        (await context.LearnerDataGrants.IgnoreQueryFilters().SingleAsync()).Status
            .Should().Be(LearnerDataGrantStatus.Revoked);
        (await context.LearnerConsentRequests.IgnoreQueryFilters().SingleAsync()).Status
            .Should().Be(LearnerConsentRequestStatus.Revoked);
        (await context.LearnerIdentityAccessLogs.IgnoreQueryFilters().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Student_can_resolve_own_consent_but_unrelated_role_is_denied()
    {
        var options = CreateOptions();
        var scenario = await SeedScenarioAsync(options, ownerStudentUserId: 66, parentUserId: null);
        await using var studentContext = CreateContext(options, 101, 66, "Student");
        var studentService = CreateService(studentContext, new TestCurrentUser(101, 66, "Student"));

        var approved = await studentService.ResolveAsync(scenario.RequestReference,
            new ResolveLearnerConsentRequestDto { Decision = LearnerConsentDecision.Approve });

        approved.Success.Should().BeTrue();

        var secondOptions = CreateOptions();
        var secondScenario = await SeedScenarioAsync(secondOptions);
        await using var teacherContext = CreateContext(secondOptions, 101, 77, "Teacher");
        var teacherService = CreateService(teacherContext, new TestCurrentUser(101, 77, "Teacher"));
        var denied = await teacherService.ResolveAsync(secondScenario.RequestReference,
            new ResolveLearnerConsentRequestDto { Decision = LearnerConsentDecision.Approve });

        denied.StatusCode.Should().Be(403);
        (await teacherContext.LearnerDataGrants.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Normal_unit_of_work_still_rejects_cross_tenant_consent_mutation()
    {
        var options = CreateOptions();
        await SeedScenarioAsync(options);
        await using var context = CreateContext(options, 101, 77, "Parent");
        var request = await context.LearnerConsentRequests.IgnoreQueryFilters().SingleAsync();
        request.Status = LearnerConsentRequestStatus.Denied;

        var action = () => context.SaveChangesAsync();

        await action.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Tenant boundary violation*");
    }

    private static LearnerConsentService CreateService(
        EduOSDbContext context,
        ICurrentUserService currentUser) => new(
        new LearnerConsentRepository(context),
        currentUser,
        Options.Create(new LearnerIdentitySettings
        {
            ConsentRequestLifetimeHours = 168,
            DataGrantLifetimeDays = 365
        }),
        new FixedTimeProvider(Now),
        NullLogger<LearnerConsentService>.Instance);

    private static async Task<ConsentScenario> SeedScenarioAsync(
        DbContextOptions<EduOSDbContext> options,
        LearnerDataScope scopes = LearnerDataScope.BasicIdentity,
        DateTime? expiresAt = null,
        long? ownerStudentUserId = null,
        long? parentUserId = 77)
    {
        await using (var tenants = CreateContext(options, 101, 1, "TenantAdmin"))
        {
            tenants.Tenants.AddRange(
                Tenant(101, "Origin School", "ORIGIN"),
                Tenant(202, "Destination School", "DESTINATION"));
            await tenants.SaveChangesAsync();
        }

        long personId;
        await using (var origin = CreateContext(options, 101, 1, "TenantAdmin"))
        {
            var person = new Person
            {
                FullName = "Private Learner Name",
                DateOfBirth = new DateTime(2010, 1, 2),
                Gender = "Male"
            };
            var student = Student(101, "Origin Student", ownerStudentUserId);
            origin.Persons.Add(person);
            origin.Students.Add(student);
            origin.StudentPersonLinks.Add(new StudentPersonLink
            {
                TenantId = 101,
                Student = student,
                Person = person,
                Status = StudentPersonLinkStatus.Active,
                LinkedAt = Now.UtcDateTime,
                LinkedByUserId = 1
            });
            if (parentUserId.HasValue)
            {
                origin.Guardians.Add(new Guardian
                {
                    TenantId = 101,
                    Student = student,
                    UserId = parentUserId,
                    Name = "Authorized Parent",
                    Relation = "Parent",
                    Phone = "01700000000",
                    IsPrimary = true
                });
            }

            await origin.SaveChangesAsync();
            personId = person.Id;
        }

        await using var destination = CreateContext(options, 202, 2, "AdmissionOfficer");
        var target = Student(202, "Applicant Supplied Name");
        destination.Students.Add(target);
        await destination.SaveChangesAsync();
        var request = new LearnerConsentRequest
        {
            TenantId = 202,
            PersonId = personId,
            RequestedStudentId = target.Id,
            RequestedByUserId = 2,
            Purpose = LearnerIdentityPurpose.Admission,
            RequestedScopes = scopes,
            Status = LearnerConsentRequestStatus.Pending,
            ExpiresAt = expiresAt ?? Now.UtcDateTime.AddDays(2)
        };
        destination.LearnerConsentRequests.Add(request);
        await destination.SaveChangesAsync();
        return new ConsentScenario(request.PublicId, personId, target.Id);
    }

    private static Tenant Tenant(long id, string name, string code) => new()
    {
        Id = id,
        Name = name,
        Code = code,
        Email = $"{code.ToLowerInvariant()}@example.test",
        OwnerName = "Test Owner",
        IsActive = true
    };

    private static Student Student(long tenantId, string name, long? userId = null) => new()
    {
        TenantId = tenantId,
        UserId = userId,
        StudentCode = $"S-{Guid.NewGuid():N}"[..18],
        FullName = name,
        FatherName = "Father",
        MotherName = "Mother",
        DOB = new DateTime(2010, 1, 2),
        Gender = "Male",
        AdmissionDate = Now.UtcDateTime,
        ClassId = 1,
        SectionId = 1,
        AcademicYearId = 1
    };

    private static DbContextOptions<EduOSDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"learner-consent-{Guid.NewGuid():N}")
            .Options;

    private static EduOSDbContext CreateContext(
        DbContextOptions<EduOSDbContext> options,
        long tenantId,
        long userId,
        string role)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim("TenantId", tenantId.ToString())
            ], "TestAuthentication"))
        };
        httpContext.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = httpContext });
    }

    private sealed class TestCurrentUser(long tenantId, long userId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenantId;
        public string? FullName => "Consent User";
        public string? Email => "consent@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => string.Equals(value, role, StringComparison.Ordinal);
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "EduOS consent tests";
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record ConsentScenario(Guid RequestReference, long PersonId, long TargetStudentId);
}
