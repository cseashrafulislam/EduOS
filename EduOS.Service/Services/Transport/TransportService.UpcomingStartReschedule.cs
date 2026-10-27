using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Service.Services.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    /// <summary>Only pre-start reservations can be rescheduled; the end date and fare are immutable.</summary>
    public static bool CanRescheduleUpcomingRiderDates(DateOnly currentStart, DateOnly? endDate,
        DateOnly newStart, DateOnly today) =>
        currentStart > today && newStart > today && newStart != default &&
        (!endDate.HasValue || newStart <= endDate.Value);

    public async Task<ApiResponse<StudentTransportDto>> RescheduleUpcomingRiderStartAsync(
        Guid reference, RescheduleUpcomingTransportStartRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<StudentTransportDto>();
        if (reference == Guid.Empty || request == null || request.StartDate == default ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return Error("Assignment, new start date and row version are required.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var assignment = await _assignments.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.PublicId == reference, token);
                if (assignment == null) return Error("Assignment not found.", 404);
                var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == assignment.StudentEnrollmentId &&
                    x.StudentId == assignment.StudentId && x.IsCurrent && x.State == EnrollmentState.Active, token);
                if (enrollment == null || !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == enrollment.CampusId && x.IsActive, token))
                    return Error("Active enrollment campus unavailable.", 409);
                if (!await CanManageCampusAsync(enrollment.CampusId, tenant, token))
                    return Denied<StudentTransportDto>();
                var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                if (assignment.State != TransportAssignmentState.Active ||
                    !CanRescheduleUpcomingRiderDates(assignment.StartDate, assignment.EndDate, request.StartDate, today))
                    return Error("Only future active reservations can change start date; end date cannot be exceeded.", 409);
                if (request.StartDate > today.AddDays(365))
                    return Error("Start date must be within the next 365 days.", 409);
                // Exact-state retry is safe even when the caller's old row version is stale.
                if (assignment.StartDate == request.StartDate)
                    return ApiResponse<StudentTransportDto>.SuccessResponse(
                        await MapAsync(assignment, token), "Reservation already starts on this date.");
                if (!assignment.RowVersion.AsSpan().SequenceEqual(version))
                    return Error("Assignment changed. Reload and retry.", 409);
                if (await _reservationFinancePolicy.HasPostedActivityAsync(tenant,
                    assignment.StudentId, assignment.StudentEnrollmentId, token))
                    return Error("Posted finance activity requires an approved Finance adjustment before rescheduling.", 409);
                if (ReservationFinancePolicy.HasChargeableTerms(assignment.MonthlyFare))
                    return Error("Charged Transport reservation changes require an approved Finance billing adjustment.", 409);
                var studentIsActive = await _students.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == assignment.StudentId &&
                    x.StatusCode == "Active", token);
                var route = await _routes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == assignment.RouteId &&
                    x.IsActive && (!x.CampusId.HasValue || x.CampusId == enrollment.CampusId), token);
                var vehicle = await _vehicles.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == assignment.VehicleId &&
                    x.IsActive && x.Capacity > 0 &&
                    (!x.CampusId.HasValue || x.CampusId == enrollment.CampusId), token);
                if (!studentIsActive || route == null || vehicle == null)
                    return Error("Student, route or vehicle is unavailable in this campus.", 409);
                var pairing = await _vehicleRoutes.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsCurrent &&
                        x.RouteId == route.Id && x.VehicleId == vehicle.Id &&
                        x.EffectiveFrom <= request.StartDate &&
                        (!x.EffectiveTo.HasValue ||
                         (assignment.EndDate.HasValue && x.EffectiveTo.Value >= assignment.EndDate.Value)))
                    .OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(token);
                if (pairing == null)
                    return Error("Route and vehicle are not paired for the full rescheduled period.", 409);
                var stopIds = new[] { assignment.PickupStopId, assignment.DropStopId }
                    .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
                if (stopIds.Length > 0 && await _stops.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.RouteId == route.Id &&
                    x.IsActive && stopIds.Contains(x.Id), token) != stopIds.Length)
                    return Error("One or more existing stops are no longer valid.", 409);
                var requestedEnd = assignment.EndDate ?? DateOnly.MaxValue;
                if (await _assignments.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id != assignment.Id &&
                    x.StudentId == assignment.StudentId && x.State == TransportAssignmentState.Active &&
                    x.StartDate <= requestedEnd &&
                    (!x.EndDate.HasValue || x.EndDate.Value >= request.StartDate), token))
                    return Error("Student has another overlapping transport reservation.", 409);
                var overlapping = await _assignments.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted && x.Id != assignment.Id &&
                        x.VehicleId == vehicle.Id && x.State == TransportAssignmentState.Active &&
                        x.StartDate <= requestedEnd &&
                        (!x.EndDate.HasValue || x.EndDate.Value >= request.StartDate))
                    .Select(x => new { x.StartDate, x.EndDate }).Take(10001).ToListAsync(token);
                if (overlapping.Count > 10000)
                    return Error("Too many overlapping reservations to validate capacity.", 413);
                var peak = PeakReservedSeats(
                    overlapping.Select(x => (x.StartDate, x.EndDate)),
                    request.StartDate, requestedEnd).Peak;
                if (peak >= vehicle.Capacity)
                    return Error("Vehicle is at capacity during the rescheduled period.", 409);
                var previous = assignment.StartDate;
                assignment.StartDate = request.StartDate;
                assignment.RouteVehicleAssignmentId = pairing.Id;
                assignment.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                assignment.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                _logger.LogInformation(
                    "Transport upcoming start rescheduled: tenant {TenantId}, assignment {AssignmentId}, from {OldStart} to {NewStart}",
                    tenant, assignment.Id, previous, assignment.StartDate);
                return ApiResponse<StudentTransportDto>.SuccessResponse(
                    await MapAsync(assignment, token), "Upcoming transport start date rescheduled.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error("Assignment changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Upcoming rider reschedule conflict for tenant {TenantId}", tenant);
            return Error("Reschedule conflicts with another reservation.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Upcoming rider reschedule transaction aborted for tenant {TenantId}", tenant);
            return Error("Concurrent reservation update. Reload and retry.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Upcoming rider reschedule failed for tenant {TenantId}", tenant);
            return Error("Transport start date could not be rescheduled.", 500);
        }
    }
}
