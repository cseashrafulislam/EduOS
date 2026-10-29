using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    public async Task<ApiResponse<CertificateSnapshotDocumentDto>> GetIssuedCertificateDocumentAsync(
        Guid reference, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<CertificateSnapshotDocumentDto>("Certificate document permission required.", 403);
        if (reference == Guid.Empty) return Error<CertificateSnapshotDocumentDto>("Invalid certificate reference.");
        var issue = await _certificates.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
        if (issue == null) return Error<CertificateSnapshotDocumentDto>("Certificate not found.", 404);
        if (!await CanAccessEnrollmentAsync(issue.StudentEnrollmentId, ct))
            return Error<CertificateSnapshotDocumentDto>("Campus access denied.", 403);
        if (issue.IsRevoked) return Error<CertificateSnapshotDocumentDto>("Certificate has been revoked.", 409);
        if (!CertificateSnapshotIntegrity.TryRead(_certificateProtector, issue, out var document))
            return Error<CertificateSnapshotDocumentDto>("Legacy or unverifiable certificate document.", 409);
        if (!await CertificatePublicationStillValidAsync(document!, ct))
            return Error<CertificateSnapshotDocumentDto>(
                "The issued publication was withdrawn, superseded or withheld; review this certificate.", 409);
        var identity = await _enrollments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == issue.TenantId && x.Id == issue.StudentEnrollmentId && !x.IsDeleted &&
            x.StudentId == issue.StudentId && x.PublicId == document!.StudentEnrollmentReference)
            .Select(x => x.Id).AnyAsync(ct);
        if (!identity) return Error<CertificateSnapshotDocumentDto>("Enrollment identity mismatch.", 409);
        var studentRef = await _students.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == issue.TenantId && x.Id == issue.StudentId && !x.IsDeleted)
            .Select(x => x.PublicId).FirstOrDefaultAsync(ct);
        if (studentRef != document!.StudentReference)
            return Error<CertificateSnapshotDocumentDto>("Student identity mismatch.", 409);
        return ApiResponse<CertificateSnapshotDocumentDto>.SuccessResponse(document);
    }

    private async Task<bool> CertificatePublicationStillValidAsync(
        CertificateSnapshotDocumentDto document, CancellationToken ct)
    {
        var publications = _publications.GetQueryable().AsNoTracking();
        var evidence = await (from summary in _summaries.GetQueryable().AsNoTracking()
            join publication in publications on new { summary.TenantId, Id = summary.ResultPublicationId }
                equals new { publication.TenantId, publication.Id }
            where summary.TenantId == document.TenantId && !summary.IsDeleted &&
                summary.StudentEnrollmentId == document.StudentEnrollmentId &&
                summary.ResultPublicationId == document.ResultPublicationId &&
                summary.PublicationVersionNo == document.ResultPublicationVersionNo &&
                summary.IsPassed && !summary.IsWithheld &&
                !publication.IsDeleted && publication.State == ResultPublicationState.Published &&
                publication.VisibleToStudent && publication.PublishedAt != null &&
                publication.VersionNo == document.ResultPublicationVersionNo &&
                publication.PublishedAt <= document.IssueDate.ToDateTime(TimeOnly.MaxValue) &&
                !publications.Any(newer => newer.TenantId == publication.TenantId && !newer.IsDeleted &&
                    newer.AssessmentId == publication.AssessmentId &&
                    newer.AcademicBatchId == publication.AcademicBatchId && newer.VersionNo > publication.VersionNo &&
                    (newer.State == ResultPublicationState.Published ||
                     newer.State == ResultPublicationState.Withdrawn))
            select summary.Id).AnyAsync(ct);
        return evidence;
    }

    private static string RenderCertificateTemplate(string template, CertificateSnapshotDocumentDto document) =>
        template.Replace("{{StudentName}}", document.StudentName, StringComparison.Ordinal)
            .Replace("{{StudentCode}}", document.StudentCode, StringComparison.Ordinal)
            .Replace("{{CampusName}}", document.CampusName, StringComparison.Ordinal)
            .Replace("{{BatchName}}", document.BatchName, StringComparison.Ordinal)
            .Replace("{{CertificateNumber}}", document.CertificateNumber, StringComparison.Ordinal)
            .Replace("{{IssueDate}}", document.IssueDate.ToString("yyyy-MM-dd"), StringComparison.Ordinal)
            .Replace("{{IssuerName}}", document.IssuerName, StringComparison.Ordinal);
}
