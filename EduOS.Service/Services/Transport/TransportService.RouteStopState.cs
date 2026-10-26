using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Transport;

/// <summary>Safe stop activation/deactivation without deleting canonical stop IDs or changing fares.</summary>
public sealed partial class TransportService
{
    public async Task<ApiResponse<TransportRouteStopsEditorDto>> SetRouteStopStateAsync(
        Guid reference, long stopId, SetTransportRouteStopStateRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<TransportRouteStopsEditorDto>();
        if (reference == Guid.Empty || stopId <= 0 || request == null ||
            !TryDecodeVersion(request.RouteRowVersion, out var routeVersion) ||
            !TryDecodeVersion(request.StopRowVersion, out var stopVersion))
            return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Route, stop and both row versions are required.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var route = await _routes.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.IsActive && x.PublicId == reference, token);
                if (route == null) return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Active route not found.", 404);
                if (!route.CampusId.HasValue)
                    return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Route has no assigned campus. Complete campus review first.", 409);
                var campus = await _campuses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == route.CampusId.Value && !x.IsDeleted && x.IsActive, token);
                if (campus == null) return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Active campus not found.", 409);
                if (!await CanManageCampusAsync(campus.Id, tenant, token)) return Denied<TransportRouteStopsEditorDto>();
                var stops = await _stops.GetQueryable().Where(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.RouteId == route.Id)
                    .OrderBy(x => x.SequenceNo).ThenBy(x => x.Id).ToListAsync(token);
                var stop = stops.FirstOrDefault(x => x.Id == stopId);
                if (stop == null) return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Stop does not belong to this route.", 404);
                if (stop.IsActive == request.IsActive)
                    return ApiResponse<TransportRouteStopsEditorDto>.SuccessResponse(
                        MapRouteStopsEditor(route, stops), "Stop already has the requested state.");
                if (!route.RowVersion.AsSpan().SequenceEqual(routeVersion) ||
                    !stop.RowVersion.AsSpan().SequenceEqual(stopVersion))
                    return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Route or stop changed. Reload and retry.", 409);
                if (!request.IsActive)
                {
                    if (await _assignments.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && !x.IsDeleted && x.RouteId == route.Id &&
                        x.State == TransportAssignmentState.Active &&
                        (x.PickupStopId == stop.Id || x.DropStopId == stop.Id), token))
                        return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse(
                            "Close active and future rider reservations referencing this stop first.", 409);
                    if (!stops.Any(x => x.Id != stop.Id && x.IsActive))
                        return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse(
                            "An active route must retain at least one active stop.", 409);
                }
                var now = _clock.GetUtcNow().UtcDateTime;
                stop.IsActive = request.IsActive;
                stop.UpdatedAt = now; stop.UpdatedBy = _currentUser.UserId;
                route.UpdatedAt = now; route.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                return ApiResponse<TransportRouteStopsEditorDto>.SuccessResponse(
                    MapRouteStopsEditor(route, stops),
                    request.IsActive ? "Stop activated." : "Stop deactivated.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Route or stop changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Route stop state conflict for tenant {TenantId}", tenant);
            return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Stop state conflicts with another update.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Route stop state failed for tenant {TenantId}", tenant);
            return ApiResponse<TransportRouteStopsEditorDto>.ErrorResponse("Stop state could not be updated.", 500);
        }
    }
}
