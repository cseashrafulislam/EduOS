using EduOS.Core.DTOs.Student;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Settings;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Helpers;
using EduOS.Service.Services.Students;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public class LearnerIdentityServiceTests
{
    private static readonly string LookupKey = Convert.ToBase64String(
        Enumerable.Range(1, 32).Select(x => (byte)x).ToArray());

    [Fact]
    public async Task New_identifier_creates_one_global_person_and_encrypted_tenant_link()
    {
        var options = CreateOptions();
        await using var context = CreateContext(options, 101);
        var student = await AddStudentAsync(context, 101, "Rahim Uddin");
        var service = CreateService(context, new TestCurrentUser(101), LookupKey);

        var result = await service.RegisterOrRequestAsync(Request(
            student.Id,
            PersonIdentifierType.BirthRegistration,
            "২০০১২৬৯২৫১০০০০১২৩"));

        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.State.Should().Be("Created");
        result.Data.PersonReference.Should().NotBeNull();
        result.Data.ConsentRequired.Should().BeFalse();

        var identifier = await context.PersonIdentifiers.SingleAsync();
        identifier.ProtectedValue.Should().NotContain("20012692510000123");
        identifier.LookupDigest.Should().NotContain("20012692510000123");
        identifier.VerificationStatus.Should().Be(IdentifierVerificationStatus.Unverified);
        (await context.StudentPersonLinks.SingleAsync()).TenantId.Should().Be(101);

        var access = await context.LearnerIdentityAccessLogs.SingleAsync();
        access.PersonId.Should().Be(identifier.PersonId);
        access.Outcome.Should().Be(LearnerIdentityAccessOutcome.Created);
    }

    [Fact]
    public async Task Same_student_and_identifier_are_idempotent()
    {
        var options = CreateOptions();
        await using var context = CreateContext(options, 101);
        var student = await AddStudentAsync(context, 101, "Rahim Uddin");
        var service = CreateService(context, new TestCurrentUser(101), LookupKey);
        var request = Request(student.Id, PersonIdentifierType.NationalId, "1234567890");

        var first = await service.RegisterOrRequestAsync(request);
        var second = await service.RegisterOrRequestAsync(request);

        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue();
        second.Data!.State.Should().Be("AlreadyLinked");
        (await context.Persons.CountAsync()).Should().Be(1);
        (await context.PersonIdentifiers.CountAsync()).Should().Be(1);
        (await context.StudentPersonLinks.CountAsync()).Should().Be(1);
        (await context.LearnerIdentityAccessLogs.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Cross_tenant_match_returns_only_consent_reference_and_never_person_data()
    {
        var options = CreateOptions();
        const string identifier = "20012692510000123";

        await using (var firstTenant = CreateContext(options, 101))
        {
            var firstStudent = await AddStudentAsync(firstTenant, 101, "Private First Name");
            var firstService = CreateService(firstTenant, new TestCurrentUser(101), LookupKey);
            (await firstService.RegisterOrRequestAsync(Request(
                firstStudent.Id,
                PersonIdentifierType.BirthRegistration,
                identifier))).Success.Should().BeTrue();

            var verifiedIdentifier = await firstTenant.PersonIdentifiers.SingleAsync();
            verifiedIdentifier.VerificationStatus = IdentifierVerificationStatus.Verified;
            verifiedIdentifier.VerifiedAt = DateTime.UtcNow;
            verifiedIdentifier.VerificationProvider = "TestApprovedProvider";
            await firstTenant.SaveChangesAsync();
        }

        await using var secondTenant = CreateContext(options, 202);
        var secondStudent = await AddStudentAsync(secondTenant, 202, "Applicant Supplied Name");
        var secondService = CreateService(secondTenant, new TestCurrentUser(202), LookupKey);

        var result = await secondService.RegisterOrRequestAsync(Request(
            secondStudent.Id,
            PersonIdentifierType.BirthRegistration,
            identifier));

        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(202);
        result.Data!.State.Should().Be("ConsentRequired");
        result.Data.PersonReference.Should().BeNull();
        result.Data.ConsentRequestReference.Should().NotBeNull();
        result.Message.Should().NotContain("Private First Name");
        (await secondTenant.StudentPersonLinks.CountAsync()).Should().Be(0);
        (await secondTenant.LearnerConsentRequests.CountAsync()).Should().Be(1);

        var log = await secondTenant.LearnerIdentityAccessLogs.SingleAsync();
        log.ReasonCode.Should().Be("CONSENT_REQUEST_CREATED");
        log.ReasonCode.Should().NotContain(identifier);
    }

    [Fact]
    public async Task Unverified_identifier_never_creates_cross_tenant_consent_request()
    {
        var options = CreateOptions();
        const string identifier = "20012692510000123";

        await using (var firstTenant = CreateContext(options, 101))
        {
            var firstStudent = await AddStudentAsync(firstTenant, 101, "First Claim");
            var firstService = CreateService(firstTenant, new TestCurrentUser(101), LookupKey);
            (await firstService.RegisterOrRequestAsync(Request(
                firstStudent.Id,
                PersonIdentifierType.BirthRegistration,
                identifier))).Success.Should().BeTrue();
        }

        await using var secondTenant = CreateContext(options, 202);
        var secondStudent = await AddStudentAsync(secondTenant, 202, "Second Claim");
        var secondService = CreateService(secondTenant, new TestCurrentUser(202), LookupKey);

        var result = await secondService.RegisterOrRequestAsync(Request(
            secondStudent.Id,
            PersonIdentifierType.BirthRegistration,
            identifier));

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Message.Should().NotContain("First Claim");
        (await secondTenant.LearnerConsentRequests.CountAsync()).Should().Be(0);
        (await secondTenant.LearnerIdentityAccessLogs.SingleAsync()).ReasonCode
            .Should().Be("IDENTIFIER_VERIFICATION_REQUIRED");
    }

    [Fact]
    public async Task Missing_lookup_key_fails_closed_and_records_non_sensitive_failure()
    {
        var options = CreateOptions();
        await using var context = CreateContext(options, 101);
        var student = await AddStudentAsync(context, 101, "Rahim Uddin");
        var service = CreateService(context, new TestCurrentUser(101), string.Empty);

        var result = await service.RegisterOrRequestAsync(Request(
            student.Id,
            PersonIdentifierType.NationalId,
            "1234567890"));

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(503);
        (await context.Persons.CountAsync()).Should().Be(0);
        var log = await context.LearnerIdentityAccessLogs.SingleAsync();
        log.ReasonCode.Should().Be("PROTECTION_NOT_CONFIGURED");
    }

    [Fact]
    public async Task Tenant_admin_cannot_link_identity_to_another_tenants_student()
    {
        var options = CreateOptions();
        long otherStudentId;
        await using (var seed = CreateContext(options, 202))
        {
            otherStudentId = (await AddStudentAsync(seed, 202, "Other Tenant Student")).Id;
        }

        await using var tenant = CreateContext(options, 101);
        var service = CreateService(tenant, new TestCurrentUser(101), LookupKey);

        var result = await service.RegisterOrRequestAsync(Request(
            otherStudentId,
            PersonIdentifierType.NationalId,
            "1234567890"));

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        (await tenant.Persons.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task User_without_admission_role_is_denied_before_identifier_lookup()
    {
        var options = CreateOptions();
        await using var context = CreateContext(options, 101);
        var student = await AddStudentAsync(context, 101, "Rahim Uddin");
        var service = CreateService(context, new ReadOnlyCurrentUser(101), LookupKey);

        var result = await service.RegisterOrRequestAsync(Request(
            student.Id,
            PersonIdentifierType.NationalId,
            "1234567890"));

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        (await context.PersonIdentifiers.CountAsync()).Should().Be(0);
        (await context.LearnerIdentityAccessLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Learner_identity_access_log_is_append_only()
    {
        var options = CreateOptions();
        await using var context = CreateContext(options, 101);
        var log = new LearnerIdentityAccessLog
        {
            TenantId = 101,
            UserId = 7,
            Action = LearnerIdentityAccessAction.RegisterOrLink,
            Outcome = LearnerIdentityAccessOutcome.Failed,
            ReasonCode = "TEST"
        };
        context.LearnerIdentityAccessLogs.Add(log);
        await context.SaveChangesAsync();

        log.ReasonCode = "CHANGED";
        var action = () => context.SaveChangesAsync();

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*append-only*");
    }

    private static RegisterLearnerIdentityRequestDto Request(
        long studentId,
        PersonIdentifierType identifierType,
        string identifierValue) =>
        new()
        {
            StudentId = studentId,
            IdentifierType = identifierType,
            IdentifierValue = identifierValue,
            Purpose = LearnerIdentityPurpose.Admission,
            RequestedScopes = LearnerDataScope.BasicIdentity
        };

    private static async Task<Student> AddStudentAsync(
        EduOSDbContext context,
        long tenantId,
        string name)
    {
        var student = new Student
        {
            TenantId = tenantId,
            StudentCode = $"S-{Guid.NewGuid():N}"[..18],
            FullName = name,
            FatherName = "Father",
            MotherName = "Mother",
            DOB = new DateTime(2010, 1, 2),
            Gender = "Male",
            AdmissionDate = DateTime.UtcNow,
            ClassId = 1,
            SectionId = 1,
            AcademicYearId = 1
        };
        context.Students.Add(student);
        await context.SaveChangesAsync();
        return student;
    }

    private static LearnerIdentityService CreateService(
        EduOSDbContext context,
        ICurrentUserService currentUser,
        string lookupKey)
    {
        var settings = Options.Create(new LearnerIdentitySettings
        {
            LookupKeyBase64 = lookupKey,
            ConsentRequestLifetimeHours = 168
        });
        var protector = new LearnerIdentifierProtector(
            settings,
            new EphemeralDataProtectionProvider());

        return new LearnerIdentityService(
            new GenericRepository<Student>(context),
            new GenericRepository<Person>(context),
            new GenericRepository<PersonIdentifier>(context),
            new GenericRepository<StudentPersonLink>(context),
            new GenericRepository<LearnerConsentRequest>(context),
            new GenericRepository<LearnerIdentityAccessLog>(context),
            context,
            currentUser,
            protector,
            settings,
            NullLogger<LearnerIdentityService>.Instance);
    }

    private static DbContextOptions<EduOSDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"learner-identity-{Guid.NewGuid():N}")
            .Options;

    private static EduOSDbContext CreateContext(
        DbContextOptions<EduOSDbContext> options,
        long tenantId)
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
        return new EduOSDbContext(
            options,
            new HttpContextAccessor { HttpContext = httpContext });
    }

    private sealed class TestCurrentUser(long tenantId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenantId;
        public string? FullName => "Admission Admin";
        public string? Email => "admin@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "EduOS tests";
    }

    private sealed class ReadOnlyCurrentUser(long tenantId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 8;
        public long TenantId => tenantId;
        public string? FullName => "Read Only";
        public string? Email => "reader@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => ["Teacher"];
        public bool IsInRole(string role) => role == "Teacher";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "EduOS tests";
    }
}
