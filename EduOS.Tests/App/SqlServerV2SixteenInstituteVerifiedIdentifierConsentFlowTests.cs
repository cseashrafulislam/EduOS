using EduOS.Core.DTOs.Learners;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;
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
/// SQL-backed boundary regression across the 16-institute cohort. The real SQL
/// schema requires Student.PersonId to reference an existing Person, whereas
/// LearnerIdentityService's consent-request path currently expects an unlinked
/// Student (PersonId == 0). Until the V1 identity owner reconciles that contract,
/// a verified identifier belonging to another Person must fail closed: never
/// replace the tenant's canonical Student.PersonId or silently create a grant.
/// This is a NEGATIVE security gate, not a completed verified-consent lifecycle.
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
            db,
            new TenantAdmin(tenantId, userId),
            new LearnerIdentifierProtector(settings, new EphemeralDataProtectionProvider()),
            settings,
            NullLogger<LearnerIdentityService>.Instance);
    }

    [Fact]
    public async Task Sixteen_SQL_institutes_never_replace_an_existing_person_when_foreign_identifier_is_verified()
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

            Assert.False(response.Success);
            Assert.Equal(409, response.StatusCode);
            Assert.Null(response.Data);
            Assert.DoesNotContain(identifierValue, response.Message);
            Assert.Equal(member.PersonId, await db.Students
                .Where(x => x.Id == member.StudentId)
                .Select(x => x.PersonId).SingleAsync());
            Assert.Equal(0, await db.StudentPersonLinks.CountAsync(x =>
                x.TenantId == member.Tenant.TenantId && x.StudentId == member.StudentId));
            Assert.Equal(0, await db.LearnerConsentRequests.CountAsync());
            Assert.Equal(0, await db.LearnerDataGrants.CountAsync());
            Assert.Contains(await db.LearnerIdentityAccessLogs
                .Where(x => x.StudentId == member.StudentId)
                .Select(x => x.ReasonCode).ToListAsync(),
                x => x == "IDENTIFIER_PERSON_CONFLICT");

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
            Assert.Equal(1, await db.PersonIdentifiers.CountAsync(x => x.PersonId == origin.PersonId));
            Assert.Equal(0, await db.LearnerConsentRequests.CountAsync());
            Assert.Equal(0, await db.LearnerDataGrants.CountAsync());
            Assert.Equal(origin.PersonId, await db.Students.Where(x => x.Id == origin.StudentId)
                .Select(x => x.PersonId).SingleAsync());
        }
    }
}
