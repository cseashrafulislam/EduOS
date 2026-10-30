using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Service.Services.LMS;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    [Fact]
    public async Task SQL_LMS_public_verification_consent_race_is_single_audited_and_revocable()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        Guid certificateReference;
        string initialVersion;
        long certificateId;

        await using (var setup = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var now = DateTime.UtcNow;
            var tenant = await setup.Tenants.SingleAsync(x => x.Id == fixture.TenantId);
            tenant.OnboardingStage = OnboardingStage.Completed;
            tenant.OnboardingCompletedAt = now.AddDays(-10);
            var plan = new SubscriptionPlan
            {
                Code = "LMS-PUBLIC-" + Guid.NewGuid().ToString("N")[..10], Name = "LMS CI plan"
            };
            var module = await setup.ProductModules.SingleOrDefaultAsync(x => x.Code == "LMS");
            if (module == null)
            {
                module = new ProductModule
                {
                    Code = "LMS", Name = "Learning Management System", IsActive = true
                };
                setup.ProductModules.Add(module);
            }
            setup.SubscriptionPlans.Add(plan);
            await setup.SaveChangesAsync();
            setup.TenantSubscriptions.Add(new TenantSubscription
            {
                TenantId = fixture.TenantId, SubscriptionPlanId = plan.Id,
                State = SubscriptionState.Active, StartsAt = now.AddDays(-10),
                EndsAt = now.AddDays(10)
            });
            setup.TenantModules.Add(new TenantModule
            {
                TenantId = fixture.TenantId, ProductModuleId = module.Id, IsEnabled = true
            });

            var academic = await setup.StudentEnrollments.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.PublicId == fixture.Enrollments[0]);
            var learner = await setup.Students.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == academic.StudentId);
            learner.UserId = fixture.UserId;
            var course = new Course
            {
                TenantId = fixture.TenantId,
                Code = "LMS-PUBLIC-" + Guid.NewGuid().ToString("N")[..10],
                Title = "Private SQL learning course", IsActive = true
            };
            setup.Courses.Add(course);
            await setup.SaveChangesAsync();
            var enrollment = new CourseEnrollment
            {
                TenantId = fixture.TenantId, CourseId = course.Id, StudentId = learner.Id,
                StudentEnrollmentId = academic.Id, ClientRequestId = Guid.NewGuid(),
                EnrolledAt = now.AddDays(-5), CompletedAt = now.AddDays(-1),
                State = CourseEnrollmentState.Completed
            };
            var lesson = new Lesson
            {
                TenantId = fixture.TenantId, CourseId = course.Id,
                Title = "Published lesson", IsPublished = true
            };
            setup.AddRange(enrollment, lesson);
            await setup.SaveChangesAsync();
            setup.LessonProgresses.Add(new LessonProgress
            {
                TenantId = fixture.TenantId, CourseEnrollmentId = enrollment.Id,
                LessonId = lesson.Id, IsCompleted = true, ProgressPercent = 100m
            });
            await setup.SaveChangesAsync();

            var issued = await SqlCertificateWorkflow(setup, fixture.TenantId, fixture.UserId)
                .IssueCertificateAsync(new IssueLmsCertificateRequestDto
                {
                    CourseEnrollmentId = enrollment.Id, ClientRequestId = Guid.NewGuid()
                });
            Assert.True(issued.Success, issued.Message);
            certificateReference = issued.Data!.Reference;
            var certificate = await setup.LmsCompletionCertificates.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.PublicId == certificateReference);
            certificateId = certificate.Id;
            initialVersion = Convert.ToBase64String(certificate.RowVersion);
        }

        var change = new LmsPublicVerificationChangeDto { RowVersion = initialVersion };
        EduOS.Core.Common.ApiResponse<LmsPublicVerificationLinkDto>[] attempts;
        await using (var dbA = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        await using (var dbB = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var a = PublicVerificationService(dbA, fixture.TenantId, fixture.UserId);
            var b = PublicVerificationService(dbB, fixture.TenantId, fixture.UserId);
            attempts = await Task.WhenAll(
                Task.Run(() => a.EnableAsync(certificateReference, change)),
                Task.Run(() => b.EnableAsync(certificateReference, change)));
        }
        Assert.Contains(attempts, x => x.Success);
        Assert.All(attempts, x => Assert.True(x.Success || x.StatusCode == 409, x.Message));
        var winner = Assert.Single(attempts.Where(x => x.Success)).Data!;

        await using (var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            var service = PublicVerificationService(verify, fixture.TenantId, fixture.UserId);
            var certificate = await verify.LmsCompletionCertificates.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.Id == certificateId);
            Assert.NotNull(certificate.VerificationTokenHash);
            Assert.DoesNotContain(winner.Token, certificate.VerificationTokenHash!);
            Assert.Equal(1, await verify.AuditLogs.CountAsync(x =>
                x.TenantId == fixture.TenantId && x.EntityName == "LmsCompletionCertificate" &&
                x.EntityId == certificateId && x.Action == "LMS.PublicVerificationEnabled"));
            var verified = await service.VerifyAsync(winner.Token);
            Assert.True(verified.Success, verified.Message);
            Assert.Equal("Valid", verified.Data!.Status);
            Assert.Equal(404, (await PublicVerificationService(verify, fixture.TenantId + 999, fixture.UserId)
                .GetStatusAsync(certificateReference)).StatusCode);

            var disabled = await service.DisableAsync(certificateReference,
                new LmsPublicVerificationChangeDto
                {
                    RowVersion = Convert.ToBase64String(certificate.RowVersion)
                });
            Assert.True(disabled.Success, disabled.Message);
            Assert.Equal(404, (await service.VerifyAsync(winner.Token)).StatusCode);
            Assert.Equal(1, await verify.AuditLogs.CountAsync(x =>
                x.TenantId == fixture.TenantId && x.EntityId == certificateId &&
                x.Action == "LMS.PublicVerificationDisabled"));
        }

        // Exercise actual MVC, CSRF, rate limiting and anonymous bearer privacy
        // against the disposable SQL certificate produced by this race test.
        await AssertPublicLmsHttpAcceptanceAsync(
            fixture.Options, fixture.TenantId, fixture.UserId, certificateReference);
    }

    private static LmsPublicCertificateVerificationService PublicVerificationService(
        EduOS.Persistence.Context.EduOSDbContext db, long tenant, long userId) =>
        new(db, new LmsPublicVerificationActor(tenant, userId), TimeProvider.System,
            NullLogger<LmsPublicCertificateVerificationService>.Instance);

    private sealed class LmsPublicVerificationActor(long tenant, long userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => userId;
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
