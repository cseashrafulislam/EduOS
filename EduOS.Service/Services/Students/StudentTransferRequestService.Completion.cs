using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Students;

public sealed partial class StudentTransferRequestService
{
    /// <summary>
    /// Destination tenant attests to an already completed, locally authorized student
    /// enrollment. Never reads source-tenant personal data or creates a second enrollment.
    /// </summary>
    public Task<ApiResponse<DestinationTransferInboxDto>> LinkDestinationEnrollmentAsync(
        Guid reference, DestinationTransferEnrollmentDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Error<DestinationTransferInboxDto>("Tenant administrator permission required.", 403));
        if (reference == Guid.Empty || request == null ||
            request.StudentReference == Guid.Empty || request.EnrollmentReference == Guid.Empty ||
            !TryVersion(request.RowVersion, out var expected))
            return Task.FromResult(Error<DestinationTransferInboxDto>("Transfer, student, enrollment and row version are required."));
        var tenant = _user.TenantId;
        return WriteAsync(async token =>
        {
            var item = await _requests.GetQueryable().FirstOrDefaultAsync(x =>
                x.PublicId == reference && x.DestinationTenantId == tenant &&
                x.SourceTenantId != tenant && !x.IsDeleted, token);
            if (item == null) return Error<DestinationTransferInboxDto>("Transfer not found.", 404);
            if (item.State != TransferRequestState.DestinationAccepted)
                return Error<DestinationTransferInboxDto>("Destination acceptance is required.", 409);
            var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.StudentReference && !x.IsDeleted, token);
            if (student == null || student.StatusCode != "Active")
                return Error<DestinationTransferInboxDto>("Active destination student not found.", 409);
            var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.EnrollmentReference &&
                x.StudentId == student.Id && x.IsCurrent && x.State == EnrollmentState.Active &&
                !x.IsDeleted, token);
            if (enrollment == null)
                return Error<DestinationTransferInboxDto>("Current active destination enrollment not found.", 409);
            if (item.DestinationStudentId.HasValue && item.DestinationStudentId != student.Id)
                return Error<DestinationTransferInboxDto>("Transfer is already linked to a different student.", 409);
            if (item.DestinationEnrollmentId.HasValue && item.DestinationEnrollmentId != enrollment.Id)
                return Error<DestinationTransferInboxDto>("Transfer is already linked to a different enrollment.", 409);
            // Exact retry is safe even when the caller holds the old transfer rowversion.
            // Re-validate current enrollment before returning success; do not attest stale placement.
            if (item.DestinationStudentId == student.Id && item.DestinationEnrollmentId == enrollment.Id)
                return ApiResponse<DestinationTransferInboxDto>.SuccessResponse(
                    MapDestination(item), "Destination enrollment already linked.");
            if (!Matches(item.RowVersion, expected))
                return Error<DestinationTransferInboxDto>("Transfer changed. Reload and retry.", 409);
            if (await _requests.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.Id != item.Id && x.DestinationTenantId == tenant && !x.IsDeleted &&
                (x.DestinationEnrollmentId == enrollment.Id ||
                 (x.DestinationStudentId == student.Id &&
                  x.State == TransferRequestState.DestinationAccepted)), token))
                return Error<DestinationTransferInboxDto>("Destination enrollment or active student transfer is already linked.", 409);
            // Enrollment creation, seat allocation and roll uniqueness remain owned by
            // the canonical destination enrollment workflow and its database constraints.
            var now = _clock.GetUtcNow().UtcDateTime;
            item.DestinationStudentId = student.Id;
            item.DestinationEnrollmentId = enrollment.Id;
            item.DestinationEnrollmentConfirmedAt = null;
            item.DestinationEnrollmentConfirmedByUserId = null;
            item.DestinationEnrollmentVersionAtConfirmation = null;
            item.UpdatedAt = now;
            item.UpdatedBy = _user.UserId;
            await _uow.SaveChangesAsync(token);
            return ApiResponse<DestinationTransferInboxDto>.SuccessResponse(
                MapDestination(item), "Destination enrollment linked. Source must now issue the final transfer exit.");
        }, ct);
    }

    /// <summary>
    /// Source finalization is a recoverable last step: the existing exit workflow
    /// performs fee clearance, enrollment closure, status audit and TC issuance.
    /// Here the certificate linkage and transfer completion commit atomically.
    /// </summary>
    public Task<ApiResponse<TransferRequestDto>> CompleteAsync(
        Guid reference, CompleteTransferRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Error<TransferRequestDto>("Tenant administrator permission required.", 403));
        if (reference == Guid.Empty || request == null || !TryVersion(request.RowVersion, out var expected))
            return Task.FromResult(Error<TransferRequestDto>("Transfer reference and row version are required."));
        var tenant = _user.TenantId;
        return WriteAsync(async token =>
        {
            var item = await _requests.GetQueryable().FirstOrDefaultAsync(x =>
                x.SourceTenantId == tenant && x.PublicId == reference && !x.IsDeleted, token);
            if (item == null) return Error<TransferRequestDto>("Transfer not found.", 404);
            var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == item.SourceStudentId && !x.IsDeleted, token);
            var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == item.SourceEnrollmentId &&
                x.StudentId == item.SourceStudentId && !x.IsDeleted, token);
            if (student == null || enrollment == null)
                return Error<TransferRequestDto>("Source student or enrollment unavailable.", 409);
            if (item.State == TransferRequestState.Completed)
                return ApiResponse<TransferRequestDto>.SuccessResponse(
                    Map(item, student, enrollment), "Transfer already completed.");
            if (item.State != TransferRequestState.DestinationAccepted ||
                !item.DestinationTenantId.HasValue || !item.DestinationStudentId.HasValue ||
                !item.DestinationEnrollmentId.HasValue)
                return Error<TransferRequestDto>("Destination must accept and link its enrolled student first.", 409);
            if (!Matches(item.RowVersion, expected))
                return Error<TransferRequestDto>("Transfer changed. Reload and retry.", 409);
            // The source must not bypass tenant filters to read destination learners.
            // Require a recent explicit receiving-tenant attestation.
            if (!DestinationTransferConfirmationPolicy.IsFresh(
                    item.DestinationEnrollmentConfirmedAt, _clock.GetUtcNow().UtcDateTime))
                return Error<TransferRequestDto>(
                    "Destination enrollment confirmation expired or missing. Ask destination to reconfirm.", 409);
            if (student.StatusCode != "TC" || enrollment.IsCurrent ||
                enrollment.State != EnrollmentState.Transferred)
                return Error<TransferRequestDto>("Issue the source student's final Transfer exit and TC first.", 409);
            var exit = await _exits.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.StudentId == student.Id &&
                x.StudentEnrollmentId == enrollment.Id && x.ExitType == StudentExitType.Transfer &&
                x.FeesCleared && !x.IsDeleted, token);
            if (exit == null)
                return Error<TransferRequestDto>("Matching cleared transfer exit is required.", 409);
            var certificate = await _certificates.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.StudentId == student.Id &&
                x.StudentEnrollmentId == enrollment.Id && x.ClientRequestId == exit.ClientRequestId &&
                x.FeesCleared && !x.IsDeleted, token);
            if (certificate == null || (certificate.TransferRequestId.HasValue &&
                certificate.TransferRequestId != item.Id))
                return Error<TransferRequestDto>("Matching unlinked transfer certificate is required.", 409);
            if (await _requests.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.Id != item.Id && x.SourceTenantId == tenant &&
                x.SourceEnrollmentId == enrollment.Id && !x.IsDeleted &&
                x.State == TransferRequestState.Completed, token))
                return Error<TransferRequestDto>("Source enrollment has already been transferred.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            certificate.TransferRequestId = item.Id;
            certificate.UpdatedAt = now;
            certificate.UpdatedBy = _user.UserId;
            item.State = TransferRequestState.Completed;
            item.CompletedAt = now;
            item.UpdatedAt = now;
            item.UpdatedBy = _user.UserId;
            await _uow.SaveChangesAsync(token);
            return ApiResponse<TransferRequestDto>.SuccessResponse(
                Map(item, student, enrollment), "Transfer completed and certificate linked.");
        }, ct);
    }
}
