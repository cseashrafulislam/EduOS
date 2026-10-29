using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using EduOS.Service.Services.AssessmentAdministration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PublicTranscriptVerificationIntegrationTests
{
    [Theory]
    [InlineData(false, true, false, false, "Valid")]
    [InlineData(true, true, false, false, "Revoked")]
    [InlineData(false, false, false, false, "ReviewRequired")]
    [InlineData(false, true, true, false, "ReviewRequired")]
    [InlineData(false, true, false, true, "ReviewRequired")]
    public async Task Transcript_status_respects_revocation_tenant_and_latest_publication(
        bool revoked, bool published, bool foreignEvidence, bool superseded, string expected)
    {
        await using var db = Context();
        var reference = Guid.NewGuid();
        await SeedAsync(db, reference, revoked, published, foreignEvidence, superseded);
        var result = await Service(db).VerifyTranscriptAsync(reference);
        result.Success.Should().BeTrue();
        result.Data!.Status.Should().Be(expected);
        result.Data.IssuerName.Should().Be("Institute A");
        result.Data.TranscriptNumber.Should().Be("TRN-TEST");
    }

    [Fact]
    public async Task Unknown_reference_never_returns_private_data()
    {
        await using var db = Context();
        await SeedAsync(db, Guid.NewGuid(), false, true, false, false);
        var result = await Service(db).VerifyTranscriptAsync(Guid.NewGuid());
        result.StatusCode.Should().Be(404);
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task Disabled_exam_module_fails_closed()
    {
        await using var db = Context();
        var reference = Guid.NewGuid();
        await SeedAsync(db, reference, false, true, false, false);
        db.TenantModules.IgnoreQueryFilters().Single().IsEnabled = false;
        await db.SaveChangesAsync();
        (await Service(db).VerifyTranscriptAsync(reference)).StatusCode.Should().Be(404);
    }

    [Theory]
    [InlineData(false, "Valid")]
    [InlineData(true, "ReviewRequired")]
    public async Task Authenticated_document_requires_its_exact_published_version(bool superseded, string expected)
    {
        await using var db = Context();
        var reference = Guid.NewGuid();
        var provider = new EphemeralDataProtectionProvider();
        var snapshot = new EduOS.Core.DTOs.Assessment.TranscriptSnapshotDocumentDto
        {
            TenantId = 1, IssuerName = "Institute A", StudentId = 7,
            TranscriptReference = reference, TranscriptNumber = "TRN-TEST",
            IssueDate = DateOnly.FromDateTime(DateTime.UtcNow), IssuedByUserId = 1,
            AcademicRecord = new EduOS.Core.DTOs.Assessment.TranscriptAcademicRecordDto
            {
                StudentReference = Guid.NewGuid(), Courses =
                [new EduOS.Core.DTOs.Assessment.TranscriptAcademicCourseDto
                {
                    PublicationId = 11, PublicationVersionNo = 1, CreditHours = 3,
                    FullMarks = 100, ObtainedMarks = 75, SubjectCode = "MAT",
                    SubjectName = "Mathematics", GradePoint = 3.5m
                }]
            }
        };
        var protectedRecord = provider.CreateProtector(TranscriptSnapshotIntegrity.Purpose)
            .Protect(JsonSerializer.Serialize(snapshot));
        await SeedAsync(db, reference, false, true, false, superseded, protectedRecord);
        var result = await Service(db, provider).VerifyTranscriptAsync(reference);
        result.Success.Should().BeTrue();
        result.Data!.Status.Should().Be(expected);
    }

    private static EduOSDbContext Context() => new(
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("transcript-verification-" + Guid.NewGuid().ToString("N")).Options);
    private static PublicCertificateVerificationService Service(EduOSDbContext db,
        IDataProtectionProvider? provider = null) =>
        new(db, TimeProvider.System, NullLogger<PublicCertificateVerificationService>.Instance, provider);

    private static async Task SeedAsync(EduOSDbContext db, Guid reference,
        bool revoked, bool published, bool foreignEvidence, bool superseded, string? protectedRecord = null)
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
            new Student { Id = 7, TenantId = 1, StudentCode = "S-7", FullName = "Private Name" },
            new StudentEnrollment { Id = 6, TenantId = 1, StudentId = 7, CampusId = 10,
                AcademicYearId = 11, AcademicBatchId = 5, AcademicProgramId = 12,
                AcademicLevelId = 13, AcademicCurriculumId = 14, RollNo = "01",
                EnrollmentDate = today.AddDays(-20) },
            new Assessment { Id = 8, TenantId = 1, CampusId = 10, AcademicYearId = 11,
                Name = "Final", Code = "FINAL", State = AssessmentState.Locked },
            new TranscriptIssue { Id = 10, TenantId = 1, PublicId = reference,
                StudentId = 7, TranscriptNumber = "TRN-TEST", IssueDate = today,
                IsRevoked = revoked, IssuedByUserId = 1,
                ProtectedAcademicRecord = protectedRecord },
            new ResultPublication { Id = 11, TenantId = foreignEvidence ? 2 : 1,
                AssessmentId = 8, AcademicBatchId = 5, VersionNo = 1,
                State = published ? ResultPublicationState.Published : ResultPublicationState.Withdrawn,
                PublishedAt = now.AddDays(-1), VisibleToStudent = true },
            new StudentResultSummary { Id = 12, TenantId = foreignEvidence ? 2 : 1,
                ResultPublicationId = 11, AssessmentId = 8, StudentEnrollmentId = 6,
                PublicationVersionNo = 1, IsPassed = true, IsWithheld = false }
        );
        if (superseded)
            db.ResultPublications.Add(new ResultPublication
            {
                Id = 13, TenantId = 1, AssessmentId = 8, AcademicBatchId = 5,
                VersionNo = 2, State = ResultPublicationState.Withdrawn,
                PublishedAt = now.AddHours(-1)
            });
        await db.SaveChangesAsync();
    }
}
