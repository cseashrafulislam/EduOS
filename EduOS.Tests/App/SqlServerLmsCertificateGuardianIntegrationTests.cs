using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.LMS;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// SQL Server-backed LMS guardian consent race and direct-ID boundaries, using the
/// existing disposable tenant fixture. No real institutional data is touched.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    private sealed class CertificateActor(string role, long tenant, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenant;
        public string? FullName => "SQL certificate actor";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }

    private static LmsCertificateGuardianService CertificateSharingService(
        EduOSDbContext db, string role, long tenant, long userId) =>
        new(db, new CertificateActor(role, tenant, userId), TimeProvider.System,
            NullLogger<LmsCertificateGuardianService>.Instance);

    [Fact]
    public async Task SQL_LMS_certificate_consent_race_and_guardian_direct_id_isolation()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var other = await SeedAsync(connection);
        Guid certificateReference;
        long guardianLinkId;
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var academic = await db.StudentEnrollments.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.PublicId == fixture.Enrollments[0]);
            var learner = await db.Students.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == academic.StudentId);
            learner.UserId = fixture.UserId;
            var person = new Person { FullName = "Disposable certificate guardian" };
            db.Persons.Add(person);
            await db.SaveChangesAsync();
            var guardian = new Guardian
            {
                TenantId = fixture.TenantId, PersonId = person.Id,
                UserId = fixture.UserId, FullName = "Guardian", IsActive = true
            };
            var course = new Course
            {
                TenantId = fixture.TenantId, Code = "SQL-CERT-" + Guid.NewGuid().ToString("N")[..12],
                Title = "SQL completion", IsActive = true
            };
            db.AddRange(guardian, course);
            await db.SaveChangesAsync();
            var link = new StudentGuardian
            {
                TenantId = fixture.TenantId, StudentId = learner.Id,
                GuardianId = guardian.Id, RelationCode = "Parent"
            };
            var enrollment = new CourseEnrollment
            {
                TenantId = fixture.TenantId, CourseId = course.Id, StudentId = learner.Id,
                StudentEnrollmentId = academic.Id, ClientRequestId = Guid.NewGuid(),
                State = EduOS.Core.Enums.Domain.CourseEnrollmentState.Completed,
                EnrolledAt = DateTime.UtcNow.AddDays(-3), CompletedAt = DateTime.UtcNow.AddDays(-1)
            };
            db.AddRange(link, enrollment);
            await db.SaveChangesAsync();
            var certificate = new LmsCompletionCertificate
            {
                TenantId = fixture.TenantId, CourseEnrollmentId = enrollment.Id,
                CourseId = course.Id, StudentId = learner.Id, PublicId = Guid.NewGuid(),
                ClientRequestId = Guid.NewGuid(), StudentNameSnapshot = learner.FullName,
                StudentCodeSnapshot = learner.StudentCode, CourseTitleSnapshot = course.Title,
                CourseCodeSnapshot = course.Code, CompletedAtSnapshot = enrollment.CompletedAt!.Value,
                IssuedAt = DateTime.UtcNow, IssuedByUserId = fixture.UserId
            };
            db.LmsCompletionCertificates.Add(certificate);
            await db.SaveChangesAsync();
            certificateReference = certificate.PublicId;
            guardianLinkId = link.Id;
        }

        var request = new GrantLmsCertificateGuardianRequestDto
        {
            CertificateReference = certificateReference, GuardianLinkId = guardianLinkId,
            ClientRequestId = Guid.NewGuid()
        };
        await using var firstDb = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        await using var secondDb = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var first = CertificateSharingService(firstDb, "Student", fixture.TenantId, fixture.UserId);
        var second = CertificateSharingService(secondDb, "Student", fixture.TenantId, fixture.UserId);
        var attempts = await Task.WhenAll(
            Task.Run(() => first.GrantAsync(request)),
            Task.Run(() => second.GrantAsync(request)));
        Assert.Contains(attempts, x => x.Success);
        Assert.All(attempts, x => Assert.True(x.Success || x.StatusCode == 409, x.Message));
        await using (var verifyDb = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            Assert.Equal(1, await verifyDb.LmsCertificateGuardianGrants.CountAsync(x =>
                x.TenantId == fixture.TenantId && x.CertificateId > 0));
            var parent = CertificateSharingService(verifyDb, "Guardian", fixture.TenantId, fixture.UserId);
            Assert.True((await parent.GetSharedCertificateAsync(certificateReference)).Success);
            Assert.Single((await parent.GetSharedCertificatesAsync(1, 20)).Data!.Items);
            var otherTenant = CertificateSharingService(verifyDb, "Guardian", other.TenantId, fixture.UserId);
            Assert.Equal(404, (await otherTenant.GetSharedCertificateAsync(certificateReference)).StatusCode);
            Assert.Equal(404, (await parent.GetSharedCertificateAsync(Guid.NewGuid())).StatusCode);
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var link = await db.StudentGuardians.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == guardianLinkId);
            link.IsDeleted = true;
            await db.SaveChangesAsync();
        }
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var parent = CertificateSharingService(db, "Guardian", fixture.TenantId, fixture.UserId);
            Assert.Equal(404, (await parent.GetSharedCertificateAsync(certificateReference)).StatusCode);
            Assert.Empty((await parent.GetSharedCertificatesAsync(1, 20)).Data!.Items);
        }
    }
}
