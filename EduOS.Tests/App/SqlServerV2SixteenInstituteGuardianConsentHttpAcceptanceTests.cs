using System.Net;
using EduOS.Core.Entities.Students;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Cross-module V1 Person/Student/Guardian -> V2 Hostel HTTP acceptance across the
/// release cohort. All data is generated in the disposable SQL Server test DB.
/// Explicitly links a verified global Person; never infers identity from names.
/// Unlike the entitlement-only matrix, this executes real SQL domain projections,
/// Guardian/Parent authorization, Razor print and CSV with one switching client.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    private sealed record GuardianCohortMember(
        Fixture Tenant, GuardianHostelFixture Stay);

    [Fact]
    public async Task Sixteen_SQL_institutes_keep_shared_person_guardian_consent_and_hostel_print_exports_isolated()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        // 16 distinct SQL tenants, with the contractual 2/3/2/1/3/3/2
        // institute category counts exercised by the separate entitlement matrix.
        // Each tenant has independent students, enrolments, campus, hostel, bed,
        // guardian relationship, pickup consent and private output.
        var cohort = new List<GuardianCohortMember>(16);
        for (var i = 0; i < 16; i++)
        {
            var fixture = await SeedAsync(connection);
            var beds = await SeedHostelBedsAsync(fixture, 2, 2);
            var stay = await SeedGuardianHostelStaysAsync(fixture, beds);
            cohort.Add(new GuardianCohortMember(fixture, stay));
        }
        Assert.Equal(16, cohort.Select(x => x.Tenant.TenantId).Distinct().Count());
        Assert.Equal(16, cohort.Select(x => x.Stay.VisibleCode).Distinct().Count());

        // A known, explicitly linked global guardian Person and user belong to
        // all 16 tenants. One learner Person is deliberately linked to exactly
        // three independent tenant Student records; no automatic identity merge.
        var first = cohort[0];
        long guardianPersonId, studentPersonId;
        await using (var db = ScopedContext(first.Tenant.Options,
            first.Tenant.TenantId, first.Tenant.UserId))
        {
            var link = await db.StudentGuardians.SingleAsync(x => x.Id == first.Stay.LinkId);
            guardianPersonId = (await db.Guardians.SingleAsync(x => x.Id == link.GuardianId)).PersonId;
            studentPersonId = (await db.Students.SingleAsync(x => x.Id == link.StudentId)).PersonId;
        }
        var sharedUserId = first.Tenant.UserId;
        for (var i = 1; i < cohort.Count; i++)
        {
            var member = cohort[i];
            await using var db = ScopedContext(member.Tenant.Options,
                member.Tenant.TenantId, member.Tenant.UserId);
            var link = await db.StudentGuardians.SingleAsync(x => x.Id == member.Stay.LinkId);
            var guardian = await db.Guardians.SingleAsync(x => x.Id == link.GuardianId);
            guardian.PersonId = guardianPersonId;
            guardian.UserId = sharedUserId;
            if (i < 3)
            {
                var student = await db.Students.SingleAsync(x => x.Id == link.StudentId);
                student.PersonId = studentPersonId;
            }
            await db.SaveChangesAsync();
        }

        // The same real SQL-backed service host and HTTP client switch through
        // all 16 tenant claims; no per-tenant mock response markers are used.
        using var server = GuardianHostelHttpServer(first.Tenant.Options);
        using var client = server.CreateClient();

        async Task AssertMemberAsync(int index, bool visible)
        {
            var member = cohort[index];
            foreach (var path in GuardianHostelRoutes)
            {
                using var response = await GuardianHostelGetAsync(client, path,
                    index % 2 == 0 ? "Guardian" : "Parent",
                    member.Tenant.TenantId, sharedUserId);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("no-store",
                    response.Headers.CacheControl?.ToString() ?? "",
                    StringComparison.OrdinalIgnoreCase);
                Assert.Equal("nosniff",
                    Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
                var body = await response.Content.ReadAsStringAsync();
                if (visible)
                    Assert.Contains(member.Stay.VisibleCode, body);
                else
                    Assert.DoesNotContain(member.Stay.VisibleCode, body);
                Assert.DoesNotContain(member.Stay.UnconsentedCode, body);
                foreach (var foreign in cohort.Where(x =>
                    x.Tenant.TenantId != member.Tenant.TenantId))
                    Assert.DoesNotContain(foreign.Stay.VisibleCode, body);
                if (path.Contains("/print", StringComparison.Ordinal))
                {
                    Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
                    Assert.Contains("noindex,nofollow", body);
                }
                if (path.Contains("/export", StringComparison.Ordinal))
                    Assert.Contains("attachment",
                        response.Content.Headers.ContentDisposition?.ToString() ?? "");
            }
        }

        // Both traversal orders and a return to the first tenant catch stale
        // request-scoped tenant context and previously cached private responses.
        foreach (var index in Enumerable.Range(0, 16)
            .Concat(Enumerable.Range(0, 16).Reverse()))
            await AssertMemberAsync(index, visible: true);

        const int revoked = 7;
        await using (var db = ScopedContext(cohort[revoked].Tenant.Options,
            cohort[revoked].Tenant.TenantId, cohort[revoked].Tenant.UserId))
        {
            (await db.StudentGuardians.SingleAsync(x =>
                x.Id == cohort[revoked].Stay.LinkId)).IsAuthorizedPickup = false;
            await db.SaveChangesAsync();
        }
        await AssertMemberAsync(revoked, visible: false);
        await AssertMemberAsync(revoked - 1, visible: true);
        await AssertMemberAsync(revoked + 1, visible: true);

        // Consent regrant restores only the matching tenant. Campus retirement
        // independently suppresses active private data without erasing history.
        await using (var db = ScopedContext(cohort[revoked].Tenant.Options,
            cohort[revoked].Tenant.TenantId, cohort[revoked].Tenant.UserId))
        {
            (await db.StudentGuardians.SingleAsync(x =>
                x.Id == cohort[revoked].Stay.LinkId)).IsAuthorizedPickup = true;
            (await db.Campuses.SingleAsync(x =>
                x.Id == cohort[revoked].Stay.CampusId)).IsActive = false;
            await db.SaveChangesAsync();
        }
        await AssertMemberAsync(revoked, visible: false);
        await AssertMemberAsync(revoked + 1, visible: true);
        await using (var db = ScopedContext(cohort[revoked].Tenant.Options,
            cohort[revoked].Tenant.TenantId, cohort[revoked].Tenant.UserId))
        {
            (await db.Campuses.SingleAsync(x =>
                x.Id == cohort[revoked].Stay.CampusId)).IsActive = true;
            await db.SaveChangesAsync();
        }
        await AssertMemberAsync(revoked, visible: true);

        // Sharing a platform Person is never itself a grant. The wrong account
        // and a role without Guardian/Parent permissions cannot read any tenant.
        foreach (var path in GuardianHostelRoutes)
        {
            using var wrongUser = await GuardianHostelGetAsync(client, path,
                "Guardian", first.Tenant.TenantId, sharedUserId + 999999);
            Assert.Equal(HttpStatusCode.OK, wrongUser.StatusCode);
            Assert.DoesNotContain(first.Stay.VisibleCode,
                await wrongUser.Content.ReadAsStringAsync());

            using var wrongRole = await GuardianHostelGetAsync(client, path,
                "Student", first.Tenant.TenantId, sharedUserId);
            Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);
            Assert.DoesNotContain(first.Stay.VisibleCode,
                await wrongRole.Content.ReadAsStringAsync());
        }

        // Verify that the three explicitly linked tenant-owned Students still
        // exist separately, with independent guardian links and enrollment rows.
        foreach (var member in cohort.Take(3))
        {
            await using var db = ScopedContext(member.Tenant.Options,
                member.Tenant.TenantId, member.Tenant.UserId);
            var link = await db.StudentGuardians.SingleAsync(x => x.Id == member.Stay.LinkId);
            Assert.Equal(studentPersonId,
                (await db.Students.SingleAsync(x => x.Id == link.StudentId)).PersonId);
            Assert.Equal(guardianPersonId,
                (await db.Guardians.SingleAsync(x => x.Id == link.GuardianId)).PersonId);
            Assert.True(await db.StudentEnrollments.AnyAsync(x =>
                x.TenantId == member.Tenant.TenantId && x.StudentId == link.StudentId));
        }
    }
}
