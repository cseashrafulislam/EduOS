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
        const string operation = "transport.route-vehicle-cancel-upcoming";
        var requestHash = CommandFingerprint(operation, assignmentId, Convert.ToBase64String(version));
        var requestId = request.ClientRequestId == Guid.Empty
            ? DeriveRouteVehicleCommandRequestId(tenant, _currentUser.UserId, operation, assignmentId, requestHash)
            : request.ClientRequestId;
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
            var replay = await _commandReceipts.ReplayAsync<RouteVehicleAssignmentDto>(tenant,
                _currentUser.UserId, operation, requestId, assignmentId, requestHash, token);
            if (replay.Conflict)
                return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                    "Request key was already used with different command details.", 409);
            if (replay.Found) return replay.Response!;
            if (link.EffectiveFrom <= today)
                return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Already started.", 409);
            if (!link.IsCurrent)
            {
                var same = ApiResponse<RouteVehicleAssignmentDto>.SuccessResponse(MapRouteVehicleLink(link), "Already cancelled.");
                await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                    requestId, assignmentId, requestHash, same, token);
                await _unitOfWork.SaveChangesAsync(token);
                return same;
            }
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
            var success = ApiResponse<RouteVehicleAssignmentDto>.SuccessResponse(MapRouteVehicleLink(link), "Cancelled.");
            await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                requestId, assignmentId, requestHash, success, token);
            await _unitOfWork.SaveChangesAsync(token);
            return success;
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
