using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Assessment;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    public async Task<ApiResponse<ResultRescrutinyHistoryDto>> GetResultRescrutinyHistoryAsync(
        Guid caseReference, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<ResultRescrutinyHistoryDto>("Rescrutiny history permission required.", 403);
        if (caseReference == Guid.Empty)
            return Error<ResultRescrutinyHistoryDto>("Invalid case reference.");
        var tenant = _user.TenantId;
        var original = await _rescrutinyEvents.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.CaseReference == caseReference &&
            !x.IsTerminal && x.Action == ResultRescrutinyAction.Requested && !x.IsDeleted, ct);
        if (original == null) return Error<ResultRescrutinyHistoryDto>("Application not found.", 404);
        var scope = await FindRescrutinyScopeAsync(original.ResultPublicationId, ct);
        if (scope == null || original.PublicationVersionNo != scope.Publication.VersionNo)
            return Error<ResultRescrutinyHistoryDto>("Publication evidence unavailable.", 409);
        if (!await CanAccessCampusAsync(scope.CampusId, ct))
            return Error<ResultRescrutinyHistoryDto>("Campus access denied.", 403);
        if (!await HasValidRescrutinyEvidenceAsync(scope, ct))
            return Error<ResultRescrutinyHistoryDto>("Published snapshot requires reconciliation.", 409);

        var studentReference = await (from enrollment in _enrollments.GetQueryable().AsNoTracking()
            join student in _students.GetQueryable().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            where enrollment.TenantId == tenant && enrollment.Id == original.StudentEnrollmentId &&
                !enrollment.IsDeleted && !student.IsDeleted &&
                enrollment.AcademicBatchId == scope.Publication.AcademicBatchId &&
                enrollment.AcademicYearId == scope.AcademicYearId && enrollment.CampusId == scope.CampusId
            select (Guid?)student.PublicId).FirstOrDefaultAsync(ct);
        if (!studentReference.HasValue)
            return Error<ResultRescrutinyHistoryDto>("Student enrollment evidence unavailable.", 409);

        // Read three to detect invalid duplicate events rather than silently truncating history.
        var events = await _rescrutinyEvents.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.CaseReference == caseReference && !x.IsDeleted)
            .OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).Take(3)
            .Select(x => new { x.Id, x.ResultPublicationId, x.StudentEnrollmentId,
                x.PublicationVersionNo, x.Action, x.IsTerminal, x.ActorUserId, x.OccurredAt, x.Reason })
            .ToListAsync(ct);
        if (events.Count is < 1 or > 2 || events[0].Id != original.Id ||
            events[0].Action != ResultRescrutinyAction.Requested || events[0].IsTerminal ||
            events.Any(x => x.ResultPublicationId != original.ResultPublicationId ||
                x.StudentEnrollmentId != original.StudentEnrollmentId ||
                x.PublicationVersionNo != original.PublicationVersionNo) ||
            (events.Count == 2 && (!events[1].IsTerminal ||
                events[1].Action is not (ResultRescrutinyAction.Rejected or ResultRescrutinyAction.AcceptedForCorrection) ||
                events[1].ActorUserId == original.ActorUserId)))
            return Error<ResultRescrutinyHistoryDto>("Rescrutiny audit evidence requires reconciliation.", 409);

        _logger.LogInformation("Result rescrutiny history inspected for tenant {TenantId} publication {PublicationId}",
            tenant, original.ResultPublicationId);
        return ApiResponse<ResultRescrutinyHistoryDto>.SuccessResponse(new()
        {
            CaseReference = caseReference,
            PublicationId = original.ResultPublicationId,
            PublicationVersionNo = original.PublicationVersionNo,
            StudentReference = studentReference.Value,
            Events = events.Select(x => new ResultRescrutinyHistoryEventDto
            {
                Action = x.Action, ActorUserId = x.ActorUserId,
                OccurredAt = x.OccurredAt, Reason = x.Reason
            }).ToArray()
        });
    }
}
