using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    public static bool CanExtendUpcomingRouteVehicleDates(
        DateOnly start, DateOnly? currentEnd, DateOnly requestedEnd, DateOnly today) =>
        start > today && currentEnd.HasValue && currentEnd.Value >= start &&
        requestedEnd > currentEnd.Value;

    // Keep the canonical link identity and rider associations. Exact-state retries
    // are safe; durable request-key idempotency requires Platform persistence.
    public async Task<ApiResponse<RouteVehicleAssignmentDto>> ExtendUpcomingRouteVehicleAsync(
        long assignmentId, ExtendUpcomingRouteVehicleRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<RouteVehicleAssignmentDto>();
        if (assignmentId <= 0 || request == null || request.EffectiveTo == default ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                "Assignment, new end date and row version are required.");
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
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
                if (!link.IsCurrent || link.EffectiveFrom <= today)
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Only a future current route/vehicle reservation can be extended.", 409);
                if (link.EffectiveTo == request.EffectiveTo)
                    return ApiResponse<RouteVehicleAssignmentDto>.SuccessResponse(
                        MapRouteVehicleLink(link), "Route/vehicle reservation already ends on this date.");
                if (!link.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Assignment changed. Reload and retry.", 409);
                if (!CanExtendUpcomingRouteVehicleDates(
                    link.EffectiveFrom, link.EffectiveTo, request.EffectiveTo, today))
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "New end must follow the current finite end of an upcoming reservation.", 409);
                var oldEnd = link.EffectiveTo!.Value;
                var overlap = await _vehicleRoutes.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.IsCurrent &&
                    x.Id != link.Id && x.VehicleId == link.VehicleId &&
                    x.EffectiveFrom <= request.EffectiveTo &&
                    (!x.EffectiveTo.HasValue || x.EffectiveTo.Value > oldEnd), token);
                if (overlap)
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Vehicle has another route assignment in the extended period.", 409);
                var conflictingRiders = await _assignments.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.State == TransportAssignmentState.Active &&
                    x.VehicleId == link.VehicleId && x.RouteId != link.RouteId &&
                    x.StartDate <= request.EffectiveTo &&
                    (!x.EndDate.HasValue || x.EndDate.Value > oldEnd), token);
                if (conflictingRiders)
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Riders on another route conflict with the extended reservation.", 409);
                var occupied = await _assignments.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.State == TransportAssignmentState.Active &&
                    x.VehicleId == link.VehicleId && x.StartDate <= request.EffectiveTo &&
                    (!x.EndDate.HasValue || x.EndDate.Value >= link.EffectiveFrom), token);
                if (occupied > vehicle.Capacity)
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Vehicle reservations exceed capacity for the proposed period.", 409);
                link.EffectiveTo = request.EffectiveTo;
                link.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                link.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                _logger.LogInformation(
                    "Upcoming route/vehicle reservation extended: tenant {TenantId}, campus {CampusId}, assignment {AssignmentId}, old end {OldEnd}, new end {NewEnd}",
                    tenant, route.CampusId.Value, link.Id, oldEnd, link.EffectiveTo);
                return ApiResponse<RouteVehicleAssignmentDto>.SuccessResponse(
                    MapRouteVehicleLink(link), "Upcoming route/vehicle reservation extended.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                "Assignment changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Upcoming route/vehicle extension conflict for tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                "Concurrent route/vehicle reservation conflict.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Upcoming route/vehicle extension aborted for tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                "Concurrent route/vehicle reservation conflict.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Upcoming route/vehicle extension failed for tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                "Route/vehicle reservation could not be extended.", 500);
        }
    }
}
