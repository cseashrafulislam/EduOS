using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
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
        var actualReference = await _students.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == issue.TenantId && x.Id == issue.StudentId && !x.IsDeleted)
            .Select(x => x.PublicId).FirstOrDefaultAsync(ct);
        if (actualReference != snapshot!.AcademicRecord.StudentReference)
            return Error<TranscriptSnapshotDocumentDto>("Student document identity requires reconciliation.", 409);
        return ApiResponse<TranscriptSnapshotDocumentDto>.SuccessResponse(snapshot);
    }
}
