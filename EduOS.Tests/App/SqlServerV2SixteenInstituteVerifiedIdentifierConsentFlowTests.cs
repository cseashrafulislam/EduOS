using EduOS.Core.DTOs.Learners;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
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
/// SQL-backed V1 learner identity -> V2 institute isolation acceptance. Exercises
/// production registration, protected identifier lookup, consent request, guardian
/// decision, per-tenant grant and revocation rather than inserting consent rows.
/// The test fixture explicitly simulates an independently verified identifier;
/// it does not assert that a government identity-verification provider exists.
/// All data and lookup keys are disposable synthetic test values.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    private static readonly string CohortIdentityLookupKey = Convert.ToBase64String(
        Enumerable.Range(1, 32).Select(x => (byte)(x + 17)).ToArray());

    private sealed record VerifiedIdentityMember(Fixture Tenant, long StudentId, Guid StudentReference);

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
            db,
            new TenantAdmin(tenantId, userId),
            new LearnerIdentifierProtector(settings, new EphemeralDataProtectionProvider()),
            settings,
            NullLogger<LearnerIdentityService>.Instance);
    }

    [Fact]
    public async Task Sixteen_SQL_institutes_require_verified_identifier_then_guardian_consent_before_cross_tenant_link()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        const string syntheticBirthRegistration = "20012692510000123";
        var members = new List<VerifiedIdentityMember>(16);
        foreach (var i in Enumerable.Range(0, 16))
        {
            var tenant = await SeedAsync(connection);
            await using var db = ScopedContext(tenant.Options, tenant.TenantId, tenant.UserId);
            var student = new Student
            {
                TenantId = tenant.TenantId,
                StudentCode = "IDENT-" + Guid.NewGuid().ToString("N")[..12],
                FullName = i == 0 ? "Synthetic private origin learner" : "Synthetic applicant " + i,
                DateOfBirth = new DateOnly(2010, 1, 2),
                Gender = "Male",
                AdmissionDate = DateOnly.FromDateTime(DateTime.UtcNow),
                StatusCode = "Active"
            };
            db.Students.Add(student);
            await db.SaveChangesAsync();
            members.Add(new VerifiedIdentityMember(tenant, student.Id, student.PublicId));
        }

        Assert.Equal(16, members.Select(x => x.Tenant.TenantId).Distinct().Count());
        var origin = members[0];
        long personId;
        await using (var db = ScopedContext(origin.Tenant.Options, origin.Tenant.TenantId, origin.Tenant.UserId))
        {
            var identity = CohortIdentityService(db, origin.Tenant.TenantId, origin.Tenant.UserId);
            var created = await identity.RegisterOrRequestByReferenceAsync(new RegisterLearnerIdentityByReferenceRequestDto
            {
                StudentReference = origin.StudentReference,
                IdentifierType = PersonIdentifierType.BirthRegistration,
                IdentifierValue = syntheticBirthRegistration,
                Purpose = LearnerIdentityPurpose.Admission,
                RequestedScopes = LearnerDataScope.BasicIdentity
            });
            Assert.True(created.Success, created.Message);
            Assert.Equal(201, created.StatusCode);
            Assert.Equal("Created", created.Data!.State);

            var identifier = await db.PersonIdentifiers.SingleAsync();
            personId = identifier.PersonId;
            Assert.False(identifier.IsVerified);
            Assert.DoesNotContain(syntheticBirthRegistration, identifier.ProtectedValue);
            Assert.DoesNotContain(syntheticBirthRegistration, identifier.LookupDigest);

            var guardian = new EduOS.Core.Entities.Students.Guardian
            {
                TenantId = origin.Tenant.TenantId,
                UserId = origin.Tenant.UserId,
                FullName = "Synthetic authorized guardian",
                Phone = "01700000000",
                IsActive = true
            };
            db.Guardians.Add(guardian);
            await db.SaveChangesAsync();
            db.StudentGuardians.Add(new StudentGuardian
            {
                TenantId = origin.Tenant.TenantId,
                StudentId = origin.StudentId,
                GuardianId = guardian.Id,
                RelationCode = "Parent",
                IsPrimary = true
            });
            await db.SaveChangesAsync();
        }

        // Before independent verification, a foreign institution must receive
        // a neutral conflict and must not create a request, grant or Person link.
        var firstTarget = members[1];
        await using (var db = ScopedContext(firstTarget.Tenant.Options,
            firstTarget.Tenant.TenantId, firstTarget.Tenant.UserId))
        {
            var denied = await CohortIdentityService(db, firstTarget.Tenant.TenantId,
                firstTarget.Tenant.UserId).RegisterOrRequestByReferenceAsync(new RegisterLearnerIdentityByReferenceRequestDto
                {
                    StudentReference = firstTarget.StudentReference,
                    IdentifierType = PersonIdentifierType.BirthRegistration,
                    IdentifierValue = syntheticBirthRegistration,
                    Purpose = LearnerIdentityPurpose.Admission,
                    RequestedScopes = LearnerDataScope.BasicIdentity
                });
            Assert.Equal(409, denied.StatusCode);
            Assert.Null(denied.Data);
            Assert.Equal(0, await db.LearnerConsentRequests.CountAsync());
            Assert.Equal(0, await db.StudentPersonLinks.CountAsync());
            Assert.Equal(0, await db.LearnerDataGrants.CountAsync());
            Assert.Contains(await db.LearnerIdentityAccessLogs.Select(x => x.ReasonCode).ToListAsync(),
                x => x == "IDENTIFIER_VERIFICATION_REQUIRED");
        }

        // The fixture represents an external, audited verification decision.
        // Never simulate this by automatically matching name, DOB or phone.
        await using (var db = ScopedContext(origin.Tenant.Options,
            origin.Tenant.TenantId, origin.Tenant.UserId))
        {
            var identifier = await db.PersonIdentifiers.SingleAsync(x => x.PersonId == personId);
            identifier.IsVerified = true;
            identifier.VerifiedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var requests = new List<(VerifiedIdentityMember Member, Guid Reference)>(15);
        foreach (var member in members.Skip(1))
        {
            await using var db = ScopedContext(member.Tenant.Options,
                member.Tenant.TenantId, member.Tenant.UserId);
            var identity = CohortIdentityService(db, member.Tenant.TenantId, member.Tenant.UserId);
            var requested = await identity.RegisterOrRequestByReferenceAsync(new RegisterLearnerIdentityByReferenceRequestDto
            {
                StudentReference = member.StudentReference,
                IdentifierType = PersonIdentifierType.BirthRegistration,
                IdentifierValue = syntheticBirthRegistration,
                Purpose = LearnerIdentityPurpose.Admission,
                RequestedScopes = LearnerDataScope.BasicIdentity
            });
            Assert.True(requested.Success, requested.Message);
            Assert.Equal(202, requested.StatusCode);
            Assert.Equal("ConsentRequired", requested.Data!.State);
            Assert.Null(requested.Data.PersonReference);
            Assert.NotNull(requested.Data.ConsentRequestReference);
            Assert.DoesNotContain("Synthetic private origin learner", requested.Message);
            requests.Add((member, requested.Data.ConsentRequestReference!.Value));

            Assert.Equal(0, await db.StudentPersonLinks.CountAsync(x => x.StudentId == member.StudentId));
            Assert.Equal(0, await db.LearnerDataGrants.CountAsync());
            Assert.Equal(0, await db.Students.Where(x => x.Id == member.StudentId)
                .Select(x => x.PersonId).SingleAsync());
        }

        Assert.Equal(15, requests.Select(x => x.Reference).Distinct().Count());
        await using var reviewDb = ScopedContext(origin.Tenant.Options,
            origin.Tenant.TenantId, origin.Tenant.UserId);
        var guardianReviewer = CohortConsentService(reviewDb, origin.Tenant.TenantId,
            origin.Tenant.UserId, "Guardian");
        var outsider = CohortConsentService(reviewDb, origin.Tenant.TenantId,
            origin.Tenant.UserId + 999999, "Guardian");
        Assert.Equal(15, (await guardianReviewer.GetPendingAsync()).Data!.Count);
        Assert.Equal(404, (await outsider.ResolveAsync(requests[0].Reference,
            new ResolveLearnerIdentityConsentRequestDto
            { Decision = LearnerConsentDecision.Approve })).StatusCode);

        var grantReferences = new List<Guid>(15);
        foreach (var (member, reference) in requests)
        {
            var approved = await guardianReviewer.ResolveAsync(reference,
                new ResolveLearnerIdentityConsentRequestDto
                { Decision = LearnerConsentDecision.Approve });
            Assert.True(approved.Success, approved.Message);
            Assert.Equal("Approved", approved.Data!.State);
            grantReferences.Add(Assert.IsType<Guid>(approved.Data.GrantReference));

        }

        Assert.Equal(15, (await guardianReviewer.GetActiveGrantsAsync()).Data!.Count);
        Assert.Equal(15, (await guardianReviewer.GetHistoryAsync(1, 20)).Data!.TotalCount);

        // Revoking one grant cannot revoke another tenant's independent grant.
        var revoked = await guardianReviewer.RevokeAsync(grantReferences[3]);
        Assert.True(revoked.Success, revoked.Message);
        Assert.Equal("Revoked", revoked.Data!.State);
        var active = await guardianReviewer.GetActiveGrantsAsync();
        Assert.Equal(14, active.Data!.Count);
        Assert.DoesNotContain(active.Data, x => x.Reference == grantReferences[3]);
        Assert.Contains(active.Data, x => x.Reference == grantReferences[4]);

        // Removing the origin guardian relationship immediately denies every
        // foreign grant readback without deleting any tenant's audit history.
        await using (var db = ScopedContext(origin.Tenant.Options,
            origin.Tenant.TenantId, origin.Tenant.UserId))
        {
            var link = await db.StudentGuardians.SingleAsync(x => x.StudentId == origin.StudentId);
            link.IsDeleted = true;
            await db.SaveChangesAsync();
        }
        Assert.Empty((await guardianReviewer.GetActiveGrantsAsync()).Data!);
        Assert.Empty((await guardianReviewer.GetPendingAsync()).Data!);
        Assert.Equal(404, (await guardianReviewer.RevokeAsync(grantReferences[4])).StatusCode);

        foreach (var (member, reference) in requests)
        {
            await using var verify = ScopedContext(member.Tenant.Options,
                member.Tenant.TenantId, member.Tenant.UserId);
            Assert.Equal(1, await verify.LearnerConsentRequests.CountAsync(x =>
                x.TenantId == member.Tenant.TenantId && x.PublicId == reference));
            var link = await verify.StudentPersonLinks.SingleAsync(x =>
                x.TenantId == member.Tenant.TenantId && x.StudentId == member.StudentId);
            Assert.Equal(personId, link.PersonId);
            var grant = await verify.LearnerDataGrants.SingleAsync(x =>
                x.TenantId == member.Tenant.TenantId && x.StudentId == member.StudentId);
            Assert.Equal(grantReferences[requests.IndexOf((member, reference))], grant.PublicId);
            Assert.Equal(member.Tenant.TenantId, grant.TenantId);
            Assert.Equal(1, await verify.LearnerDataGrants.CountAsync(x =>
                x.TenantId == member.Tenant.TenantId && x.StudentId == member.StudentId));
            Assert.Equal(1, await verify.StudentPersonLinks.CountAsync(x =>
                x.TenantId == member.Tenant.TenantId && x.StudentId == member.StudentId));
        }
    }
}
