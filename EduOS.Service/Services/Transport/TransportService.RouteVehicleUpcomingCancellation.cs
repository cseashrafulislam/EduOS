using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;
namespace EduOS.Service.Services.Transport;
public sealed partial class TransportService
{
    public async Task<ApiResponse<RouteVehicleAssignmentDto>> CancelUpcomingRouteVehicleAssignmentAsync(
        long assignmentId, CancelUpcomingRouteVehicleLinkRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<RouteVehicleAssignmentDto>();
        if (assignmentId <= 0 || request == null || !TryDecodeVersion(request.RowVersion, out var version))
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Invalid cancellation request.");
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var link = await _vehicleRoutes.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == assignmentId && !x.IsDeleted, token);
            if (link == null) return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Not found.", 404);
            var route = await _routes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == link.RouteId && !x.IsDeleted, token);
            var vehicle = await _vehicles.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == link.VehicleId && !x.IsDeleted, token);
            if (route?.CampusId == null || route.CampusId != vehicle?.CampusId ||
                !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == route.CampusId.Value && x.IsActive && !x.IsDeleted, token))
                return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Campus unavailable.", 409);
            if (!await CanManageCampusAsync(route.CampusId.Value, tenant, token))
                return Denied<RouteVehicleAssignmentDto>();
            if (link.EffectiveFrom <= today)
                return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Already started.", 409);
            if (!link.IsCurrent)
                return ApiResponse<RouteVehicleAssignmentDto>.SuccessResponse(MapRouteVehicleLink(link), "Already cancelled.");
            if (!link.RowVersion.AsSpan().SequenceEqual(version))
                return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Changed.", 409);
            var end = link.EffectiveTo ?? DateOnly.MaxValue;
            if (await _assignments.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && !x.IsDeleted && x.State == TransportAssignmentState.Active &&
                (x.RouteVehicleAssignmentId == link.Id || (x.RouteId == link.RouteId && x.VehicleId == link.VehicleId)) &&
                x.StartDate <= end && (!x.EndDate.HasValue || x.EndDate.Value >= link.EffectiveFrom), token))
                return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Riders still reserved.", 409);
            link.IsCurrent = false;
            link.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            link.UpdatedBy = _currentUser.UserId;
            await _unitOfWork.SaveChangesAsync(token);
            return ApiResponse<RouteVehicleAssignmentDto>.SuccessResponse(MapRouteVehicleLink(link), "Cancelled.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Assignment changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Route-vehicle cancellation conflict for tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Concurrent route-vehicle assignment conflict.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Route-vehicle cancellation aborted for tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Concurrent route-vehicle assignment conflict.", 409);
        }
    }
}
