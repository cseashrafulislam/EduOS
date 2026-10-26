using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    public static bool CanExtendUpcomingRiderDates(DateOnly start, DateOnly? currentEnd,
        DateOnly requestedEnd, DateOnly today) =>
        start > today && currentEnd.HasValue && currentEnd.Value >= start &&
        requestedEnd > currentEnd.Value;

    /// <summary>Extend an upcoming finite rider reservation without changing its route, fare, stops or identity.</summary>
    public async Task<ApiResponse<StudentTransportDto>> ExtendUpcomingRiderAsync(
        Guid reference, ExtendUpcomingTransportAssignmentRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<StudentTransportDto>();
        if (reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty ||
            request.EndDate == default ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return Error("Assignment, extension end date and row version are required.");
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        const string operation = "transport.rider-extend-upcoming";
        var requestHash = CommandFingerprint(operation, 0, reference,
            request.EndDate.ToString("yyyy-MM-dd"), Convert.ToBase64String(version));
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var assignment = await _assignments.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.PublicId == reference, ct);
                if (assignment == null) return Error("Assignment not found.", 404);
                var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == assignment.StudentEnrollmentId &&
                    x.StudentId == assignment.StudentId && x.IsCurrent &&
                    x.State == EnrollmentState.Active, ct);
                if (enrollment == null || !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == enrollment.CampusId && x.IsActive, ct))
                    return Error("Active enrollment campus unavailable.", 409);
                if (!await CanManageCampusAsync(enrollment.CampusId, tenant, ct))
                    return Denied<StudentTransportDto>();
                var replay = await _commandReceipts.ReplayAsync<StudentTransportDto>(tenant,
                    _currentUser.UserId, operation, request.ClientRequestId, assignment.Id, requestHash, ct);
                if (replay.Conflict) return Error("Request key was already used with different command details.", 409);
                if (replay.Found) return replay.Response!;
                var route = await _routes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == assignment.RouteId && x.IsActive, ct);
                var vehicle = await _vehicles.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == assignment.VehicleId && x.IsActive, ct);
                if (route == null || vehicle == null || vehicle.Capacity <= 0 ||
                    (route.CampusId.HasValue && route.CampusId.Value != enrollment.CampusId) ||
                    (vehicle.CampusId.HasValue && vehicle.CampusId.Value != enrollment.CampusId))
                    return Error("Active route or vehicle is unavailable for this campus.", 409);
                // Exact-state retry may use a stale row version; never change audit or fare.
                if (assignment.State == TransportAssignmentState.Active &&
                    assignment.StartDate > today && assignment.EndDate == request.EndDate)
                {
                    var same = ApiResponse<StudentTransportDto>.SuccessResponse(
                        await MapAsync(assignment, ct), "Reservation already extended to this date.");
                    await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                        request.ClientRequestId, assignment.Id, requestHash, same, ct);
                    await _unitOfWork.SaveChangesAsync(ct);
                    return same;
                }
                if (!assignment.RowVersion.AsSpan().SequenceEqual(version))
                    return Error("Assignment changed. Reload and retry.", 409);
                if (await _reservationFinancePolicy.HasPostedActivityAsync(tenant,
                    assignment.StudentId, assignment.StudentEnrollmentId, ct))
                    return Error("Posted finance activity requires an approved Finance adjustment before extension.", 409);
                // There is no finance-owned recurring transport billing/adjustment contract yet.
                // Never extend a chargeable period without reconciling its monetary obligation.
                if (assignment.MonthlyFare != 0m)
                    return Error("Charged reservation extension requires the Finance billing and adjustment workflow.", 409);
                if (assignment.State != TransportAssignmentState.Active ||
                    !CanExtendUpcomingRiderDates(assignment.StartDate, assignment.EndDate,
                        request.EndDate, today))
                    return Error("Only future reservations with a finite end can be extended.", 409);
                if (!await _students.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == assignment.StudentId &&
                    x.StatusCode == "Active", ct))
                    return Error("Student is no longer active.", 409);
                var stopIds = new[] { assignment.PickupStopId, assignment.DropStopId }
                    .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
                if (stopIds.Length > 0 && await _stops.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.RouteId == route.Id &&
                    x.IsActive && stopIds.Contains(x.Id), ct) != stopIds.Length)
                    return Error("One or more assigned stops are unavailable.", 409);
                var oldEnd = assignment.EndDate!.Value;
                var extensionStart = oldEnd.AddDays(1);
                if (!await _vehicleRoutes.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.IsCurrent &&
                    x.RouteId == route.Id && x.VehicleId == vehicle.Id &&
                    (!assignment.RouteVehicleAssignmentId.HasValue ||
                        x.Id == assignment.RouteVehicleAssignmentId.Value) &&
                    x.EffectiveFrom <= assignment.StartDate &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= request.EndDate), ct))
                    return Error("Route-vehicle assignment does not cover the extended reservation.", 409);
                if (await _assignments.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id != assignment.Id &&
                    x.StudentId == assignment.StudentId && x.State == TransportAssignmentState.Active &&
                    x.StartDate <= request.EndDate &&
                    (!x.EndDate.HasValue || x.EndDate.Value >= extensionStart), ct))
                    return Error("Student has another reservation during the extension.", 409);
                var overlapping = await _assignments.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted &&
                        x.Id != assignment.Id && x.VehicleId == vehicle.Id &&
                        x.State == TransportAssignmentState.Active &&
                        x.StartDate <= request.EndDate &&
                        (!x.EndDate.HasValue || x.EndDate.Value >= extensionStart))
                    .Select(x => new { x.StartDate, x.EndDate }).Take(10001).ToListAsync(ct);
                if (overlapping.Count > 10000)
                    return Error("Too many overlapping reservations to validate capacity.", 413);
                var peak = PeakReservedSeats(overlapping.Select(x => (x.StartDate, x.EndDate)),
                    extensionStart, request.EndDate).Peak;
                if (peak >= vehicle.Capacity)
                    return Error("Vehicle has no seat available for the extended period.", 409);
                assignment.EndDate = request.EndDate;
                assignment.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                assignment.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Transport upcoming rider reservation extended: tenant {TenantId}, campus {CampusId}, assignment {AssignmentId}, end {EndDate}",
                    tenant, enrollment.CampusId, assignment.Id, assignment.EndDate);
                var success = ApiResponse<StudentTransportDto>.SuccessResponse(
                    await MapAsync(assignment, ct), "Upcoming rider reservation extended.");
                await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                    request.ClientRequestId, assignment.Id, requestHash, success, ct);
                await _unitOfWork.SaveChangesAsync(ct);
                return success;
            }, System.Data.IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error("Assignment changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Transport upcoming rider extension conflict for tenant {TenantId}", tenant);
            return Error("Reservation extension conflicts with another update.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Transport upcoming rider extension transaction aborted for tenant {TenantId}", tenant);
            return Error("Reservation extension conflicts with another update.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Transport upcoming rider extension failed for tenant {TenantId}", tenant);
            return Error("Reservation extension could not be saved.", 500);
        }
    }
}
