using System.Net;
using System.Security.Cryptography;
using System.Text;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Helpers.Storage;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// V1 Student/Campus -> V2 Library private-byte acceptance for the 16-institute
/// launch cohort. Uses the existing disposable SQL Server fixture and the real
/// HTTP controller/service/EF query filters. A single HTTP client switches
/// between institutes, including three Student rows explicitly sharing one
/// platform Person. No customer data, external storage or production writes.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    private sealed record PrivateFileCohortMember(
        Fixture Tenant, Guid Reference, string Key, byte[] Bytes);

    [Fact]
    public async Task Sixteen_SQL_institutes_isolate_real_private_download_bytes_across_switches_and_revocations()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        var cohort = new List<PrivateFileCohortMember>(16);
        for (var i = 0; i < 16; i++)
        {
            var fixture = await SeedAsync(connection);
            var asset = await SeedDigitalResourceAsync(fixture);
            // The shared fixture's default PDF bytes are intentionally identical.
            // Give every tenant different bytes and persist the matching integrity
            // metadata so this test detects an actual cross-tenant byte response.
            var bytes = Encoding.UTF8.GetBytes(
                $"%PDF-1.4\nEduOS synthetic private tenant {fixture.TenantId} {Guid.NewGuid():N}\n");
            await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
            {
                var file = await db.FileAssets.SingleAsync(x =>
                    x.TenantId == fixture.TenantId && x.PublicId == asset.Reference);
                file.SizeBytes = bytes.Length;
                file.Sha256 = Convert.ToHexString(SHA256.HashData(bytes));
                await db.SaveChangesAsync();
            }
            cohort.Add(new PrivateFileCohortMember(
                fixture, asset.Reference, asset.Key, bytes));
        }

        Assert.Equal(16, cohort.Select(x => x.Tenant.TenantId).Distinct().Count());
        Assert.Equal(16, cohort.Select(x => x.Reference).Distinct().Count());
        Assert.Equal(16, cohort.Select(x => Convert.ToHexString(SHA256.HashData(x.Bytes)))
            .Distinct().Count());

        // One synthetic user has an explicitly assigned Student record in every
        // tenant. The first three tenant-owned Students share a platform Person.
        // Neither shared UserId nor PersonId is permission to read a foreign row.
        var sharedUserId = cohort[0].Tenant.UserId;
        long sharedPersonId;
        await using (var db = ScopedContext(cohort[0].Tenant.Options,
            cohort[0].Tenant.TenantId, cohort[0].Tenant.UserId))
        {
            var enrollment = await db.StudentEnrollments.SingleAsync(x =>
                x.PublicId == cohort[0].Tenant.Enrollments[0]);
            sharedPersonId = (await db.Students.SingleAsync(x =>
                x.Id == enrollment.StudentId)).PersonId;
        }
        for (var i = 0; i < cohort.Count; i++)
        {
            var member = cohort[i];
            await using var db = ScopedContext(member.Tenant.Options,
                member.Tenant.TenantId, member.Tenant.UserId);
            var enrollment = await db.StudentEnrollments.SingleAsync(x =>
                x.PublicId == member.Tenant.Enrollments[0]);
            var student = await db.Students.SingleAsync(x => x.Id == enrollment.StudentId);
            student.UserId = sharedUserId;
            if (i < 3) student.PersonId = sharedPersonId;
            await db.SaveChangesAsync();
        }

        var storage = new Mock<IFileUploadService>();
        foreach (var member in cohort)
        {
            storage.Setup(x => x.GetPrivateFileAsync(member.Key))
                .ReturnsAsync(new FileDownloadResult
                {
                    Content = member.Bytes, ContentType = "application/pdf"
                });
        }
        using var server = PrivateSqlServer(cohort[0].Tenant.Options, storage);
        using var client = server.CreateClient();

        async Task AssertAllowedAsync(int index)
        {
            var member = cohort[index];
            using var response = await PrivateGetAsync(client, member.Reference,
                "Student", member.Tenant.TenantId, sharedUserId);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertPrivateCache(response);
            Assert.Equal("nosniff",
                Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(member.Bytes, await response.Content.ReadAsByteArrayAsync());
        }

        async Task AssertDeniedAsync(int callerIndex, int resourceIndex)
        {
            var caller = cohort[callerIndex];
            var target = cohort[resourceIndex];
            using var response = await PrivateGetAsync(client, target.Reference,
                "Student", caller.Tenant.TenantId, sharedUserId);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            AssertPrivateCache(response);
            Assert.False((await response.Content.ReadAsByteArrayAsync())
                .SequenceEqual(target.Bytes));
        }

        // Reuse one HTTP client, visit all tenants in both directions, and
        // probe another tenant's real document ID on every switch.
        foreach (var index in Enumerable.Range(0, 16))
            await AssertAllowedAsync(index);
        foreach (var index in Enumerable.Range(0, 16).Reverse())
        {
            await AssertDeniedAsync(index, (index + 1) % 16);
            await AssertAllowedAsync(index);
        }
        storage.Verify(x => x.GetPrivateFileAsync(It.IsAny<string>()), Times.Exactly(32));

        using (var guessed = await PrivateGetAsync(client, Guid.NewGuid(),
            "Student", cohort[0].Tenant.TenantId, sharedUserId))
        {
            Assert.Equal(HttpStatusCode.NotFound, guessed.StatusCode);
            AssertPrivateCache(guessed);
        }
        using (var wrongRole = await PrivateGetAsync(client, cohort[0].Reference,
            "Guardian", cohort[0].Tenant.TenantId, sharedUserId))
        {
            Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);
            AssertPrivateCache(wrongRole);
        }

        // Three independent SQL revocations must take effect on the very next
        // HTTP request, without deleting historical file or document records.
        const int campusIndex = 5, withdrawalIndex = 9, documentIndex = 12;
        await using (var db = ScopedContext(cohort[campusIndex].Tenant.Options,
            cohort[campusIndex].Tenant.TenantId, cohort[campusIndex].Tenant.UserId))
        {
            var enrollment = await db.StudentEnrollments.SingleAsync(x =>
                x.PublicId == cohort[campusIndex].Tenant.Enrollments[0]);
            (await db.Campuses.SingleAsync(x => x.Id == enrollment.CampusId)).IsActive = false;
            await db.SaveChangesAsync();
        }
        await using (var db = ScopedContext(cohort[withdrawalIndex].Tenant.Options,
            cohort[withdrawalIndex].Tenant.TenantId, cohort[withdrawalIndex].Tenant.UserId))
        {
            var enrollment = await db.StudentEnrollments.SingleAsync(x =>
                x.PublicId == cohort[withdrawalIndex].Tenant.Enrollments[0]);
            enrollment.IsCurrent = false;
            enrollment.State = EduOS.Core.Enums.Domain.EnrollmentState.Withdrawn;
            await db.SaveChangesAsync();
        }
        await using (var db = ScopedContext(cohort[documentIndex].Tenant.Options,
            cohort[documentIndex].Tenant.TenantId, cohort[documentIndex].Tenant.UserId))
        {
            (await db.Documents.SingleAsync(x =>
                x.PublicId == cohort[documentIndex].Reference)).IsActive = false;
            await db.SaveChangesAsync();
        }

        foreach (var index in new[] { campusIndex, withdrawalIndex, documentIndex })
        {
            using var response = await PrivateGetAsync(client, cohort[index].Reference,
                "Student", cohort[index].Tenant.TenantId, sharedUserId);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            AssertPrivateCache(response);
            Assert.False((await response.Content.ReadAsByteArrayAsync())
                .SequenceEqual(cohort[index].Bytes));
        }

        // Adjacent institutes are unaffected by the other tenants' revocations.
        foreach (var index in new[] { campusIndex - 1, withdrawalIndex - 1, documentIndex - 1 })
            await AssertAllowedAsync(index);

        storage.Verify(x => x.GetPrivateFileAsync(It.IsAny<string>()), Times.Exactly(35));
        foreach (var index in new[] { campusIndex, withdrawalIndex, documentIndex })
        {
            var member = cohort[index];
            await using var db = ScopedContext(member.Tenant.Options,
                member.Tenant.TenantId, member.Tenant.UserId);
            Assert.Equal(1, await db.Documents.CountAsync(x =>
                x.TenantId == member.Tenant.TenantId && x.PublicId == member.Reference));
            Assert.Equal(1, await db.FileAssets.CountAsync(x =>
                x.TenantId == member.Tenant.TenantId && x.PublicId == member.Reference));
        }
    }
}
