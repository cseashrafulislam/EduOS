using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    public async Task<ApiResponse<TranscriptSnapshotDocumentDto>> GetIssuedTranscriptDocumentAsync(
        Guid transcriptReference, CancellationToken ct = default)
    {
        if (!CanPublish())
            return Error<TranscriptSnapshotDocumentDto>("Transcript document permission required.", 403);
        if (transcriptReference == Guid.Empty)
            return Error<TranscriptSnapshotDocumentDto>("Invalid transcript reference.");
        var issue = await _transcripts.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.PublicId == transcriptReference && !x.IsDeleted, ct);
        if (issue == null) return Error<TranscriptSnapshotDocumentDto>("Transcript not found.", 404);
        if (!await CanAccessStudentHistoryAsync(issue.StudentId, ct))
            return Error<TranscriptSnapshotDocumentDto>("Campus access denied.", 403);
        if (issue.IsRevoked) return Error<TranscriptSnapshotDocumentDto>("Transcript is revoked.", 409);
        if (!TranscriptSnapshotIntegrity.TryRead(_transcriptProtector, issue, out var snapshot))
            return Error<TranscriptSnapshotDocumentDto>(
                "A verifiable issue-time document is unavailable for this transcript.", 409);
        if (!await TranscriptPublicationsStillValidAsync(snapshot!, ct))
            return Error<TranscriptSnapshotDocumentDto>(
                "A cited result was withdrawn, superseded or unreleased; this document requires review.", 409);
        var actualReference = await _students.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == issue.TenantId && x.Id == issue.StudentId && !x.IsDeleted)
            .Select(x => x.PublicId).FirstOrDefaultAsync(ct);
        if (actualReference != snapshot!.AcademicRecord.StudentReference)
            return Error<TranscriptSnapshotDocumentDto>("Student document identity requires reconciliation.", 409);
        return ApiResponse<TranscriptSnapshotDocumentDto>.SuccessResponse(snapshot);
    }

    private async Task<bool> TranscriptPublicationsStillValidAsync(
        TranscriptSnapshotDocumentDto snapshot, CancellationToken ct)
    {
        var pinned = snapshot.AcademicRecord.Courses
            .Select(x => (x.PublicationId, x.PublicationVersionNo)).Distinct().ToArray();
        if (pinned.Length is < 1 or > 500) return false;
        var ids = pinned.Select(x => x.PublicationId).ToArray();
        var issuedEnd = snapshot.IssueDate.ToDateTime(TimeOnly.MaxValue);
        var publications = _publications.GetQueryable().AsNoTracking();
        var active = await publications.Where(x =>
            x.TenantId == snapshot.TenantId && ids.Contains(x.Id) && !x.IsDeleted &&
            x.State == ResultPublicationState.Published && x.VisibleToStudent &&
            x.PublishedAt != null && x.PublishedAt <= issuedEnd &&
            !publications.Any(newer =>
                newer.TenantId == x.TenantId && !newer.IsDeleted &&
                newer.AssessmentId == x.AssessmentId && newer.AcademicBatchId == x.AcademicBatchId &&
                newer.VersionNo > x.VersionNo &&
                (newer.State == ResultPublicationState.Published ||
                 newer.State == ResultPublicationState.Withdrawn)))
            .Select(x => new { x.Id, x.VersionNo }).Take(501).ToListAsync(ct);
        return active.Count == pinned.Length &&
            pinned.All(x => active.Any(a => a.Id == x.PublicationId && a.VersionNo == x.PublicationVersionNo));
    }
}
