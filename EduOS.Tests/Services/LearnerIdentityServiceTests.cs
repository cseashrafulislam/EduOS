using EduOS.Core.DTOs.Learners;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Settings;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Helpers;
using EduOS.Service.Services.Learners;
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
        identifier.IsVerified.Should().BeFalse();
        (await context.StudentPersonLinks.SingleAsync()).TenantId.Should().Be(101);

        var access = await context.LearnerIdentityAccessLogs.SingleAsync();
        access.PersonId.Should().Be(identifier.PersonId);
        access.OutcomeCode.Should().Be("Created");
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
    public async Task Tenant_local_person_can_add_an_identifier_without_changing_student_identity()
    {
        var options = CreateOptions();
        await using var db = CreateContext(options, 101);
        var student = await AddStudentAsync(db, 101, "Local Student");
        var person = new Person { FullName = "Local Student" };
        db.Persons.Add(person);
        await db.SaveChangesAsync();
        student.PersonId = person.Id;
        db.StudentPersonLinks.Add(new StudentPersonLink
        {
            TenantId = 101, StudentId = student.Id, PersonId = person.Id,
            LinkedAt = DateTime.UtcNow, IsPrimary = true
        });
        await db.SaveChangesAsync();

        var result = await CreateService(db, new TestCurrentUser(101), LookupKey)
            .RegisterOrRequestAsync(Request(student.Id, PersonIdentifierType.NationalId, "1234567890"));

        result.Success.Should().BeTrue();
        result.Data!.State.Should().Be("IdentifierAdded");
        (await db.PersonIdentifiers.SingleAsync()).PersonId.Should().Be(person.Id);
        (await db.Students.SingleAsync()).PersonId.Should().Be(person.Id);
    }

    [Fact]
    public async Task Shared_person_cannot_receive_a_new_global_identifier_from_one_institution()
    {
        var options = CreateOptions();
        await using var db = CreateContext(options, 101);
        var student = await AddStudentAsync(db, 101, "First Student");
        var person = new Person { FullName = "First Student" };
        db.Persons.Add(person);
        await db.SaveChangesAsync();
        student.PersonId = person.Id;
        db.StudentPersonLinks.Add(new StudentPersonLink
        {
            TenantId = 101, StudentId = student.Id, PersonId = person.Id,
            LinkedAt = DateTime.UtcNow, IsPrimary = true
        });
        await db.SaveChangesAsync();

        await using (var foreign = CreateContext(options, 202))
        {
            var otherStudent = await AddStudentAsync(foreign, 202, "Second Student");
            otherStudent.PersonId = person.Id;
            foreign.StudentPersonLinks.Add(new StudentPersonLink
            {
                TenantId = 202, StudentId = otherStudent.Id, PersonId = person.Id,
                LinkedAt = DateTime.UtcNow, IsPrimary = true
            });
            await foreign.SaveChangesAsync();
        }

        await using var localTenant = CreateContext(options, 101);
        var result = await CreateService(localTenant, new TestCurrentUser(101), LookupKey)
            .RegisterOrRequestAsync(Request(student.Id, PersonIdentifierType.NationalId, "1234567890"));

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Data.Should().BeNull();
        (await localTenant.PersonIdentifiers.CountAsync()).Should().Be(0);
        (await localTenant.StudentPersonLinks.IgnoreQueryFilters().CountAsync()).Should().Be(2);
        (await localTenant.LearnerIdentityAccessLogs.SingleAsync()).ReasonCode
            .Should().Be("SHARED_PERSON_IDENTIFIER_REVIEW_REQUIRED");
    }

    [Fact]
    public async Task Shared_identifier_cannot_create_a_new_tenant_link_but_replays_an_existing_link()
    {
        var options = CreateOptions();
        long sharedPersonId;
        await using (var origin = CreateContext(options, 101))
        {
            var student = await AddStudentAsync(origin, 101, "Origin Student");
            var created = await CreateService(origin, new TestCurrentUser(101), LookupKey)
                .RegisterOrRequestAsync(Request(student.Id, PersonIdentifierType.NationalId, "1234567890"));
            created.Success.Should().BeTrue();
            sharedPersonId = (await origin.PersonIdentifiers.SingleAsync()).PersonId;
        }

        await using var other = CreateContext(options, 202);
        var local = await AddStudentAsync(other, 202, "Existing Shared Student");
        local.PersonId = sharedPersonId;
        await other.SaveChangesAsync();
        var service = CreateService(other, new TestCurrentUser(202), LookupKey);
        var attempt = Request(local.Id, PersonIdentifierType.NationalId, "1234567890");

        (await service.RegisterOrRequestAsync(attempt)).StatusCode.Should().Be(409);
        (await other.StudentPersonLinks.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await other.PersonIdentifiers.CountAsync()).Should().Be(1);

        // This fixture models a link independently granted before the replay.
        other.StudentPersonLinks.Add(new StudentPersonLink
        {
            TenantId = 202, StudentId = local.Id, PersonId = sharedPersonId,
            IsPrimary = true, LinkedAt = DateTime.UtcNow
        });
        await other.SaveChangesAsync();
        var replay = await service.RegisterOrRequestAsync(attempt);
        replay.Success.Should().BeTrue();
        replay.Data!.State.Should().Be("AlreadyLinked");
        (await other.StudentPersonLinks.IgnoreQueryFilters().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Opaque_student_reference_registers_identity_for_current_tenant()
    {
        var options = CreateOptions();
        await using var context = CreateContext(options, 101);
        var student = await AddStudentAsync(context, 101, "Rahim Uddin");
        var service = CreateService(context, new TestCurrentUser(101), LookupKey);

        var result = await service.RegisterOrRequestByReferenceAsync(new RegisterLearnerIdentityByReferenceRequestDto
        {
            StudentReference = student.PublicId,
            IdentifierType = PersonIdentifierType.NationalId,
            IdentifierValue = "1234567890",
            Purpose = LearnerIdentityPurpose.Admission,
            RequestedScopes = LearnerDataScope.BasicIdentity
        });

        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        (await context.StudentPersonLinks.SingleAsync()).StudentId.Should().Be(student.Id);
    }

    [Fact]
    public async Task Opaque_student_reference_must_not_resolve_another_tenants_student()
    {
        var options = CreateOptions();
        Guid otherStudentReference;
        await using (var seed = CreateContext(options, 202))
            otherStudentReference = (await AddStudentAsync(seed, 202, "Private Student")).PublicId;

        await using var tenantContext = CreateContext(options, 101);
        var service = CreateService(tenantContext, new TestCurrentUser(101), LookupKey);

        var result = await service.RegisterOrRequestByReferenceAsync(new RegisterLearnerIdentityByReferenceRequestDto
        {
            StudentReference = otherStudentReference,
            IdentifierType = PersonIdentifierType.NationalId,
            IdentifierValue = "1234567890",
            Purpose = LearnerIdentityPurpose.Admission,
            RequestedScopes = LearnerDataScope.BasicIdentity
        });

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        result.Message.Should().NotContain("Private Student");
        (await tenantContext.PersonIdentifiers.CountAsync()).Should().Be(0);
        (await tenantContext.LearnerIdentityAccessLogs.CountAsync()).Should().Be(0);
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
            verifiedIdentifier.IsVerified = true;
            verifiedIdentifier.VerifiedAt = DateTime.UtcNow;
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Existing_local_person_is_preserved_and_only_verified_foreign_identifier_can_request_consent(
        bool verified)
    {
        var options = CreateOptions();
        const string identifier = "20012692510000123";
        long foreignPersonId;
        await using (var origin = CreateContext(options, 101))
        {
            var student = await AddStudentAsync(origin, 101, "Private Origin Learner");
            (await CreateService(origin, new TestCurrentUser(101), LookupKey)
                .RegisterOrRequestAsync(Request(student.Id, PersonIdentifierType.BirthRegistration,
                    identifier))).Success.Should().BeTrue();
            var source = await origin.PersonIdentifiers.SingleAsync();
            source.IsVerified = verified;
            source.VerifiedAt = verified ? DateTime.UtcNow : null;
            foreignPersonId = source.PersonId;
            await origin.SaveChangesAsync();
        }

        await using var destination = CreateContext(options, 202);
        var localPerson = new Person { FullName = "Tenant-local identity" };
        destination.Persons.Add(localPerson);
        await destination.SaveChangesAsync();
        var student2 = await AddStudentAsync(destination, 202, "Applicant Provided Name");
        student2.PersonId = localPerson.Id;
        destination.StudentPersonLinks.Add(new StudentPersonLink
        {
            TenantId = 202, StudentId = student2.Id, PersonId = localPerson.Id,
            IsPrimary = true, LinkedAt = DateTime.UtcNow
        });
        await destination.SaveChangesAsync();

        var response = await CreateService(destination, new TestCurrentUser(202), LookupKey)
            .RegisterOrRequestAsync(Request(student2.Id, PersonIdentifierType.BirthRegistration,
                identifier));

        response.Success.Should().Be(verified);
        response.StatusCode.Should().Be(verified ? 202 : 409);
        response.Data?.PersonReference.Should().BeNull();
        (await destination.Students.SingleAsync(x => x.Id == student2.Id)).PersonId
            .Should().Be(localPerson.Id);
        (await destination.StudentPersonLinks.SingleAsync()).PersonId.Should().Be(localPerson.Id);
        (await destination.LearnerConsentRequests.CountAsync()).Should().Be(verified ? 1 : 0);
        // PersonIdentifier is intentionally global: the verified origin row is
        // visible to this query, but consent must not create a duplicate.
        (await destination.PersonIdentifiers.CountAsync(x => x.PersonId == foreignPersonId))
            .Should().Be(1);
        if (verified)
        {
            response.Data!.State.Should().Be("ConsentRequired");
            response.Data.ConsentRequestReference.Should().NotBeNull();
            (await destination.LearnerConsentRequests.SingleAsync()).PersonId
                .Should().Be(foreignPersonId);
        }
        else
            (await destination.LearnerIdentityAccessLogs.SingleAsync()).ReasonCode
                .Should().Be("IDENTIFIER_VERIFICATION_REQUIRED");
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
            Action = "RegisterOrLink",
            OutcomeCode = "Failed",
            ReasonCode = "TEST"
        };
        context.LearnerIdentityAccessLogs.Add(log);
        await context.SaveChangesAsync();

        log.ReasonCode = "CHANGED";
        var action = () => context.SaveChangesAsync();

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*append-only*");
    }

    [Fact]
    public async Task Tenant_admin_can_page_and_filter_only_own_identity_access_events()
    {
        var options = CreateOptions();
        Guid localStudentReference;
        await using (var firstTenant = CreateContext(options, 101))
        {
            var student = await AddStudentAsync(firstTenant, 101, "First Student");
            localStudentReference = student.PublicId;
            var service = CreateService(firstTenant, new TestCurrentUser(101), LookupKey);
            var request = Request(student.Id, PersonIdentifierType.NationalId, "1234567890");
            (await service.RegisterOrRequestAsync(request)).Success.Should().BeTrue();
            (await service.RegisterOrRequestAsync(request)).Success.Should().BeTrue();
        }
        await using (var otherTenant = CreateContext(options, 202))
        {
            var student = await AddStudentAsync(otherTenant, 202, "Private Student");
            var service = CreateService(otherTenant, new TestCurrentUser(202), LookupKey);
            (await service.RegisterOrRequestAsync(Request(student.Id, PersonIdentifierType.NationalId, "9876543210")))
                .Success.Should().BeTrue();
        }
        await using var context = CreateContext(options, 101);
        var auditService = CreateService(context, new TestCurrentUser(101), LookupKey);
        var firstPage = await auditService.GetAccessHistoryAsync(new LearnerIdentityAccessLogFilterDto
        {
            StudentReference = localStudentReference, Page = 1, PageSize = 1
        });

        firstPage.Success.Should().BeTrue();
        firstPage.Data!.TotalCount.Should().Be(2);
        firstPage.Data.TotalPages.Should().Be(2);
        firstPage.Data.Items.Should().ContainSingle();
        firstPage.Data.Items[0].UserId.Should().Be(7);

        var filtered = await auditService.GetAccessHistoryAsync(new LearnerIdentityAccessLogFilterDto
        {
            OutcomeCode = "Reused", Action = "RegisterOrLink"
        });
        filtered.Data!.TotalCount.Should().Be(1);
        filtered.Data.Items[0].ReasonCode.Should().Be("IDENTITY_ALREADY_LINKED");
    }

    [Fact]
    public async Task Identity_access_history_denies_other_tenant_student_reference_and_non_admin()
    {
        var options = CreateOptions();
        Guid otherStudentReference;
        await using (var otherTenant = CreateContext(options, 202))
            otherStudentReference = (await AddStudentAsync(otherTenant, 202, "Private Student")).PublicId;

        await using var tenant = CreateContext(options, 101);
        var administrator = CreateService(tenant, new TestCurrentUser(101), LookupKey);
        var missing = await administrator.GetAccessHistoryAsync(new LearnerIdentityAccessLogFilterDto
        {
            StudentReference = otherStudentReference
        });
        missing.StatusCode.Should().Be(404);
        missing.Message.Should().NotContain("Private Student");

        var invalid = await administrator.GetAccessHistoryAsync(new LearnerIdentityAccessLogFilterDto
        {
            Page = 0, PageSize = 500
        });
        invalid.StatusCode.Should().Be(400);

        var nonAdmin = CreateService(tenant, new ReadOnlyCurrentUser(101), LookupKey);
        var denied = await nonAdmin.GetAccessHistoryAsync(new LearnerIdentityAccessLogFilterDto());
        denied.StatusCode.Should().Be(403);
        denied.Data.Should().BeNull();
    }

    [Fact]
    public async Task Direct_identity_service_rejects_switched_institution_before_person_or_audit_access()
    {
        var options = CreateOptions();
        await using var db = CreateContext(options, 101);
        var service = CreateService(db, new TestCurrentUser(202), LookupKey);
        (await service.GetAccessHistoryAsync(new LearnerIdentityAccessLogFilterDto())).StatusCode.Should().Be(403);
        (await service.RegisterOrRequestAsync(Request(1, PersonIdentifierType.BirthRegistration,
            "12345678901234567"))).StatusCode.Should().Be(403);
        db.ChangeTracker.HasChanges().Should().BeFalse();
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
            DateOfBirth = new DateOnly(2010, 1, 2),
            Gender = "Male",
            AdmissionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            StatusCode = "Active"
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
            new PersonTenantIsolationGuard(
                new GenericRepository<StudentPersonLink>(context),
                new GenericRepository<Student>(context),
                new GenericRepository<Guardian>(context),
                new GenericRepository<Employee>(context),
                new GenericRepository<AdmissionApplicant>(context),
                new GenericRepository<TenantMembership>(context),
                new GenericRepository<ApplicationUser>(context),
                new GenericRepository<LearnerConsentRequest>(context),
                new GenericRepository<LearnerDataGrant>(context)),
            context,
            currentUser,
            protector,
            settings,
            context,
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
