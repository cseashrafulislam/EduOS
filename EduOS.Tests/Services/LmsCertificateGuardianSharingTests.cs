using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.LMS;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LmsCertificateGuardianSharingTests
{
    [Fact]
    public async Task Learner_consent_is_required_and_retries_do_not_duplicate_grants()
    {
        await using var db = Database();
        var (certificate, link, guardian) = await Seed(db);
        var student = Service(db, "Student", 101, 77);
        var parent = Service(db, "Guardian", 101, 88);
        Assert.Equal(404, (await parent.GetSharedCertificateAsync(certificate.PublicId)).StatusCode);
        Assert.Empty((await parent.GetSharedCertificatesAsync(1, 20)).Data!.Items);
        Assert.Single((await student.GetEligibleGuardiansAsync(certificate.PublicId)).Data!);

        var request = new GrantLmsCertificateGuardianRequestDto
        {
            CertificateReference = certificate.PublicId,
            GuardianLinkId = link.Id,
            ClientRequestId = Guid.NewGuid()
        };
        var first = await student.GrantAsync(request);
        Assert.True(first.Success, first.Message);
        var replay = await student.GrantAsync(request);
        Assert.Equal(first.Data!.Reference, replay.Data!.Reference);
        var duplicate = await student.GrantAsync(new GrantLmsCertificateGuardianRequestDto
        {
            CertificateReference = certificate.PublicId,
            GuardianLinkId = link.Id,
            ClientRequestId = Guid.NewGuid()
        });
        Assert.Equal(first.Data.Reference, duplicate.Data!.Reference);
        Assert.Single(await db.LmsCertificateGuardianGrants.ToListAsync());
        Assert.True((await parent.GetSharedCertificateAsync(certificate.PublicId)).Success);
        Assert.Single((await parent.GetSharedCertificatesAsync(1, 20)).Data!.Items);
        Assert.Equal(404, (await Service(db, "Guardian", 101, 99)
            .GetSharedCertificateAsync(certificate.PublicId)).StatusCode);
        Assert.Equal(404, (await Service(db, "Guardian", 202, 88)
            .GetSharedCertificateAsync(certificate.PublicId)).StatusCode);
        Assert.Equal(404, (await Service(db, "Student", 101, 999)
            .GetActiveGrantsAsync(certificate.PublicId)).StatusCode);
        Assert.Single((await student.GetActiveGrantsAsync(certificate.PublicId)).Data!);
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.Action == "LMS.CertificateGuardianGranted"));
    }

    [Fact]
    public async Task Relationship_removal_certificate_revocation_and_stale_consent_fail_closed()
    {
        await using var db = Database();
        var (certificate, link, guardian) = await Seed(db);
        var student = Service(db, "Student", 101, 77);
        var parent = Service(db, "Parent", 101, 88);
        var grant = await student.GrantAsync(new GrantLmsCertificateGuardianRequestDto
        {
            CertificateReference = certificate.PublicId,
            GuardianLinkId = link.Id,
            ClientRequestId = Guid.NewGuid()
        });
        Assert.True(grant.Success);
        var row = await db.LmsCertificateGuardianGrants.SingleAsync();
        row.RowVersion = [1, 2, 3, 4];
        await db.SaveChangesAsync();
        var stale = await student.RevokeAsync(grant.Data!.Reference,
            new RevokeLmsCertificateGuardianRequestDto { RowVersion = "AQ==" });
        Assert.Equal(409, stale.StatusCode);
        Assert.True((await parent.GetSharedCertificateAsync(certificate.PublicId)).Success);
        link.IsDeleted = true;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await parent.GetSharedCertificateAsync(certificate.PublicId)).StatusCode);
        Assert.Empty((await parent.GetSharedCertificatesAsync(1, 20)).Data!.Items);
        link.IsDeleted = false;
        await db.SaveChangesAsync();
        guardian.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await parent.GetSharedCertificateAsync(certificate.PublicId)).StatusCode);
        guardian.IsActive = true;
        await db.SaveChangesAsync();
        certificate.IsRevoked = true;
        certificate.RevokedAt = DateTime.UtcNow;
        certificate.RevokedByUserId = 77;
        certificate.RevocationReason = "Correction";
        await db.SaveChangesAsync();
        Assert.Equal(404, (await parent.GetSharedCertificateAsync(certificate.PublicId)).StatusCode);
    }

    [Fact]
    public async Task Revocation_keeps_history_and_regrant_creates_new_consent()
    {
        await using var db = Database();
        var (certificate, link, _) = await Seed(db);
        var student = Service(db, "Student", 101, 77);
        var parent = Service(db, "Guardian", 101, 88);
        var first = await student.GrantAsync(new GrantLmsCertificateGuardianRequestDto
        {
            CertificateReference = certificate.PublicId,
            GuardianLinkId = link.Id,
            ClientRequestId = Guid.NewGuid()
        });
        var row = await db.LmsCertificateGuardianGrants.SingleAsync();
        row.RowVersion = [1, 2, 3, 4];
        await db.SaveChangesAsync();
        var revoked = await student.RevokeAsync(first.Data!.Reference,
            new RevokeLmsCertificateGuardianRequestDto
            { RowVersion = Convert.ToBase64String(row.RowVersion) });
        Assert.True(revoked.Success, revoked.Message);
        Assert.Equal(404, (await parent.GetSharedCertificateAsync(certificate.PublicId)).StatusCode);
        Assert.Empty((await student.GetActiveGrantsAsync(certificate.PublicId)).Data!);
        var second = await student.GrantAsync(new GrantLmsCertificateGuardianRequestDto
        {
            CertificateReference = certificate.PublicId,
            GuardianLinkId = link.Id,
            ClientRequestId = Guid.NewGuid()
        });
        Assert.True(second.Success, second.Message);
        Assert.NotEqual(first.Data.Reference, second.Data!.Reference);
        Assert.Equal(2, await db.LmsCertificateGuardianGrants.CountAsync());
        Assert.True((await parent.GetSharedCertificateAsync(certificate.PublicId)).Success);
        Assert.Equal(2, await db.AuditLogs.CountAsync(x =>
            x.Action == "LMS.CertificateGuardianGranted"));
        Assert.Single(await db.AuditLogs.Where(x =>
            x.Action == "LMS.CertificateGuardianRevoked").ToListAsync());
    }

    private static async Task<(LmsCompletionCertificate Certificate, StudentGuardian Link, Guardian Guardian)> Seed(
        EduOSDbContext db)
    {
        var learner = new Student { TenantId = 101, StudentCode = "LMS-01", FullName = "Learner",
            UserId = 77, StatusCode = "Active" };
        var guardian = new Guardian { TenantId = 101, FullName = "Guardian",
            UserId = 88, IsActive = true, PersonId = 123 };
        var course = new Course { TenantId = 101, Code = "SHARE-01", Title = "Completion", IsActive = true };
        db.AddRange(learner, guardian, course);
        await db.SaveChangesAsync();
        var link = new StudentGuardian { TenantId = 101, StudentId = learner.Id,
            GuardianId = guardian.Id, RelationCode = "Parent" };
        var certificate = new LmsCompletionCertificate
        {
            TenantId = 101, CourseId = course.Id, StudentId = learner.Id, CourseEnrollmentId = 1,
            ClientRequestId = Guid.NewGuid(), PublicId = Guid.NewGuid(),
            StudentNameSnapshot = "Learner", StudentCodeSnapshot = "LMS-01",
            CourseTitleSnapshot = "Completion", CourseCodeSnapshot = "SHARE-01",
            CompletedAtSnapshot = DateTime.UtcNow.AddDays(-1), IssuedAt = DateTime.UtcNow,
            IssuedByUserId = 77
        };
        db.AddRange(link, certificate);
        await db.SaveChangesAsync();
        return (certificate, link, guardian);
    }

    private static EduOSDbContext Database()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("lms-share-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext(); http.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static LmsCertificateGuardianService Service(
        EduOSDbContext db, string role, long tenant, long userId) =>
        new(db, new TestUser(role, tenant, userId), TimeProvider.System,
            NullLogger<LmsCertificateGuardianService>.Instance);

    private sealed class TestUser(string role, long tenant, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
        public long TenantId => tenant;
        public string? FullName => "Test";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
