using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Assessment;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    /// <summary>Tenant/campus-scoped SQL counts for the entire publication, not just the visible page.</summary>
    public async Task<ApiResponse<ResultRescrutinySummaryDto>> GetResultRescrutinySummaryAsync(
        long publicationId, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<ResultRescrutinySummaryDto>("Rescrutiny report permission required.", 403);
        if (publicationId <= 0) return Error<ResultRescrutinySummaryDto>("Invalid publication.");
        var scope = await FindRescrutinyScopeAsync(publicationId, ct);
        if (scope == null) return Error<ResultRescrutinySummaryDto>("Publication not found.", 404);
        if (!await CanAccessCampusAsync(scope.CampusId, ct))
            return Error<ResultRescrutinySummaryDto>("Campus access denied.", 403);
        if (!await HasValidRescrutinyEvidenceAsync(scope, ct))
            return Error<ResultRescrutinySummaryDto>("Published snapshot requires reconciliation.", 409);

        var tenant = _user.TenantId;
        var requests = _rescrutinyEvents.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.ResultPublicationId == publicationId &&
            !x.IsDeleted && !x.IsTerminal && x.Action == ResultRescrutinyAction.Requested);
        var decisions = _rescrutinyEvents.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.ResultPublicationId == publicationId &&
            !x.IsDeleted && x.IsTerminal);

        // Do not show reassuring counts when a historical case points at a
        // different version/enrollment, or a terminal event has no matching request.
        var invalidRequest = await requests.AnyAsync(x =>
            x.PublicationVersionNo != scope.Publication.VersionNo ||
            !_enrollments.GetQueryable().AsNoTracking().Any(e =>
                e.TenantId == tenant && e.Id == x.StudentEnrollmentId && !e.IsDeleted &&
                e.AcademicBatchId == scope.Publication.AcademicBatchId &&
                e.AcademicYearId == scope.AcademicYearId && e.CampusId == scope.CampusId), ct);
        var invalidDecision = await decisions.AnyAsync(d =>
            d.PublicationVersionNo != scope.Publication.VersionNo ||
            (d.Action != ResultRescrutinyAction.Rejected &&
             d.Action != ResultRescrutinyAction.AcceptedForCorrection) ||
            !requests.Any(r => r.CaseReference == d.CaseReference &&
                r.StudentEnrollmentId == d.StudentEnrollmentId &&
                r.PublicationVersionNo == d.PublicationVersionNo &&
                r.ActorUserId != d.ActorUserId), ct);
        if (invalidRequest || invalidDecision)
            return Error<ResultRescrutinySummaryDto>("Rescrutiny case evidence requires reconciliation.", 409);

        var total = await requests.LongCountAsync(ct);
        var accepted = await decisions.LongCountAsync(x =>
            x.Action == ResultRescrutinyAction.AcceptedForCorrection, ct);
        var rejected = await decisions.LongCountAsync(x =>
            x.Action == ResultRescrutinyAction.Rejected, ct);
        if (accepted + rejected > total)
            return Error<ResultRescrutinySummaryDto>("Rescrutiny case totals require reconciliation.", 409);
        var oldestPending = await requests.Where(r =>
                !decisions.Any(d => d.CaseReference == r.CaseReference))
            .OrderBy(r => r.OccurredAt).Select(r => (DateTime?)r.OccurredAt)
            .FirstOrDefaultAsync(ct);
        return ApiResponse<ResultRescrutinySummaryDto>.SuccessResponse(new()
        {
            PublicationId = publicationId, PublicationVersionNo = scope.Publication.VersionNo,
            Total = total, Pending = total - accepted - rejected,
            AcceptedForCorrection = accepted, Rejected = rejected,
            OldestPendingAt = oldestPending
        });
    }

    /// <summary>Keyset export, at most 500 rows, reusing the same security and integrity gates as the worklist.</summary>
    public async Task<ApiResponse<ResultRescrutinyExportDto>> ExportResultRescrutinyAsync(
        ResultRescrutinyQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<ResultRescrutinyExportDto>("Rescrutiny export permission required.", 403);
        if (request == null || request.PublicationId <= 0 || request.BeforeId.HasValue ||
            request.PageSize is < 1 or > 50 ||
            (request.Status.HasValue && request.Status.Value is not
                (ResultRescrutinyAction.Requested or ResultRescrutinyAction.Rejected or
                 ResultRescrutinyAction.AcceptedForCorrection)))
            return Error<ResultRescrutinyExportDto>("Invalid export scope.");
        var items = new List<ResultRescrutinyItemDto>(500);
        long? cursor = null;
        for (var pageNo = 0; pageNo < 10; pageNo++)
        {
            ct.ThrowIfCancellationRequested();
            var page = await SearchResultRescrutinyAsync(new()
            {
                PublicationId = request.PublicationId, Status = request.Status,
                PageSize = 50, BeforeId = cursor
            }, ct);
            if (!page.Success || page.Data == null)
                return Error<ResultRescrutinyExportDto>(page.Message, page.StatusCode);
            items.AddRange(page.Data.Items);
            if (!page.Data.HasMore)
                return ApiResponse<ResultRescrutinyExportDto>.SuccessResponse(new()
                { Items = items, Truncated = false });
            if (!page.Data.NextBeforeId.HasValue ||
                (cursor.HasValue && page.Data.NextBeforeId.Value >= cursor.Value) ||
                page.Data.Items.Count == 0)
                return Error<ResultRescrutinyExportDto>("Rescrutiny export cursor requires reconciliation.", 409);
            cursor = page.Data.NextBeforeId;
        }
        return ApiResponse<ResultRescrutinyExportDto>.SuccessResponse(new()
        { Items = items, Truncated = true });
    }
}
