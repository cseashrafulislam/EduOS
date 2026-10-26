using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Transport;

/// <summary>Changes the operational state of the canonical Route aggregate.</summary>
public sealed partial class TransportService
{
    public async Task<ApiResponse<TransportRouteSetupRowDto>> SetRouteStateAsync(
        Guid reference, SetTransportRouteStateRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<TransportRouteSetupRowDto>();
        if (reference == Guid.Empty || request == null || !TryDecodeVersion(request.RowVersion, out var version))
            return ApiResponse<TransportRouteSetupRowDto>.ErrorResponse("Route reference and row version are required.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var route = await _routes.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.PublicId == reference, token);
                if (route == null) return ApiResponse<TransportRouteSetupRowDto>.ErrorResponse("Route not found.", 404);
                if (!route.CampusId.HasValue)
                    return ApiResponse<TransportRouteSetupRowDto>.ErrorResponse("Route has no assigned campus. Complete campus review first.", 409);
                var campus = await _campuses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == route.CampusId.Value && !x.IsDeleted && x.IsActive, token);
                if (campus == null) return ApiResponse<TransportRouteSetupRowDto>.ErrorResponse("Active campus not found.", 409);
                if (!await CanManageCampusAsync(campus.Id, tenant, token)) return Denied<TransportRouteSetupRowDto>();
                var stopCount = await _stops.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.RouteId == route.Id, token);
                TransportRouteSetupRowDto Map() => new()
                {
                    Reference = route.PublicId, CampusId = campus.Id, CampusName = campus.Name,
                    Name = route.Name, Code = route.Code, StopCount = stopCount,
                    IsActive = route.IsActive, RowVersion = Convert.ToBase64String(route.RowVersion)
                };
                // Completed retries are idempotent; do not mutate audit/version on no-op.
                if (route.IsActive == request.IsActive)
                    return ApiResponse<TransportRouteSetupRowDto>.SuccessResponse(Map(), "Route already has the requested state.");
                if (!route.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<TransportRouteSetupRowDto>.ErrorResponse("Route changed. Reload and retry.", 409);
                if (request.IsActive)
                {
                    if (!await _stops.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && !x.IsDeleted && x.RouteId == route.Id && x.IsActive, token))
                        return ApiResponse<TransportRouteSetupRowDto>.ErrorResponse("Cannot activate a route without an active stop.", 409);
                }
                else
                {
                    // Include future-dated reservations; they must be closed before retirement.
                    if (await _assignments.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && !x.IsDeleted && x.RouteId == route.Id &&
                        x.State == TransportAssignmentState.Active, token))
                        return ApiResponse<TransportRouteSetupRowDto>.ErrorResponse("Close active and future rider reservations before deactivating.", 409);
                    var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                    if (await _vehicleRoutes.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && !x.IsDeleted && x.RouteId == route.Id &&
                        x.IsCurrent && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today), token))
                        return ApiResponse<TransportRouteSetupRowDto>.ErrorResponse("Close current or future vehicle assignments before deactivating.", 409);
                }
                route.IsActive = request.IsActive;
                route.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                route.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                return ApiResponse<TransportRouteSetupRowDto>.SuccessResponse(Map(),
                    request.IsActive ? "Route activated." : "Route deactivated.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<TransportRouteSetupRowDto>.ErrorResponse("Route changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Route state conflict for tenant {TenantId}", tenant);
            return ApiResponse<TransportRouteSetupRowDto>.ErrorResponse("Route state conflicts with another update.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Route state update failed for tenant {TenantId}", tenant);
            return ApiResponse<TransportRouteSetupRowDto>.ErrorResponse("Route state could not be updated.", 500);
        }
    }
}
