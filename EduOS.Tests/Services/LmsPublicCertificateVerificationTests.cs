using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.LMS;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LmsPublicCertificateVerificationTests
{
    [Fact]
    public async Task Learner_opt_in_is_private_rotatable_revocable_and_audited()
    {
        await using var db = Db();
        var cert = await Seed(db);
        var service = Service(db);
        var version = Convert.ToBase64String(cert.RowVersion);
        var enabled = await service.EnableAsync(cert.PublicId, new() { RowVersion = version });
        Assert.True(enabled.Success, enabled.Message);
        Assert.Equal(43, enabled.Data!.Token.Length);
        Assert.DoesNotContain(enabled.Data.Token, db.LmsCompletionCertificates.Single().VerificationTokenHash!);
        Assert.Equal(64, db.LmsCompletionCertificates.Single().VerificationTokenHash!.Length);
        Assert.True((await service.GetStatusAsync(cert.PublicId)).Data!.Enabled);
        Assert.Equal(409, (await service.EnableAsync(cert.PublicId,
            new() { RowVersion = "AQID" })).StatusCode);

        var verified = await service.VerifyAsync(enabled.Data.Token);
        Assert.True(verified.Success, verified.Message);
        Assert.Equal("Valid", verified.Data!.Status);
        Assert.Equal("Test Institute", verified.Data.IssuerName);

        var disabled = await service.DisableAsync(cert.PublicId,
            new() { RowVersion = enabled.Data.RowVersion });
        Assert.True(disabled.Success, disabled.Message);
        Assert.False(disabled.Data!.Enabled);
        Assert.Equal(404, (await service.VerifyAsync(enabled.Data.Token)).StatusCode);
        Assert.Equal(2, await db.AuditLogs.CountAsync(x =>
            x.EntityName == "LmsCompletionCertificate" && x.Action.StartsWith("LMS.PublicVerification")));
    }

    [Fact]
    public async Task Anonymous_verification_requires_live_consent_certificate_and_lms_entitlement()
    {
        await using var db = Db();
        var cert = await Seed(db);
        var service = Service(db);
        Assert.Equal(404, (await service.VerifyAsync("invalid")).StatusCode);
        Assert.Equal(404, (await Service(db, 202).GetStatusAsync(cert.PublicId)).StatusCode);
        Assert.Equal(404, (await Service(db, 202).EnableAsync(cert.PublicId,
            new() { RowVersion = Convert.ToBase64String(cert.RowVersion) })).StatusCode);

        var grant = await service.EnableAsync(cert.PublicId,
            new() { RowVersion = Convert.ToBase64String(cert.RowVersion) });
        Assert.True(grant.Success);
        db.TenantModules.Single().IsEnabled = false;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await service.VerifyAsync(grant.Data!.Token)).StatusCode);
        db.TenantModules.Single().IsEnabled = true;
        db.LmsCompletionCertificates.Single().VerificationExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        Assert.Equal(404, (await service.VerifyAsync(grant.Data!.Token)).StatusCode);
        db.LmsCompletionCertificates.Single().VerificationExpiresAt = DateTime.UtcNow.AddDays(1);
        db.LmsCompletionCertificates.Single().IsRevoked = true;
        await db.SaveChangesAsync();
        Assert.Equal(404, (await service.VerifyAsync(grant.Data!.Token)).StatusCode);
    }

    private static EduOSDbContext Db()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("lms-public-verification-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private static async Task<LmsCompletionCertificate> Seed(EduOSDbContext db)
    {
        var now = DateTime.UtcNow;
        db.Tenants.Add(new Tenant
        {
            Id = 101, Code = "T101", Name = "Test Institute", Email = "test@example.invalid",
            State = TenantState.Active, OnboardingStage = OnboardingStage.Completed,
            OnboardingCompletedAt = now.AddDays(-30)
        });
        db.SubscriptionPlans.Add(new SubscriptionPlan { Id = 1, Code = "P", Name = "Test" });
        db.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = 101, SubscriptionPlanId = 1, State = SubscriptionState.Active,
            StartsAt = now.AddDays(-30), EndsAt = now.AddDays(30)
        });
        db.ProductModules.Add(new ProductModule { Id = 11, Code = "LMS", Name = "LMS", IsActive = true });
        db.TenantModules.Add(new TenantModule { TenantId = 101, ProductModuleId = 11, IsEnabled = true });
        var student = new Student
        {
            TenantId = 101, StudentCode = "S1", FullName = "Private Learner",
            UserId = 77, StatusCode = "Active"
        };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var cert = new LmsCompletionCertificate
        {
            TenantId = 101, PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(),
            StudentId = student.Id, CourseId = 1, CourseEnrollmentId = 1, Revision = 1,
            CourseTitleSnapshot = "Private Course", CourseCodeSnapshot = "C1",
            StudentNameSnapshot = student.FullName, StudentCodeSnapshot = student.StudentCode,
            CompletedAtSnapshot = now.AddDays(-1), IssuedAt = now,
            IssuedByUserId = 5, RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }
        };
        db.LmsCompletionCertificates.Add(cert);
        await db.SaveChangesAsync();
        return cert;
    }

    private static LmsPublicCertificateVerificationService Service(EduOSDbContext db, long tenant = 101) =>
        new(db, new TestUser(tenant), TimeProvider.System,
            NullLogger<LmsPublicCertificateVerificationService>.Instance);

    private sealed class TestUser(long tenant) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 77;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public IReadOnlyList<string> Roles => ["Student"];
        public bool IsInRole(string role) => role == "Student";
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
