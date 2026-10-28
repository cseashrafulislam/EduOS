using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using EduOS.Service.Services.AssessmentAdministration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PublicCertificateVerificationIntegrationTests
{
    [Theory]
    [InlineData(false, true, false, "Valid")]
    [InlineData(true, true, false, "Revoked")]
    [InlineData(false, false, false, "ReviewRequired")]
    [InlineData(false, true, true, "ReviewRequired")]
    public async Task Verification_respects_revocation_published_evidence_and_tenant_ownership(
        bool revoked, bool published, bool foreignEvidence, string expected)
    {
        await using var db = Context();
        var reference = Guid.NewGuid();
        await SeedAsync(db, reference, revoked, published, foreignEvidence);
        var response = await Service(db).VerifyAsync(reference);
        response.Success.Should().BeTrue();
        response.Data!.Status.Should().Be(expected);
        response.Data.IssuerName.Should().Be("Institute A");
        response.Data.CertificateNumber.Should().Be("CERT-TEST");
    }

    [Fact]
    public async Task Unknown_reference_does_not_reveal_any_tenant_or_student()
    {
        await using var db = Context();
        await SeedAsync(db, Guid.NewGuid(), false, true, false);
        var response = await Service(db).VerifyAsync(Guid.NewGuid());
        response.Success.Should().BeFalse();
        response.StatusCode.Should().Be(404);
        response.Data.Should().BeNull();
    }

    [Fact]
    public async Task Disabled_exam_module_fails_closed_even_with_valid_published_result()
    {
        await using var db = Context();
        var reference = Guid.NewGuid();
        await SeedAsync(db, reference, false, true, false);
        db.TenantModules.IgnoreQueryFilters().Single().IsEnabled = false;
        await db.SaveChangesAsync();
        var response = await Service(db).VerifyAsync(reference);
        response.Success.Should().BeFalse();
        response.StatusCode.Should().Be(404);
    }

    private static EduOSDbContext Context() => new(
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("certificate-verification-" + Guid.NewGuid().ToString("N")).Options);

    private static PublicCertificateVerificationService Service(EduOSDbContext db) =>
        new(db, TimeProvider.System, NullLogger<PublicCertificateVerificationService>.Instance);

    private static async Task SeedAsync(EduOSDbContext db, Guid reference, bool revoked,
        bool published, bool foreignEvidence)
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        db.AddRange(
            new Tenant { Id = 1, Name = "Institute A", Code = "INST-A",
                State = TenantState.Active, OnboardingStage = OnboardingStage.Completed,
                OnboardingCompletedAt = now.AddDays(-20) },
            new ProductModule { Id = 2, Code = "EXAM", Name = "Examinations", IsActive = true },
            new TenantModule { Id = 3, TenantId = 1, ProductModuleId = 2, IsEnabled = true },
            new TenantSubscription { Id = 4, TenantId = 1, SubscriptionPlanId = 1,
                State = SubscriptionState.Active, StartsAt = now.AddDays(-20), EndsAt = now.AddDays(20) },
            new AcademicBatch { Id = 5, TenantId = 1, CampusId = 10, AcademicYearId = 11,
                AcademicProgramId = 12, AcademicLevelId = 13, Name = "Batch A", Code = "B-A" },
            new StudentEnrollment { Id = 6, TenantId = 1, StudentId = 7, CampusId = 10,
                AcademicYearId = 11, AcademicBatchId = 5, AcademicProgramId = 12,
                AcademicLevelId = 13, AcademicCurriculumId = 14, RollNo = "01",
                EnrollmentDate = today.AddDays(-20) },
            new Assessment { Id = 8, TenantId = 1, CampusId = 10, AcademicYearId = 11,
                Name = "Final", Code = "FINAL", State = AssessmentState.Locked },
            new CertificateTemplate { Id = 9, TenantId = 1, Name = "Certificate",
                Code = "CERT", HtmlTemplate = "<p>Certificate</p>" },
            new CertificateIssue { Id = 10, TenantId = 1, PublicId = reference,
                StudentId = 7, StudentEnrollmentId = 6, CertificateTemplateId = 9,
                CertificateNumber = "CERT-TEST", IssueDate = today, IsRevoked = revoked,
                IssuedByUserId = 1 },
            new ResultPublication { Id = 11, TenantId = foreignEvidence ? 2 : 1,
                AssessmentId = 8, AcademicBatchId = 5, VersionNo = 1,
                State = published ? ResultPublicationState.Published : ResultPublicationState.Withdrawn,
                PublishedAt = now.AddDays(-1), VisibleToStudent = true },
            new StudentResultSummary { Id = 12, TenantId = foreignEvidence ? 2 : 1,
                ResultPublicationId = 11, AssessmentId = 8, StudentEnrollmentId = 6,
                PublicationVersionNo = 1, IsPassed = true, IsWithheld = false }
        );
        await db.SaveChangesAsync();
    }
}
