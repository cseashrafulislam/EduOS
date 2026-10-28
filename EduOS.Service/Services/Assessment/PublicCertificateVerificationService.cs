using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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

    public PublicCertificateVerificationService(EduOSDbContext db, TimeProvider clock,
        ILogger<PublicCertificateVerificationService> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
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
}
