using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Assessment;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    /// <summary>Immutable version-specific published result for an independent reviewer.</summary>
    public async Task<ApiResponse<ResultRescrutinyReviewDto>> GetResultRescrutinyReviewAsync(
        Guid caseReference, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<ResultRescrutinyReviewDto>("Rescrutiny review permission required.", 403);
        if (caseReference == Guid.Empty) return Error<ResultRescrutinyReviewDto>("Invalid case reference.");

        var history = await GetResultRescrutinyHistoryAsync(caseReference, ct);
        if (!history.Success || history.Data == null)
            return Error<ResultRescrutinyReviewDto>(history.Message, history.StatusCode);

        var tenant = _user.TenantId;
        var original = await _rescrutinyEvents.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.CaseReference == caseReference && !x.IsDeleted &&
            !x.IsTerminal && x.Action == ResultRescrutinyAction.Requested, ct);
        if (original == null || original.ResultPublicationId != history.Data.PublicationId ||
            original.PublicationVersionNo != history.Data.PublicationVersionNo)
            return Error<ResultRescrutinyReviewDto>("Rescrutiny audit evidence requires reconciliation.", 409);

        var scope = await FindRescrutinyScopeAsync(original.ResultPublicationId, ct);
        if (scope == null || scope.Publication.VersionNo != original.PublicationVersionNo)
            return Error<ResultRescrutinyReviewDto>("Publication scope unavailable.", 409);
        if (!await CanAccessCampusAsync(scope.CampusId, ct))
            return Error<ResultRescrutinyReviewDto>("Campus access denied.", 403);

        // Tenant-composite joins and historical enrollment scope prevent IDOR and
        // current editable marks from replacing immutable publication evidence.
        var evidence = await (from summary in _summaries.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on new { summary.TenantId, Id = summary.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            join student in _students.GetQueryable().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            where summary.TenantId == tenant &&
                summary.ResultPublicationId == original.ResultPublicationId &&
                summary.StudentEnrollmentId == original.StudentEnrollmentId &&
                summary.AssessmentId == scope.Publication.AssessmentId &&
                summary.PublicationVersionNo == original.PublicationVersionNo &&
                enrollment.AcademicBatchId == scope.Publication.AcademicBatchId &&
                enrollment.CampusId == scope.CampusId &&
                enrollment.AcademicYearId == scope.AcademicYearId &&
                !summary.IsDeleted && !enrollment.IsDeleted && !student.IsDeleted
            select new
            {
                StudentReference = student.PublicId, student.StudentCode, enrollment.RollNo,
                summary.ObtainedMarks, summary.TotalMarks, summary.Percentage,
                summary.GPA, summary.CGPA, summary.GradeLetter,
                summary.IsPassed, summary.IsWithheld
            }).Take(2).ToListAsync(ct);

        if (evidence.Count != 1 || evidence[0].StudentReference != history.Data.StudentReference)
            return Error<ResultRescrutinyReviewDto>("Published student evidence requires reconciliation.", 409);

        var snapshot = evidence[0];
        var decision = history.Data.Events.Count == 2 ? history.Data.Events[1] : null;
        return ApiResponse<ResultRescrutinyReviewDto>.SuccessResponse(new()
        {
            CaseReference = caseReference, PublicationId = original.ResultPublicationId,
            PublicationVersionNo = original.PublicationVersionNo,
            StudentReference = snapshot.StudentReference, StudentCode = snapshot.StudentCode,
            RollNo = snapshot.RollNo, RequestReason = original.Reason,
            RequestedAt = original.OccurredAt, RequestedByUserId = original.ActorUserId,
            RequestRowVersion = Version(original.RowVersion),
            Status = decision?.Action ?? ResultRescrutinyAction.Requested,
            DecidedAt = decision?.OccurredAt, DecidedByUserId = decision?.ActorUserId,
            DecisionReason = decision?.Reason,
            ObtainedMarks = snapshot.ObtainedMarks, TotalMarks = snapshot.TotalMarks,
            Percentage = snapshot.Percentage, GPA = snapshot.GPA, CGPA = snapshot.CGPA,
            GradeLetter = snapshot.GradeLetter, IsPassed = snapshot.IsPassed,
            IsWithheld = snapshot.IsWithheld
        });
    }
}
