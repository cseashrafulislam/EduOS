using EduOS.Core.DTOs.Learners;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Settings;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Helpers;
using EduOS.Service.Services.Learners;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// SQL-backed 16-institute verified-identifier/consent lifecycle. Every
/// destination has an independently created canonical Student.PersonId: consent
/// may create only a secondary StudentPersonLink and a scoped grant, never merge
/// Person records or overwrite the local Student's identity/history.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    private static readonly string CohortIdentityLookupKey = Convert.ToBase64String(
        Enumerable.Range(1, 32).Select(x => (byte)(x + 17)).ToArray());

    private static LearnerIdentityService CohortIdentityService(
        EduOSDbContext db, long tenantId, long userId)
    {
        var settings = Options.Create(new LearnerIdentitySettings
        {
            LookupKeyBase64 = CohortIdentityLookupKey,
            ConsentRequestLifetimeHours = 168,
            DataGrantLifetimeDays = 365
        });
        return new LearnerIdentityService(
            new GenericRepository<Student>(db),
            new GenericRepository<Person>(db),
            new GenericRepository<PersonIdentifier>(db),
            new GenericRepository<StudentPersonLink>(db),
            new GenericRepository<LearnerConsentRequest>(db),
            new GenericRepository<LearnerIdentityAccessLog>(db),
            new PersonTenantIsolationGuard(
                new GenericRepository<StudentPersonLink>(db),
                new GenericRepository<Student>(db),
                new GenericRepository<Guardian>(db),
                new GenericRepository<Employee>(db),
                new GenericRepository<AdmissionApplicant>(db),
                new GenericRepository<TenantMembership>(db),
                new GenericRepository<ApplicationUser>(db),
                new GenericRepository<LearnerConsentRequest>(db),
                new GenericRepository<LearnerDataGrant>(db)),
            db,
            new TenantAdmin(tenantId, userId),
            new LearnerIdentifierProtector(settings, new EphemeralDataProtectionProvider()),
            settings,
            db,
            NullLogger<LearnerIdentityService>.Instance);
    }

    [Fact]
    public async Task Sixteen_SQL_institutes_link_verified_foreign_identity_only_after_scoped_consent()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        var identifierValue = "2001" +
            string.Concat(Guid.NewGuid().ToString("N").Where(char.IsDigit))
                .PadRight(13, '0')[..13];

        var cohort = new List<(Fixture Tenant, long StudentId, long PersonId, Guid StudentReference)>(16);
        for (var i = 0; i < 16; i++)
        {
            var tenant = await SeedAsync(connection);
            await using var db = ScopedContext(tenant.Options, tenant.TenantId, tenant.UserId);
            var student = await db.Students
                .SingleAsync(x => x.TenantId == tenant.TenantId &&
                    x.StudentCode.StartsWith("CAP-A-"));
            Assert.True(student.PersonId > 0);
            // Transport's minimal fixture omits DOB; admission identity requires it.
            student.DateOfBirth = new DateOnly(2010, 1, 2);
            if (i == 0) student.UserId = tenant.UserId; // Authorized learner consent reviewer.
            await db.SaveChangesAsync();
            cohort.Add((tenant, student.Id, student.PersonId, student.PublicId));
        }
        Assert.Equal(16, cohort.Select(x => x.Tenant.TenantId).Distinct().Count());
        Assert.Equal(16, cohort.Select(x => x.PersonId).Distinct().Count());

        var origin = cohort[0];
        await using (var db = ScopedContext(origin.Tenant.Options,
            origin.Tenant.TenantId, origin.Tenant.UserId))
        {
            var registered = await CohortIdentityService(db, origin.Tenant.TenantId,
                origin.Tenant.UserId).RegisterOrRequestByReferenceAsync(
                new RegisterLearnerIdentityByReferenceRequestDto
                {
                    StudentReference = origin.StudentReference,
                    IdentifierType = PersonIdentifierType.BirthRegistration,
                    IdentifierValue = identifierValue,
                    Purpose = LearnerIdentityPurpose.Admission,
                    RequestedScopes = LearnerDataScope.BasicIdentity
                });
            Assert.True(registered.Success, registered.Message);
            Assert.Equal("IdentifierAdded", registered.Data!.State);

            var saved = await db.PersonIdentifiers.SingleAsync(x => x.PersonId == origin.PersonId);
            Assert.False(saved.IsVerified);
            Assert.DoesNotContain(identifierValue, saved.LookupDigest);
            Assert.DoesNotContain(identifierValue, saved.ProtectedValue);
            Assert.Equal(1, await db.StudentPersonLinks.CountAsync(x =>
                x.TenantId == origin.Tenant.TenantId && x.StudentId == origin.StudentId));
        }

        // The verified flag is a disposable test fixture standing in for an
        // independently audited verification decision, not a provider integration.
        await using (var db = ScopedContext(origin.Tenant.Options,
            origin.Tenant.TenantId, origin.Tenant.UserId))
        {
            var saved = await db.PersonIdentifiers.SingleAsync(x => x.PersonId == origin.PersonId);
            saved.IsVerified = true;
            saved.VerifiedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var pending = new List<(long TenantId, Guid Reference)>();
        foreach (var member in cohort.Skip(1))
        {
            await using var db = ScopedContext(member.Tenant.Options,
                member.Tenant.TenantId, member.Tenant.UserId);
            var service = CohortIdentityService(db, member.Tenant.TenantId, member.Tenant.UserId);
            var response = await service.RegisterOrRequestByReferenceAsync(
                new RegisterLearnerIdentityByReferenceRequestDto
                {
                    StudentReference = member.StudentReference,
                    IdentifierType = PersonIdentifierType.BirthRegistration,
                    IdentifierValue = identifierValue,
                    Purpose = LearnerIdentityPurpose.Admission,
                    RequestedScopes = LearnerDataScope.BasicIdentity
                });

            Assert.True(response.Success, response.Message);
            Assert.Equal(202, response.StatusCode);
            Assert.Equal("ConsentRequired", response.Data!.State);
            Assert.Null(response.Data.PersonReference);
            pending.Add((member.Tenant.TenantId, Assert.IsType<Guid>(response.Data.ConsentRequestReference)));
            Assert.DoesNotContain(identifierValue, response.Message);
            Assert.Equal(member.PersonId, await db.Students
                .Where(x => x.Id == member.StudentId)
                .Select(x => x.PersonId).SingleAsync());
            Assert.Equal(0, await db.StudentPersonLinks.CountAsync(x =>
                x.TenantId == member.Tenant.TenantId && x.StudentId == member.StudentId &&
                x.PersonId == origin.PersonId && x.UnlinkedAt == null));
            Assert.Equal(1, await db.LearnerConsentRequests.CountAsync(x =>
                x.PersonId == origin.PersonId && x.RequestedStudentId == member.StudentId &&
                x.State == ConsentState.Pending));
            Assert.Equal(0, await db.LearnerDataGrants.CountAsync());
            Assert.Contains(await db.LearnerIdentityAccessLogs
                .Where(x => x.StudentId == member.StudentId)
                .Select(x => x.ReasonCode).ToListAsync(),
                x => x == "CONSENT_REQUEST_CREATED");

            // An opaque Student reference from a different institute is never
            // a valid selector, even for an otherwise authorized tenant admin.
            var guessed = await service.RegisterOrRequestByReferenceAsync(
                new RegisterLearnerIdentityByReferenceRequestDto
                {
                    StudentReference = origin.StudentReference,
                    IdentifierType = PersonIdentifierType.BirthRegistration,
                    IdentifierValue = identifierValue,
                    Purpose = LearnerIdentityPurpose.Admission,
                    RequestedScopes = LearnerDataScope.BasicIdentity
                });
            Assert.Equal(404, guessed.StatusCode);
        }

        await using (var db = ScopedContext(origin.Tenant.Options,
            origin.Tenant.TenantId, origin.Tenant.UserId))
        {
            var reviewer = CohortConsentService(db, origin.Tenant.TenantId,
                origin.Tenant.UserId, "Student");
            var awaiting = await reviewer.GetPendingAsync();
            Assert.True(awaiting.Success, awaiting.Message);
            Assert.Equal(15, awaiting.Data!.Count);

            var outsider = CohortConsentService(db, origin.Tenant.TenantId,
                origin.Tenant.UserId + 1000000, "Student");
            Assert.Equal(404, (await outsider.ResolveAsync(pending[0].Reference,
                new ResolveLearnerIdentityConsentRequestDto
                { Decision = LearnerConsentDecision.Approve })).StatusCode);

            var grants = new List<Guid>();
            foreach (var item in pending)
            {
                var approved = await reviewer.ResolveAsync(item.Reference,
                    new ResolveLearnerIdentityConsentRequestDto
                    { Decision = LearnerConsentDecision.Approve });
                Assert.True(approved.Success, approved.Message);
                Assert.Equal("Approved", approved.Data!.State);
                grants.Add(Assert.IsType<Guid>(approved.Data.GrantReference));

                var replay = await reviewer.ResolveAsync(item.Reference,
                    new ResolveLearnerIdentityConsentRequestDto
                    { Decision = LearnerConsentDecision.Approve });
                Assert.True(replay.Success, replay.Message);
                Assert.True(replay.Data!.AlreadyProcessed);
            }
            Assert.Empty((await reviewer.GetPendingAsync()).Data!);
            Assert.Equal(15, (await reviewer.GetActiveGrantsAsync()).Data!.Count);

            var revoked = await reviewer.RevokeAsync(grants[0]);
            Assert.True(revoked.Success, revoked.Message);
            Assert.Equal("Revoked", revoked.Data!.State);
            Assert.Equal(14, (await reviewer.GetActiveGrantsAsync()).Data!.Count);

            Assert.Equal(1, await db.PersonIdentifiers.CountAsync(x => x.PersonId == origin.PersonId));
            Assert.Equal(origin.PersonId, await db.Students.Where(x => x.Id == origin.StudentId)
                .Select(x => x.PersonId).SingleAsync());
        }

        foreach (var member in cohort.Skip(1))
        {
            await using var db = ScopedContext(member.Tenant.Options,
                member.Tenant.TenantId, member.Tenant.UserId);
            Assert.Equal(member.PersonId, await db.Students
                .Where(x => x.Id == member.StudentId).Select(x => x.PersonId).SingleAsync());
            Assert.Equal(1, await db.StudentPersonLinks.CountAsync(x =>
                x.TenantId == member.Tenant.TenantId && x.StudentId == member.StudentId &&
                x.PersonId == origin.PersonId && !x.IsPrimary && x.UnlinkedAt == null));
            Assert.Equal(1, await db.LearnerDataGrants.CountAsync(x =>
                x.TenantId == member.Tenant.TenantId && x.StudentId == member.StudentId &&
                x.PersonId == origin.PersonId && x.GrantedScopes == "1"));
            Assert.Equal(1, await db.LearnerIdentityAccessLogs.CountAsync(x =>
                x.StudentId == member.StudentId && x.ReasonCode == "CONSENT_APPROVED"));
        }
    }

    [Fact]
    public async Task SQL_shared_person_identifier_append_is_blocked_by_archived_foreign_membership()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        var origin = await SeedAsync(connection);
        var other = await SeedAsync(connection);
        long personId;
        Guid studentReference;
        await using (var db = ScopedContext(origin.Options, origin.TenantId, origin.UserId))
        {
            var student = await db.Students.SingleAsync(x => x.TenantId == origin.TenantId &&
                x.StudentCode.StartsWith("CAP-A-"));
            student.DateOfBirth = new DateOnly(2010, 1, 2);
            personId = student.PersonId;
            studentReference = student.PublicId;
            (await db.Users.SingleAsync(x => x.Id == origin.UserId)).PersonId = personId;
            await db.SaveChangesAsync();

            var local = await CohortIdentityService(db, origin.TenantId, origin.UserId)
                .RegisterOrRequestByReferenceAsync(new RegisterLearnerIdentityByReferenceRequestDto
                {
                    StudentReference = studentReference,
                    IdentifierType = PersonIdentifierType.NationalId,
                    IdentifierValue = "9034567890",
                    Purpose = LearnerIdentityPurpose.Admission,
                    RequestedScopes = LearnerDataScope.BasicIdentity
                });
            Assert.True(local.Success, local.Message);
            Assert.Equal("IdentifierAdded", local.Data!.State);
            Assert.Equal(1, await db.PersonIdentifiers.CountAsync(x => x.PersonId == personId));
        }

        await using (var db = ScopedContext(other.Options, other.TenantId, other.UserId))
        {
            db.TenantMemberships.Add(new TenantMembership
            {
                TenantId = other.TenantId, UserId = origin.UserId, IsDeleted = true
            });
            await db.SaveChangesAsync();
        }

        await using (var db = ScopedContext(origin.Options, origin.TenantId, origin.UserId))
        {
            var rejected = await CohortIdentityService(db, origin.TenantId, origin.UserId)
                .RegisterOrRequestByReferenceAsync(new RegisterLearnerIdentityByReferenceRequestDto
                {
                    StudentReference = studentReference,
                    IdentifierType = PersonIdentifierType.BirthRegistration,
                    IdentifierValue = "20012692510000123",
                    Purpose = LearnerIdentityPurpose.Admission,
                    RequestedScopes = LearnerDataScope.BasicIdentity
                });
            Assert.Equal(409, rejected.StatusCode);
            Assert.Equal(1, await db.PersonIdentifiers.CountAsync(x => x.PersonId == personId));
            Assert.Equal(1, await db.LearnerIdentityAccessLogs.CountAsync(x =>
                x.TenantId == origin.TenantId && x.PersonId == personId &&
                x.ReasonCode == "SHARED_PERSON_IDENTIFIER_REVIEW_REQUIRED"));
        }
    }
}
