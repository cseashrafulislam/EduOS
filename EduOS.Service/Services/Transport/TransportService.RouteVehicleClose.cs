using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    public async Task<ApiResponse<RouteVehicleAssignmentDto>> CloseRouteVehicleAssignmentAsync(
        long assignmentId, CloseTransportAssignmentRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<RouteVehicleAssignmentDto>();
        if (assignmentId <= 0 || request == null || request.EffectiveTo == default ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                "Assignment, end date and row version are required.");
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
                    x.TenantId == tenant && x.Id == link.RouteId && !x.IsDeleted, token);
                var vehicle = await _vehicles.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == link.VehicleId && !x.IsDeleted, token);
                if (route?.CampusId == null || vehicle?.CampusId != route.CampusId ||
                    !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.Id == route.CampusId.Value && !x.IsDeleted && x.IsActive, token))
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Assignment campus is unavailable.", 409);
                if (!await CanManageCampusAsync(route.CampusId.Value, tenant, token))
                    return Denied<RouteVehicleAssignmentDto>();
                if (link.EffectiveTo == request.EffectiveTo)
                    return ApiResponse<RouteVehicleAssignmentDto>.SuccessResponse(
                        MapRouteVehicleLink(link), "Assignment already ends on this date.");
                if (!link.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Assignment changed. Reload and retry.", 409);
                if (link.EffectiveTo.HasValue || !link.IsCurrent ||
                    request.EffectiveTo < today || request.EffectiveTo < link.EffectiveFrom)
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Assignment cannot be closed with this end date.", 409);
                var stranded = await _assignments.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.State == TransportAssignmentState.Active &&
                    (x.RouteVehicleAssignmentId == link.Id ||
                        (x.RouteVehicleAssignmentId == null && x.RouteId == link.RouteId &&
                            x.VehicleId == link.VehicleId)) &&
                    (!x.EndDate.HasValue || x.EndDate.Value > request.EffectiveTo), token);
                if (stranded)
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Close or reschedule active riders before ending the route/vehicle assignment.", 409);
                // A scheduled end remains valid through its effective date.
                link.EffectiveTo = request.EffectiveTo;
                link.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                link.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                return ApiResponse<RouteVehicleAssignmentDto>.SuccessResponse(
                    MapRouteVehicleLink(link), "Route/vehicle assignment end date saved.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                "Assignment changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Route/vehicle closure conflict for tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Assignment conflicts with another update.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Route/vehicle closure aborted for tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Assignment conflicts with another update.", 409);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222 or 3960 or 2601 or 2627)
        {
            _logger.LogWarning(ex, "Route/vehicle closure SQL conflict for tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Assignment conflicts with another update.", 409);
        }
        catch (InvalidOperationException ex) when (HasSqlSerializationConflict(ex))
        {
            _logger.LogWarning(ex, "Route/vehicle closure serialization conflict for tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Assignment conflicts with another update.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Route/vehicle closure failed for tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Assignment could not be closed.", 500);
        }
    }
}
