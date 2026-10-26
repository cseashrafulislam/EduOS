using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    /// <summary>
    /// Cancel an unstarted reservation using the canonical StudentTransport row.
    /// EndDate is set to StartDate to release the existing filtered unique reservation slot.
    /// No fare, invoice or payment values are changed.
    /// </summary>
    public async Task<ApiResponse<StudentTransportDto>> CancelUpcomingAsync(
        Guid reference, CancelUpcomingTransportAssignmentRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<StudentTransportDto>();
        if (reference == Guid.Empty || request == null ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return Error("Assignment reference and row version are required.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var assignment = await _assignments.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.PublicId == reference, ct);
                if (assignment == null) return Error("Assignment not found.", 404);
                var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == assignment.StudentEnrollmentId &&
                    x.StudentId == assignment.StudentId, ct);
                if (enrollment == null || !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == enrollment.CampusId, ct))
                    return Error("Assignment enrollment or campus is unavailable.", 409);
                if (!await CanManageCampusAsync(enrollment.CampusId, tenant, ct))
                    return Denied<StudentTransportDto>();
                if (!await _routes.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == assignment.RouteId &&
                    (!x.CampusId.HasValue || x.CampusId.Value == enrollment.CampusId), ct) ||
                    !await _vehicles.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == assignment.VehicleId &&
                    (!x.CampusId.HasValue || x.CampusId.Value == enrollment.CampusId), ct))
                    return Error("Assignment route or vehicle does not belong to the campus.", 409);

                // Idempotent retry: preserve original audit/concurrency values even with an old token.
                if (assignment.State == TransportAssignmentState.Cancelled)
                {
                    if (assignment.EndDate == assignment.StartDate)
                        return ApiResponse<StudentTransportDto>.SuccessResponse(
                            await MapAsync(assignment, ct), "Reservation already cancelled.");
                    return Error("Cancelled reservation has inconsistent dates.", 409);
                }
                if (assignment.State != TransportAssignmentState.Active)
                    return Error("Only active upcoming reservations can be cancelled.", 409);
                var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                if (assignment.StartDate <= today || assignment.EndDate.HasValue)
                    return Error("This reservation has started or already ended. Use closure instead.", 409);
                if (!assignment.RowVersion.AsSpan().SequenceEqual(version))
                    return Error("Assignment changed. Reload and retry.", 409);
                if (await _reservationFinancePolicy.HasPostedActivityAsync(tenant,
                    assignment.StudentId, assignment.StudentEnrollmentId, ct))
                    return Error("Posted finance activity requires an approved Finance adjustment before cancellation.", 409);
                assignment.State = TransportAssignmentState.Cancelled;
                assignment.EndDate = assignment.StartDate;
                assignment.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                assignment.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(ct);
                _logger.LogInformation("Transport upcoming reservation cancelled: tenant {TenantId}, assignment {AssignmentId}",
                    tenant, assignment.Id);
                return ApiResponse<StudentTransportDto>.SuccessResponse(
                    await MapAsync(assignment, ct), "Upcoming transport reservation cancelled.");
            }, System.Data.IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error("Assignment changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Transport cancellation conflict for tenant {TenantId}", tenant);
            return Error("Reservation cancellation conflicts with another update.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Transport cancellation failed for tenant {TenantId}", tenant);
            return Error("Transport reservation could not be cancelled.", 500);
        }
    }
}
