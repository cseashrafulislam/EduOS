using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Students;

public sealed partial class StudentTransferRequestService
{
    private static DestinationTransferInboxDto MapDestination(EduOS.Core.Entities.Students.TransferRequest item) => new()
    {
        Reference = item.PublicId, SourceTenantId = item.SourceTenantId,
        State = item.State, RequestedAt = item.RequestedAt,
        AcceptedAt = item.AcceptedAt,
        DestinationEnrollmentLinked = item.DestinationStudentId.HasValue && item.DestinationEnrollmentId.HasValue,
        RowVersion = Convert.ToBase64String(item.RowVersion)
    };

    /// <summary>
    /// Destination admins see only transfer metadata addressed to their tenant.
    /// Source student, enrollment, guardian and transfer reason are never projected.
    /// </summary>
    public async Task<ApiResponse<IReadOnlyList<DestinationTransferInboxDto>>> GetDestinationInboxAsync(
        CancellationToken ct = default)
    {
        if (!CanManage()) return Error<IReadOnlyList<DestinationTransferInboxDto>>(
            "Tenant administrator permission required.", 403);
        try
        {
            var tenant = _user.TenantId;
            var records = await _requests.GetQueryable().AsNoTracking()
                .Where(x => x.DestinationTenantId == tenant && x.SourceTenantId != tenant &&
                    !x.IsDeleted && (x.State == TransferRequestState.SourceApproved ||
                                     x.State == TransferRequestState.DestinationAccepted))
                .OrderByDescending(x => x.RequestedAt).ThenByDescending(x => x.Id)
                .Take(100).ToListAsync(ct);
            IReadOnlyList<DestinationTransferInboxDto> result = records.Select(MapDestination).ToList();
            return ApiResponse<IReadOnlyList<DestinationTransferInboxDto>>.SuccessResponse(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Destination transfer inbox failed for tenant {TenantId}", _user.TenantId);
            return Error<IReadOnlyList<DestinationTransferInboxDto>>("Destination inbox unavailable.", 500);
        }
    }

    /// <summary>
    /// Acknowledges or rejects a source-approved handoff. Does not copy private records,
    /// create a destination learner/enrollment, or mark the transfer completed.
    /// </summary>
    public Task<ApiResponse<DestinationTransferInboxDto>> DecideDestinationAsync(
        Guid reference, DestinationTransferDecisionDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Error<DestinationTransferInboxDto>(
            "Tenant administrator permission required.", 403));
        if (reference == Guid.Empty || request == null ||
            !TryVersion(request.RowVersion, out var expected))
            return Task.FromResult(Error<DestinationTransferInboxDto>(
                "Valid transfer reference and row version are required."));
        var tenant = _user.TenantId;
        return WriteAsync(async token =>
        {
            var item = await _requests.GetQueryable().FirstOrDefaultAsync(x =>
                x.PublicId == reference && x.DestinationTenantId == tenant &&
                x.SourceTenantId != tenant && !x.IsDeleted, token);
            if (item == null) return Error<DestinationTransferInboxDto>("Transfer request not found.", 404);
            var target = request.Accept
                ? TransferRequestState.DestinationAccepted : TransferRequestState.Rejected;
            // Natural idempotency: a repeated decision by the same actor is safe after a lost response.
            if (item.State == target &&
                (request.Accept ? item.AcceptedByUserId : item.RejectedByUserId) == _user.UserId)
                return ApiResponse<DestinationTransferInboxDto>.SuccessResponse(
                    MapDestination(item), "Destination decision already recorded.");
            if (item.State != TransferRequestState.SourceApproved)
                return Error<DestinationTransferInboxDto>("Source approval is required; transfer state changed.", 409);
            if (!Matches(item.RowVersion, expected))
                return Error<DestinationTransferInboxDto>("Transfer request changed. Reload and retry.", 409);

            // Source approval was performed inside the source tenant boundary.
            // Destination requests must not bypass the global tenant query filter
            // to read source learners, enrollments, exits or guardians.
            var now = _clock.GetUtcNow().UtcDateTime;
            item.State = target; item.UpdatedAt = now; item.UpdatedBy = _user.UserId;
            if (request.Accept) { item.AcceptedAt = now; item.AcceptedByUserId = _user.UserId; }
            else { item.RejectedAt = now; item.RejectedByUserId = _user.UserId; }
            await _uow.SaveChangesAsync(token);
            return ApiResponse<DestinationTransferInboxDto>.SuccessResponse(
                MapDestination(item), request.Accept ? "Destination accepted the handoff." :
                    "Destination rejected the handoff.");
        }, ct);
    }
}
