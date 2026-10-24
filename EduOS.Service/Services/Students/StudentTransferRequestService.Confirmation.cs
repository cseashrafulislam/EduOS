using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Students;

public sealed partial class StudentTransferRequestService
{
    /// <summary>
    /// Destination-only explicit enrollment attestation. No source personal data
    /// is read, and no tenant query filters are bypassed.
    /// </summary>
    public Task<ApiResponse<DestinationTransferInboxDto>> ConfirmDestinationEnrollmentAsync(
        Guid reference, DestinationTransferConfirmationDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Error<DestinationTransferInboxDto>(
            "Tenant administrator permission required.", 403));
        if (reference == Guid.Empty || request == null || !TryVersion(request.RowVersion, out var expected))
            return Task.FromResult(Error<DestinationTransferInboxDto>(
                "Transfer reference and row version are required."));
        var tenant = _user.TenantId;
        return WriteAsync(async token =>
        {
            var item = await _requests.GetQueryable().FirstOrDefaultAsync(x =>
                x.PublicId == reference && x.DestinationTenantId == tenant &&
                x.SourceTenantId != tenant && !x.IsDeleted, token);
            if (item == null) return Error<DestinationTransferInboxDto>("Transfer not found.", 404);
            if (item.State != TransferRequestState.DestinationAccepted ||
                !item.DestinationStudentId.HasValue || !item.DestinationEnrollmentId.HasValue)
                return Error<DestinationTransferInboxDto>(
                    "Destination acceptance and an enrollment link are required.", 409);
            var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == item.DestinationStudentId.Value &&
                !x.IsDeleted && x.StatusCode == "Active", token);
            if (student == null)
                return Error<DestinationTransferInboxDto>("Linked destination student is no longer active.", 409);
            var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == item.DestinationEnrollmentId.Value &&
                x.StudentId == student.Id && x.IsCurrent &&
                x.State == EnrollmentState.Active && !x.IsDeleted, token);
            if (enrollment == null)
                return Error<DestinationTransferInboxDto>("Linked destination enrollment is no longer active.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            var version = Convert.ToBase64String(enrollment.RowVersion);
            // Idempotent lost-response retry only for the same active enrollment version.
            if (DestinationTransferConfirmationPolicy.IsFresh(item.DestinationEnrollmentConfirmedAt, now) &&
                item.DestinationEnrollmentVersionAtConfirmation == version)
                return ApiResponse<DestinationTransferInboxDto>.SuccessResponse(
                    MapDestination(item), "Destination enrollment already confirmed.");
            if (!Matches(item.RowVersion, expected))
                return Error<DestinationTransferInboxDto>("Transfer changed. Reload and retry.", 409);
            item.DestinationEnrollmentConfirmedAt = now;
            item.DestinationEnrollmentConfirmedByUserId = _user.UserId;
            item.DestinationEnrollmentVersionAtConfirmation = version;
            item.UpdatedAt = now;
            item.UpdatedBy = _user.UserId;
            await _uow.SaveChangesAsync(token);
            return ApiResponse<DestinationTransferInboxDto>.SuccessResponse(
                MapDestination(item), "Destination enrollment confirmed for 10 minutes.");
        }, ct);
    }
}
