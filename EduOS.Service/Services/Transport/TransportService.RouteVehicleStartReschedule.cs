using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    public static bool CanRescheduleUpcomingRouteVehicleDates(
        DateOnly originalStart, DateOnly? effectiveTo, DateOnly proposedStart, DateOnly today) =>
        originalStart > today && proposedStart > today && proposedStart <= today.AddYears(1) &&
        (!effectiveTo.HasValue || proposedStart <= effectiveTo.Value);

    /// <summary>
    /// Reschedules only a future route/vehicle link. Does not move or rewrite rider reservations.
    /// Any rider who would lose effective route/vehicle coverage blocks the change.
    /// </summary>
    public async Task<ApiResponse<RouteVehicleAssignmentDto>> RescheduleUpcomingRouteVehicleStartAsync(
        long assignmentId, RescheduleUpcomingRouteVehicleRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<RouteVehicleAssignmentDto>();
        if (assignmentId <= 0 || request == null || request.EffectiveFrom == default ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                "Assignment, new effective start and row version are required.");
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        const string operation = "transport.route-vehicle-start-reschedule";
        var requestHash = CommandFingerprint(operation, assignmentId,
            request.EffectiveFrom.ToString("yyyy-MM-dd"), Convert.ToBase64String(version));
        var requestId = request.ClientRequestId == Guid.Empty
            ? LegacyRouteVehicleCommandRequestId(tenant, _currentUser.UserId, operation, assignmentId, requestHash)
            : request.ClientRequestId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var link = await _vehicleRoutes.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == assignmentId && !x.IsDeleted, token);
                if (link == null)
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Assignment not found.", 404);
                var route = await _routes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == link.RouteId && !x.IsDeleted && x.IsActive, token);
                var vehicle = await _vehicles.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == link.VehicleId && !x.IsDeleted && x.IsActive, token);
                if (route?.CampusId == null || vehicle?.CampusId != route.CampusId ||
                    vehicle.Capacity < 1 ||
                    !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.Id == route.CampusId.Value &&
                        !x.IsDeleted && x.IsActive, token))
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Active route, vehicle or campus is unavailable.", 409);
                if (!await CanManageCampusAsync(route.CampusId.Value, tenant, token))
                    return Denied<RouteVehicleAssignmentDto>();
                var replay = await _commandReceipts.ReplayAsync<RouteVehicleAssignmentDto>(tenant,
                    _currentUser.UserId, operation, requestId, assignmentId, requestHash, token);
                if (replay.Conflict)
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Request key was already used with different command details.", 409);
                if (replay.Found) return replay.Response!;
                if (!link.IsCurrent || !CanRescheduleUpcomingRouteVehicleDates(
                    link.EffectiveFrom, link.EffectiveTo, request.EffectiveFrom, today))
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Only a future assignment can be rescheduled within its existing end date.", 409);
                // Exact-state replay succeeds without overwriting a newer row version.
                if (link.EffectiveFrom == request.EffectiveFrom)
                {
                    var same = ApiResponse<RouteVehicleAssignmentDto>.SuccessResponse(
                        MapRouteVehicleLink(link), "Assignment already starts on this date.");
                    await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                        requestId, assignmentId, requestHash, same, token);
                    await _unitOfWork.SaveChangesAsync(token);
                    return same;
                }
                if (!link.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Assignment changed. Reload and retry.", 409);
                var oldStart = link.EffectiveFrom;
                var proposedEnd = link.EffectiveTo ?? DateOnly.MaxValue;
                var conflictingLink = await _vehicleRoutes.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.IsCurrent &&
                    x.Id != link.Id && x.VehicleId == link.VehicleId &&
                    x.EffectiveFrom <= proposedEnd &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= request.EffectiveFrom), token);
                if (conflictingLink)
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Vehicle is reserved on another route during the proposed period.", 409);
                // A later start must not strand riders already tied to this link (including legacy null-link riders).
                if (request.EffectiveFrom > oldStart)
                {
                    var stranded = await _assignments.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && !x.IsDeleted &&
                        x.State == TransportAssignmentState.Active &&
                        (x.RouteVehicleAssignmentId == link.Id ||
                            (x.RouteVehicleAssignmentId == null &&
                                x.RouteId == link.RouteId && x.VehicleId == link.VehicleId)) &&
                        x.StartDate < request.EffectiveFrom &&
                        (!x.EndDate.HasValue || x.EndDate.Value >= oldStart), token);
                    if (stranded)
                        return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                            "Existing riders would lose route/vehicle coverage. Reschedule riders first.", 409);
                }
                var otherRouteRiders = await _assignments.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted &&
                    x.State == TransportAssignmentState.Active &&
                    x.VehicleId == link.VehicleId && x.RouteId != link.RouteId &&
                    x.StartDate <= proposedEnd &&
                    (!x.EndDate.HasValue || x.EndDate.Value >= request.EffectiveFrom), token);
                if (otherRouteRiders)
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Another route has riders on this vehicle during the proposed period.", 409);
                link.EffectiveFrom = request.EffectiveFrom;
                link.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                link.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                _logger.LogInformation(
                    "Upcoming route/vehicle start rescheduled: tenant {TenantId}, campus {CampusId}, link {LinkId}, from {OldStart} to {NewStart}",
                    tenant, route.CampusId.Value, link.Id, oldStart, link.EffectiveFrom);
                var success = ApiResponse<RouteVehicleAssignmentDto>.SuccessResponse(
                    MapRouteVehicleLink(link), "Upcoming route/vehicle assignment rescheduled.");
                await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                    requestId, assignmentId, requestHash, success, token);
                await _unitOfWork.SaveChangesAsync(token);
                return success;
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                "Assignment changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Route/vehicle reschedule conflict for tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Concurrent assignment conflict.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Route/vehicle reschedule transaction aborted for tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Concurrent assignment conflict.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Route/vehicle reschedule failed for tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Assignment could not be rescheduled.", 500);
        }
    }
}
