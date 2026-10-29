using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.DataProtection;

namespace EduOS.Service.Services.AssessmentAdministration;

/// <summary>
/// A public, read-only bearer-reference verification boundary. The random certificate PublicId
/// is printed as part of its number; possession permits checking status, never reading student data.
/// All query-filter bypasses below have explicit tenant equality and narrow projections.
/// </summary>
public sealed class PublicCertificateVerificationService : IPublicCertificateVerificationService
{
    private readonly EduOSDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<PublicCertificateVerificationService> _logger;
    private readonly IDataProtector? _transcriptProtector;

    public PublicCertificateVerificationService(EduOSDbContext db, TimeProvider clock,
        ILogger<PublicCertificateVerificationService> logger,
        IDataProtectionProvider? transcriptProtection = null)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
        _transcriptProtector = transcriptProtection?.CreateProtector(TranscriptSnapshotIntegrity.Purpose);
    }

    public async Task<ApiResponse<PublicCertificateVerificationDto>> VerifyAsync(
        Guid reference, CancellationToken cancellationToken = default)
    {
        if (reference == Guid.Empty)
            return ApiResponse<PublicCertificateVerificationDto>.ErrorResponse("Credential not found.", 404);

        // The unguessable public reference is the only cross-tenant lookup. Fail closed on
        // ambiguous/corrupt duplicates. Do not return student, guardian, roll or enrollment IDs.
        var matches = await _db.CertificateIssues.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.PublicId == reference && !x.IsDeleted)
            .Select(x => new
            {
                x.TenantId, x.StudentId, x.StudentEnrollmentId, x.CertificateTemplateId,
                x.CertificateNumber, x.IssueDate, x.IsRevoked
            }).Take(2).ToListAsync(cancellationToken);
        if (matches.Count != 1)
            return ApiResponse<PublicCertificateVerificationDto>.ErrorResponse("Credential not found.", 404);

        var certificate = matches[0];
        var tenant = await _db.Tenants.AsNoTracking()
            .Where(x => x.Id == certificate.TenantId && !x.IsDeleted &&
                x.State == TenantState.Active && x.OnboardingStage == OnboardingStage.Completed &&
                x.OnboardingCompletedAt != null)
            .Select(x => new { x.Id, x.Name }).SingleOrDefaultAsync(cancellationToken);
        if (tenant == null)
            return ApiResponse<PublicCertificateVerificationDto>.ErrorResponse("Credential not found.", 404);

        var now = _clock.GetUtcNow().UtcDateTime;
        var entitled = await _db.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.TenantId == tenant.Id && !x.IsDeleted &&
                x.StartsAt <= now && x.EndsAt > now &&
                (x.State == SubscriptionState.Active || x.State == SubscriptionState.Trial ||
                 x.State == SubscriptionState.Grace), cancellationToken);
        if (!entitled)
            return ApiResponse<PublicCertificateVerificationDto>.ErrorResponse("Credential verification unavailable.", 404);

        var enabled = await (
            from selection in _db.TenantModules.IgnoreQueryFilters().AsNoTracking()
            join module in _db.ProductModules.AsNoTracking()
                on selection.ProductModuleId equals module.Id
            where selection.TenantId == tenant.Id && !selection.IsDeleted &&
                selection.IsEnabled && module.IsActive && !module.IsDeleted &&
                module.Code == "EXAM"
            select selection.Id).AnyAsync(cancellationToken);
        if (!enabled)
            return ApiResponse<PublicCertificateVerificationDto>.ErrorResponse("Credential verification unavailable.", 404);

        // Verify historical issuance provenance and *current* publication status. A withdrawn
        // result must not continue to verify as valid. This does not mutate published history.
        var issuedBefore = certificate.IssueDate.ToDateTime(TimeOnly.MaxValue);
        var evidence = await (
            from enrollment in _db.StudentEnrollments.IgnoreQueryFilters().AsNoTracking()
            join batch in _db.AcademicBatches.IgnoreQueryFilters().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.AcademicBatchId }
                equals new { batch.TenantId, batch.Id }
            join summary in _db.StudentResultSummaries.IgnoreQueryFilters().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.Id }
                equals new { summary.TenantId, Id = summary.StudentEnrollmentId }
            join publication in _db.ResultPublications.IgnoreQueryFilters().AsNoTracking()
                on new { summary.TenantId, Id = summary.ResultPublicationId }
                equals new { publication.TenantId, publication.Id }
            join assessment in _db.Assessments.IgnoreQueryFilters().AsNoTracking()
                on new { publication.TenantId, Id = publication.AssessmentId }
                equals new { assessment.TenantId, assessment.Id }
            where enrollment.TenantId == tenant.Id && !enrollment.IsDeleted &&
                enrollment.Id == certificate.StudentEnrollmentId &&
                enrollment.StudentId == certificate.StudentId &&
                !batch.IsDeleted && !summary.IsDeleted && !publication.IsDeleted &&
                !assessment.IsDeleted && assessment.State != AssessmentState.Cancelled &&
                enrollment.CampusId == batch.CampusId &&
                enrollment.AcademicYearId == batch.AcademicYearId &&
                assessment.CampusId == batch.CampusId &&
                assessment.AcademicYearId == batch.AcademicYearId &&
                publication.AcademicBatchId == batch.Id &&
                summary.AssessmentId == assessment.Id &&
                summary.PublicationVersionNo == publication.VersionNo &&
                summary.IsPassed && !summary.IsWithheld &&
                publication.State == ResultPublicationState.Published &&
                publication.VisibleToStudent && publication.PublishedAt != null &&
                publication.PublishedAt <= issuedBefore
            select summary.Id).AnyAsync(cancellationToken);

        var templateExists = await _db.CertificateTemplates.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.TenantId == tenant.Id && x.Id == certificate.CertificateTemplateId &&
                !x.IsDeleted, cancellationToken);

        var status = certificate.IsRevoked ? "Revoked" :
            evidence && templateExists && certificate.IssueDate <= DateOnly.FromDateTime(now)
                ? "Valid" : "ReviewRequired";
        // Privacy-safe observability: no certificate token, student identifier or raw PII.
        _logger.LogInformation("Public certificate verification: tenant {TenantId}, status {Status}",
            tenant.Id, status);
        return ApiResponse<PublicCertificateVerificationDto>.SuccessResponse(
            new PublicCertificateVerificationDto
            {
                IssuerName = tenant.Name,
                CertificateNumber = certificate.CertificateNumber,
                IssueDate = certificate.IssueDate,
                Status = status
            });
    }

    /// <summary>Anonymous status-only lookup. Never disclose student or enrollment information.</summary>
    public async Task<ApiResponse<PublicTranscriptVerificationDto>> VerifyTranscriptAsync(
        Guid reference, CancellationToken cancellationToken = default)
    {
        if (reference == Guid.Empty)
            return ApiResponse<PublicTranscriptVerificationDto>.ErrorResponse("Credential not found.", 404);
        var matches = await _db.TranscriptIssues.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.PublicId == reference && !x.IsDeleted)
            .Select(x => new { x.TenantId, x.StudentId, x.PublicId, x.TranscriptNumber,
                x.IssueDate, x.IssuedByUserId, x.IsRevoked, x.ProtectedAcademicRecord })
            .Take(2).ToListAsync(cancellationToken);
        if (matches.Count != 1)
            return ApiResponse<PublicTranscriptVerificationDto>.ErrorResponse("Credential not found.", 404);
        var transcript = matches[0];
        var tenant = await _db.Tenants.AsNoTracking().Where(x =>
            x.Id == transcript.TenantId && !x.IsDeleted && x.State == TenantState.Active &&
            x.OnboardingStage == OnboardingStage.Completed && x.OnboardingCompletedAt != null)
            .Select(x => new { x.Id, x.Name }).SingleOrDefaultAsync(cancellationToken);
        if (tenant == null)
            return ApiResponse<PublicTranscriptVerificationDto>.ErrorResponse("Credential not found.", 404);
        var now = _clock.GetUtcNow().UtcDateTime;
        var subscribed = await _db.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.TenantId == tenant.Id && !x.IsDeleted &&
                x.StartsAt <= now && x.EndsAt > now &&
                (x.State == SubscriptionState.Active || x.State == SubscriptionState.Trial ||
                 x.State == SubscriptionState.Grace), cancellationToken);
        var enabled = await (
            from selection in _db.TenantModules.IgnoreQueryFilters().AsNoTracking()
            join module in _db.ProductModules.AsNoTracking()
                on selection.ProductModuleId equals module.Id
            where selection.TenantId == tenant.Id && !selection.IsDeleted &&
                selection.IsEnabled && module.IsActive && !module.IsDeleted &&
                module.Code == "EXAM"
            select selection.Id).AnyAsync(cancellationToken);
        if (!subscribed || !enabled)
            return ApiResponse<PublicTranscriptVerificationDto>.ErrorResponse(
                "Credential verification unavailable.", 404);
        var issuedBefore = transcript.IssueDate.ToDateTime(TimeOnly.MaxValue);
        // Require tenant-composite provenance, currently released evidence and no newer
        // finalized version (published or withdrawn) superseding the cited result.
        var evidence = await (
            from student in _db.Students.IgnoreQueryFilters().AsNoTracking()
            join enrollment in _db.StudentEnrollments.IgnoreQueryFilters().AsNoTracking()
                on new { student.TenantId, Id = student.Id }
                equals new { enrollment.TenantId, Id = enrollment.StudentId }
            join batch in _db.AcademicBatches.IgnoreQueryFilters().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.AcademicBatchId }
                equals new { batch.TenantId, batch.Id }
            join summary in _db.StudentResultSummaries.IgnoreQueryFilters().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.Id }
                equals new { summary.TenantId, Id = summary.StudentEnrollmentId }
            join publication in _db.ResultPublications.IgnoreQueryFilters().AsNoTracking()
                on new { summary.TenantId, Id = summary.ResultPublicationId }
                equals new { publication.TenantId, publication.Id }
            join assessment in _db.Assessments.IgnoreQueryFilters().AsNoTracking()
                on new { publication.TenantId, Id = publication.AssessmentId }
                equals new { assessment.TenantId, assessment.Id }
            where student.TenantId == tenant.Id && student.Id == transcript.StudentId &&
                !student.IsDeleted && !enrollment.IsDeleted && !batch.IsDeleted &&
                !summary.IsDeleted && !publication.IsDeleted && !assessment.IsDeleted &&
                assessment.State != AssessmentState.Cancelled &&
                enrollment.CampusId == batch.CampusId &&
                enrollment.AcademicYearId == batch.AcademicYearId &&
                assessment.CampusId == batch.CampusId &&
                assessment.AcademicYearId == batch.AcademicYearId &&
                publication.AcademicBatchId == batch.Id &&
                summary.AssessmentId == assessment.Id &&
                summary.PublicationVersionNo == publication.VersionNo &&
                !summary.IsWithheld &&
                publication.State == ResultPublicationState.Published &&
                publication.VisibleToStudent && publication.PublishedAt != null &&
                publication.PublishedAt <= issuedBefore &&
                !_db.ResultPublications.IgnoreQueryFilters().AsNoTracking().Any(newer =>
                    newer.TenantId == tenant.Id && !newer.IsDeleted &&
                    newer.AssessmentId == publication.AssessmentId &&
                    newer.AcademicBatchId == publication.AcademicBatchId &&
                    newer.VersionNo > publication.VersionNo &&
                    (newer.State == ResultPublicationState.Published ||
                     newer.State == ResultPublicationState.Withdrawn))
            select summary.Id).AnyAsync(cancellationToken);
        var authenticated = true;
        if (!string.IsNullOrWhiteSpace(transcript.ProtectedAcademicRecord))
        {
            var issue = new TranscriptIssue
            {
                TenantId = transcript.TenantId, StudentId = transcript.StudentId,
                PublicId = transcript.PublicId, TranscriptNumber = transcript.TranscriptNumber,
                IssueDate = transcript.IssueDate, IssuedByUserId = transcript.IssuedByUserId,
                ProtectedAcademicRecord = transcript.ProtectedAcademicRecord
            };
            authenticated = TranscriptSnapshotIntegrity.TryRead(_transcriptProtector, issue, out var snapshot);
            if (authenticated)
            {
                var pinned = snapshot!.AcademicRecord.Courses
                    .Select(x => (x.PublicationId, x.PublicationVersionNo)).Distinct().ToArray();
                var publicationIds = pinned.Select(x => x.PublicationId).ToArray();
                var published = await (
                    from publication in _db.ResultPublications.IgnoreQueryFilters().AsNoTracking()
                    join summary in _db.StudentResultSummaries.IgnoreQueryFilters().AsNoTracking()
                        on new { publication.TenantId, Id = publication.Id }
                        equals new { summary.TenantId, Id = summary.ResultPublicationId }
                    join enrollment in _db.StudentEnrollments.IgnoreQueryFilters().AsNoTracking()
                        on new { summary.TenantId, Id = summary.StudentEnrollmentId }
                        equals new { enrollment.TenantId, enrollment.Id }
                    join assessment in _db.Assessments.IgnoreQueryFilters().AsNoTracking()
                        on new { publication.TenantId, Id = publication.AssessmentId }
                        equals new { assessment.TenantId, assessment.Id }
                    where publication.TenantId == tenant.Id && !publication.IsDeleted && !summary.IsDeleted &&
                        !enrollment.IsDeleted && !assessment.IsDeleted &&
                        enrollment.StudentId == transcript.StudentId &&
                        publication.AcademicBatchId == enrollment.AcademicBatchId &&
                        assessment.CampusId == enrollment.CampusId &&
                        assessment.AcademicYearId == enrollment.AcademicYearId &&
                        summary.AssessmentId == assessment.Id &&
                        publicationIds.Contains(publication.Id) &&
                        publication.State == ResultPublicationState.Published &&
                        publication.VisibleToStudent && publication.PublishedAt != null &&
                        publication.PublishedAt <= issuedBefore &&
                        summary.PublicationVersionNo == publication.VersionNo && !summary.IsWithheld &&
                        !_db.ResultPublications.IgnoreQueryFilters().AsNoTracking().Any(newer =>
                            newer.TenantId == tenant.Id && !newer.IsDeleted &&
                            newer.AssessmentId == publication.AssessmentId &&
                            newer.AcademicBatchId == publication.AcademicBatchId &&
                            newer.VersionNo > publication.VersionNo &&
                            (newer.State == ResultPublicationState.Published ||
                             newer.State == ResultPublicationState.Withdrawn))
                    select new { publication.Id, publication.VersionNo })
                    .Distinct().Take(501).ToListAsync(cancellationToken);
                authenticated = pinned.Length <= 500 && pinned.Length == published.Count &&
                    pinned.All(x => published.Any(p => p.Id == x.PublicationId &&
                        p.VersionNo == x.PublicationVersionNo));
            }
        }
        var status = transcript.IsRevoked ? "Revoked" :
            evidence && authenticated && transcript.IssueDate <= DateOnly.FromDateTime(now)
                ? "Valid" : "ReviewRequired";
        _logger.LogInformation("Public transcript verification: tenant {TenantId}, status {Status}",
            tenant.Id, status);
        return ApiResponse<PublicTranscriptVerificationDto>.SuccessResponse(new()
        {
            IssuerName = tenant.Name, TranscriptNumber = transcript.TranscriptNumber,
            IssueDate = transcript.IssueDate, Status = status
        });
    }
}
