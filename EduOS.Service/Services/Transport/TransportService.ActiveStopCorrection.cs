using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
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
                var assignedSeats = await _assignments.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.VehicleId == vehicle.Id &&
                    x.State == TransportAssignmentState.Active, ct);
                if (assignedSeats > vehicle.Capacity)
                    return Error("Vehicle capacity is exceeded; resolve assignments before changes.", 409);
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Transport stop correction failed for tenant {TenantId}", tenant);
            return Error("Transport stops could not be updated.", 500);
        }
    }
}
