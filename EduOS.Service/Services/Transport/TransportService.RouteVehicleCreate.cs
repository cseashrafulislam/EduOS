using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    public async Task<ApiResponse<RouteVehicleAssignmentDto>> AssignRouteVehicleAsync(
        CreateTransportRouteVehicleLinkRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<RouteVehicleAssignmentDto>();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.RouteId <= 0 || request.VehicleId <= 0 ||
            request.EffectiveFrom == default || request.EffectiveFrom < today ||
            (request.EffectiveTo.HasValue && request.EffectiveTo.Value < request.EffectiveFrom) ||
            !TryDecodeVersion(request.RouteRowVersion, out var routeVersion) ||
            !TryDecodeVersion(request.VehicleRowVersion, out var vehicleVersion))
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                "Route, vehicle, request ID, row versions and valid dates are required.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var route = await _routes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.RouteId && !x.IsDeleted && x.IsActive, token);
                var vehicle = await _vehicles.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.VehicleId && !x.IsDeleted && x.IsActive, token);
                if (route == null || vehicle == null || vehicle.Capacity <= 0)
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Active route or vehicle not found.", 404);
                if (!route.CampusId.HasValue || !vehicle.CampusId.HasValue ||
                    route.CampusId != vehicle.CampusId)
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Route and vehicle must belong to the same explicit campus.", 409);
                var campusId = route.CampusId.Value;
                if (!await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == campusId && !x.IsDeleted && x.IsActive, token))
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Campus is unavailable.", 409);
                if (!await CanManageCampusAsync(campusId, tenant, token))
                    return Denied<RouteVehicleAssignmentDto>();
                if (!route.RowVersion.AsSpan().SequenceEqual(routeVersion) ||
                    !vehicle.RowVersion.AsSpan().SequenceEqual(vehicleVersion))
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Route or vehicle changed. Reload and retry.", 409);
                var end = request.EffectiveTo ?? DateOnly.MaxValue;
                var overlapping = await _vehicleRoutes.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsCurrent && x.VehicleId == vehicle.Id &&
                        x.EffectiveFrom <= end &&
                        (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= request.EffectiveFrom))
                    .OrderBy(x => x.Id).ToListAsync(token);
                if (overlapping.Count > 0)
                {
                    if (overlapping.Count == 1 && overlapping[0].RouteId == route.Id &&
                        overlapping[0].EffectiveFrom == request.EffectiveFrom &&
                        overlapping[0].EffectiveTo == request.EffectiveTo && overlapping[0].IsCurrent)
                        return ApiResponse<RouteVehicleAssignmentDto>.SuccessResponse(
                            MapRouteVehicleLink(overlapping[0]), "Route/vehicle assignment already exists.");
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Vehicle has an overlapping route assignment.", 409);
                }
                var conflictingRiders = await _assignments.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.VehicleId == vehicle.Id &&
                    x.State == TransportAssignmentState.Active && x.RouteId != route.Id &&
                    (!x.EndDate.HasValue || x.EndDate.Value >= request.EffectiveFrom) &&
                    x.StartDate <= end, token);
                if (conflictingRiders)
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Existing riders belong to another route during this period.", 409);
                var occupied = await _assignments.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.VehicleId == vehicle.Id &&
                    x.State == TransportAssignmentState.Active &&
                    (!x.EndDate.HasValue || x.EndDate.Value >= request.EffectiveFrom) &&
                    x.StartDate <= end, token);
                if (occupied > vehicle.Capacity)
                    return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse(
                        "Vehicle rider count exceeds capacity.", 409);
                var entity = new RouteVehicleAssignment
                {
                    TenantId = tenant, RouteId = route.Id, VehicleId = vehicle.Id,
                    EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo,
                    IsCurrent = true, CreatedAt = _clock.GetUtcNow().UtcDateTime,
                    CreatedBy = _currentUser.UserId
                };
                await _vehicleRoutes.AddAsync(entity);
                await _unitOfWork.SaveChangesAsync(token);
                return new ApiResponse<RouteVehicleAssignmentDto>
                {
                    Success = true, StatusCode = 201, Message = "Route/vehicle assignment created.",
                    Data = MapRouteVehicleLink(entity)
                };
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Route/vehicle assignment conflict for tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Assignment conflicts with another update.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Route/vehicle transaction aborted for tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Assignment conflicts with another update.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Route/vehicle assignment failed for tenant {TenantId}", tenant);
            return ApiResponse<RouteVehicleAssignmentDto>.ErrorResponse("Assignment could not be saved.", 500);
        }
    }
    private static RouteVehicleAssignmentDto MapRouteVehicleLink(RouteVehicleAssignment link) => new()
    {
        Id = link.Id, RouteId = link.RouteId, VehicleId = link.VehicleId,
        EffectiveFrom = link.EffectiveFrom, EffectiveTo = link.EffectiveTo,
        IsCurrent = link.IsCurrent, RowVersion = Convert.ToBase64String(link.RowVersion)
    };
}
