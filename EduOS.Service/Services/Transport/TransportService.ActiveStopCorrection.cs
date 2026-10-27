using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using EduOS.Service.Services.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    /// <summary>Correct stops on a currently running assignment without changing route, vehicle or fare.</summary>
    public async Task<ApiResponse<StudentTransportDto>> CorrectActiveStopsAsync(
        Guid reference, CorrectUpcomingTransportStopsRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<StudentTransportDto>();
        if (reference == Guid.Empty || request == null ||
            request.PickupStopId is <= 0 || request.DropStopId is <= 0 ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return Error("Assignment, valid stop IDs and row version are required.");
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
                    x.StudentId == assignment.StudentId && x.IsCurrent && x.State == EnrollmentState.Active, ct);
                if (enrollment == null || !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == enrollment.CampusId && x.IsActive, ct))
                    return Error("Active enrollment campus not found.", 409);
                if (!await CanManageCampusAsync(enrollment.CampusId, tenant, ct))
                    return Denied<StudentTransportDto>();
                var route = await _routes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == assignment.RouteId && x.IsActive, ct);
                var vehicle = await _vehicles.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == assignment.VehicleId && x.IsActive, ct);
                if (route == null || vehicle == null || vehicle.Capacity <= 0 ||
                    (route.CampusId.HasValue && route.CampusId.Value != enrollment.CampusId) ||
                    (vehicle.CampusId.HasValue && vehicle.CampusId.Value != enrollment.CampusId))
                    return Error("Assigned route or vehicle is unavailable for this campus.", 409);
                var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                if (assignment.State != TransportAssignmentState.Active || assignment.StartDate > today ||
                    (assignment.EndDate.HasValue && assignment.EndDate.Value < today))
                    return Error("Only currently operating transport assignments can change stops.", 409);
                // Idempotent retry: no audit/concurrency churn when the desired stops are already saved.
                if (assignment.PickupStopId == request.PickupStopId && assignment.DropStopId == request.DropStopId)
                    return ApiResponse<StudentTransportDto>.SuccessResponse(
                        await MapAsync(assignment, ct), "Stops already match the requested assignment.");
                if (!assignment.RowVersion.AsSpan().SequenceEqual(version))
                    return Error("Assignment changed. Reload and retry.", 409);
                if (await _reservationFinancePolicy.HasPostedActivityAsync(tenant,
                    assignment.StudentId, assignment.StudentEnrollmentId, ct))
                    return Error("Posted finance activity requires an approved Finance adjustment before stop correction.", 409);
                if (ReservationFinancePolicy.HasChargeableTerms(assignment.MonthlyFare))
                    return Error("Charged Transport stop changes require an approved Finance adjustment.", 409);
                if (!await _vehicleRoutes.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.RouteId == route.Id && x.VehicleId == vehicle.Id &&
                    x.IsCurrent && x.EffectiveFrom <= today &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today), ct))
                    return Error("Route/vehicle pairing is not available today.", 409);
                var stopIds = new[] { request.PickupStopId, request.DropStopId }
                    .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
                var validStops = await _stops.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.RouteId == route.Id && x.IsActive &&
                    stopIds.Contains(x.Id), ct);
                if (validStops != stopIds.Length)
                    return Error("Pickup/drop stops must be active stops on the assigned route.", 409);
                // Check inclusive peak occupancy during this rider's remaining service window.
                // Historical and disjoint future bookings must not consume today's seat.
                var capacityFrom = assignment.StartDate > today ? assignment.StartDate : today;
                var capacityTo = assignment.EndDate ?? DateOnly.MaxValue;
                var reservations = await _assignments.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted && x.VehicleId == vehicle.Id &&
                        x.State == TransportAssignmentState.Active && x.StartDate <= capacityTo &&
                        (!x.EndDate.HasValue || x.EndDate.Value >= capacityFrom))
                    .Select(x => new { x.StartDate, x.EndDate })
                    .Take(10001).ToListAsync(ct);
                if (reservations.Count > 10000)
                    return Error("Too many overlapping reservations to validate vehicle capacity.", 413);
                if (PeakReservedSeats(reservations.Select(x => (x.StartDate, x.EndDate)),
                    capacityFrom, capacityTo).Peak > vehicle.Capacity)
                    return Error("Vehicle peak occupancy exceeds capacity during this reservation.", 409);
                assignment.PickupStopId = request.PickupStopId;
                assignment.DropStopId = request.DropStopId;
                assignment.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                assignment.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(ct);
                _logger.LogInformation("Transport active stop correction: tenant {TenantId}, assignment {AssignmentId}", tenant, assignment.Id);
                return ApiResponse<StudentTransportDto>.SuccessResponse(
                    await MapAsync(assignment, ct), "Active transport stops updated.");
            }, System.Data.IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return Error("Assignment changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Transport stop correction conflict for tenant {TenantId}", tenant);
            return Error("Stop correction conflicts with another change.", 409);
        }
        catch (System.Transactions.TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Transport stop correction transaction aborted for tenant {TenantId}", tenant);
            return Error("Stop correction conflicts with another change.", 409);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222 or 3960 or 2601 or 2627)
        {
            _logger.LogWarning(ex, "Transport stop correction SQL conflict for tenant {TenantId}", tenant);
            return Error("Stop correction conflicts with another change.", 409);
        }
        catch (InvalidOperationException ex) when (HasSqlSerializationConflict(ex))
        {
            _logger.LogWarning(ex, "Transport stop correction serialization conflict for tenant {TenantId}", tenant);
            return Error("Stop correction conflicts with another change.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Transport stop correction failed for tenant {TenantId}", tenant);
            return Error("Transport stops could not be updated.", 500);
        }
    }
}
