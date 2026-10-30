using EduOS.Core.DTOs.Learners;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Settings;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Learners;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Actual V1 learner-consent repository and SQL decision transaction across the
/// 16-institute V2 launch cohort. Unlike PersonId-only sharing tests, this
/// exercises pending requests, approvals, grants, idempotent replay, revocation,
/// guardian relationship removal and preserved per-tenant audit/history.
/// All identities are synthetic and seeded in disposable SQL Server.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    private sealed record ConsentInstitute(Fixture Tenant, long StudentId,
        long PersonId, long GuardianLinkId, Guid RequestReference);

    private sealed class ConsentGuardianUser(long tenant, long user, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => user;
        public long TenantId => tenant;
        public string? FullName => "Synthetic consent reviewer";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string requested) => requested == role;
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "SQL integration acceptance";
    }

    private static LearnerConsentService CohortConsentService(
        EduOSDbContext db, long tenant, long user, string role = "Guardian") =>
        new(new LearnerConsentRepository(db),
            new ConsentGuardianUser(tenant, user, role),
            Options.Create(new LearnerIdentitySettings
            {
                ConsentRequestLifetimeHours = 168,
                DataGrantLifetimeDays = 365
            }),
            TimeProvider.System,
            NullLogger<LearnerConsentService>.Instance);

    [Fact]
    public async Task Sixteen_SQL_institutes_require_real_person_consent_and_isolate_grants_after_guardian_revocation()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        var cohort = new List<ConsentInstitute>(16);
        long sharedGuardianUser = 0;
        long sharedLearnerPerson = 0;
        var now = DateTime.UtcNow;

        for (var i = 0; i < 16; i++)
        {
            var tenant = await SeedAsync(connection);
            var beds = await SeedHostelBedsAsync(tenant, 2, 2);
            var stay = await SeedGuardianHostelStaysAsync(tenant, beds);
            if (i == 0) sharedGuardianUser = tenant.UserId;

            await using var db = ScopedContext(tenant.Options, tenant.TenantId, tenant.UserId);
            var link = await db.StudentGuardians.SingleAsync(x => x.Id == stay.LinkId);
            var student = await db.Students.SingleAsync(x => x.Id == link.StudentId);
            var guardian = await db.Guardians.SingleAsync(x => x.Id == link.GuardianId);

            if (i == 0) sharedLearnerPerson = student.PersonId;
            // A platform Person is explicitly linked to three independent
            // tenant Student rows. No name/phone matching or automatic merge.
            if (i is 1 or 2) student.PersonId = sharedLearnerPerson;
            guardian.UserId = sharedGuardianUser;

            var consent = new LearnerConsentRequest
            {
                TenantId = tenant.TenantId,
                PersonId = student.PersonId,
                RequestedStudentId = student.Id,
                RequestedByUserId = tenant.UserId,
                Purpose = LearnerIdentityPurpose.Admission.ToString(),
                RequestedScopes = ((long)LearnerDataScope.BasicIdentity).ToString(),
                RequestedAt = now,
                ExpiresAt = now.AddDays(7)
            };
            db.LearnerConsentRequests.Add(consent);
            await db.SaveChangesAsync();
            cohort.Add(new ConsentInstitute(tenant, student.Id, student.PersonId,
                link.Id, consent.PublicId));
        }

        Assert.Equal(16, cohort.Select(x => x.Tenant.TenantId).Distinct().Count());
        Assert.Equal(14, cohort.Select(x => x.PersonId).Distinct().Count());
        Assert.Equal(16, cohort.Select(x => x.RequestReference).Distinct().Count());

        // One authenticated guardian can review consent for actively linked
        // learners across institutes, but not for unrelated identities.
        var first = cohort[0];
        await using var reviewerDb = ScopedContext(first.Tenant.Options,
            first.Tenant.TenantId, sharedGuardianUser);
        var reviewer = CohortConsentService(reviewerDb,
            first.Tenant.TenantId, sharedGuardianUser);

        var pending = await reviewer.GetPendingAsync();
        Assert.True(pending.Success, pending.Message);
        Assert.Equal(16, pending.Data!.Count);
        Assert.Equal(16, pending.Data.Select(x => x.Reference).Distinct().Count());

        var outsider = CohortConsentService(reviewerDb,
            first.Tenant.TenantId, sharedGuardianUser + 1000000);
        var outsiderAttempt = await outsider.ResolveAsync(cohort[5].RequestReference,
            new ResolveLearnerIdentityConsentRequestDto { Decision = LearnerConsentDecision.Approve });
        Assert.Equal(404, outsiderAttempt.StatusCode);

        var wrongRole = CohortConsentService(reviewerDb,
            first.Tenant.TenantId, sharedGuardianUser, "Teacher");
        Assert.Equal(403, (await wrongRole.GetPendingAsync()).StatusCode);
        Assert.Equal(403, (await wrongRole.ResolveAsync(cohort[5].RequestReference,
            new ResolveLearnerIdentityConsentRequestDto
            { Decision = LearnerConsentDecision.Approve })).StatusCode);

        var grants = new List<Guid>(16);
        foreach (var member in cohort)
        {
            var approved = await reviewer.ResolveAsync(member.RequestReference,
                new ResolveLearnerIdentityConsentRequestDto
                { Decision = LearnerConsentDecision.Approve });
            Assert.True(approved.Success, approved.Message);
            Assert.Equal("Approved", approved.Data!.State);
            Assert.False(approved.Data.AlreadyProcessed);
            grants.Add(Assert.IsType<Guid>(approved.Data.GrantReference));

            // Replaying an approved decision must not double-grant or double-link.
            var replay = await reviewer.ResolveAsync(member.RequestReference,
                new ResolveLearnerIdentityConsentRequestDto
                { Decision = LearnerConsentDecision.Approve });
            Assert.True(replay.Success, replay.Message);
            Assert.True(replay.Data!.AlreadyProcessed);
            Assert.Equal(grants[^1], replay.Data.GrantReference);
        }

        Assert.Equal(16, (await reviewer.GetActiveGrantsAsync()).Data!.Count);
        Assert.Equal(16, (await reviewer.GetHistoryAsync(1, 20)).Data!.TotalCount);
        Assert.Empty((await reviewer.GetPendingAsync()).Data!);

        // A grant revoked in one institute remains revoked on repeat; it must
        // not affect the same guardian's independent grants in other institutes.
        const int revoked = 8, unlinked = 9;
        var firstRevoke = await reviewer.RevokeAsync(grants[revoked]);
        Assert.True(firstRevoke.Success, firstRevoke.Message);
        Assert.Equal("Revoked", firstRevoke.Data!.State);
        var secondRevoke = await reviewer.RevokeAsync(grants[revoked]);
        Assert.True(secondRevoke.Success, secondRevoke.Message);
        Assert.True(secondRevoke.Data!.AlreadyProcessed);

        // Removing a guardian relationship for a unique learner Person
        // immediately removes the guardian's ability to view/revoke its grant.
        var removed = cohort[unlinked];
        await using (var db = ScopedContext(removed.Tenant.Options,
            removed.Tenant.TenantId, removed.Tenant.UserId))
        {
            (await db.StudentGuardians.SingleAsync(x =>
                x.Id == removed.GuardianLinkId)).IsDeleted = true;
            await db.SaveChangesAsync();
        }

        Assert.Equal(404, (await reviewer.RevokeAsync(grants[unlinked])).StatusCode);
        var visible = await reviewer.GetActiveGrantsAsync();
        Assert.True(visible.Success, visible.Message);
        Assert.Equal(14, visible.Data!.Count);
        Assert.DoesNotContain(visible.Data, x => x.Reference == grants[revoked]);
        Assert.DoesNotContain(visible.Data, x => x.Reference == grants[unlinked]);
        Assert.Contains(visible.Data, x => x.Reference == grants[0]);
        Assert.Contains(visible.Data, x => x.Reference == grants[15]);

        var history = await reviewer.GetHistoryAsync(1, 20);
        Assert.True(history.Success, history.Message);
        Assert.Equal(15, history.Data!.TotalCount);
        Assert.DoesNotContain(history.Data.Items,
            x => x.Reference == removed.RequestReference);

        // Audited SQL state survives revocation and relationship removal.
        // Query-filter bypass is used only in this disposable test verification.
        foreach (var member in cohort)
        {
            await using var db = ScopedContext(member.Tenant.Options,
                member.Tenant.TenantId, member.Tenant.UserId);
            var request = await db.LearnerConsentRequests.SingleAsync(x =>
                x.PublicId == member.RequestReference);
            var grant = await db.LearnerDataGrants.SingleAsync(x =>
                x.LearnerConsentRequestId == request.Id);
            Assert.Equal(member.Tenant.TenantId, grant.TenantId);
            Assert.Equal(member.PersonId, grant.PersonId);
            Assert.Equal(member.StudentId, grant.StudentId);
            Assert.Equal(grants[cohort.IndexOf(member)], grant.PublicId);
            Assert.Equal(revoked == cohort.IndexOf(member) ? GrantState.Revoked : GrantState.Active,
                grant.State);
            Assert.Equal(1, await db.StudentPersonLinks.CountAsync(x =>
                x.TenantId == member.Tenant.TenantId &&
                x.StudentId == member.StudentId && x.PersonId == member.PersonId &&
                x.UnlinkedAt == null));
            Assert.Equal(revoked == cohort.IndexOf(member) ? 2 : 1,
                await db.LearnerIdentityAccessLogs.CountAsync(x =>
                    x.TenantId == member.Tenant.TenantId &&
                    x.LearnerConsentRequestId == request.Id));
        }
    }
}
