using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    public async Task<ApiResponse<PagedResult<TransportRouteVehicleLinkRowDto>>> GetRouteVehicleLinksAsync(
        long? campusId, int page, int pageSize, string? search, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<TransportRouteVehicleLinkRowDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            campusId is <= 0 || search?.Trim().Length > 100)
            return ApiResponse<PagedResult<TransportRouteVehicleLinkRowDto>>.ErrorResponse("Invalid route/vehicle filter.");
        var tenant = _currentUser.TenantId;
        var query = from link in _vehicleRoutes.GetQueryable().AsNoTracking()
            join route in _routes.GetQueryable().AsNoTracking() on link.RouteId equals route.Id
            join vehicle in _vehicles.GetQueryable().AsNoTracking() on link.VehicleId equals vehicle.Id
            join campus in _campuses.GetQueryable().AsNoTracking() on route.CampusId equals (long?)campus.Id
            where link.TenantId == tenant && route.TenantId == tenant && vehicle.TenantId == tenant &&
                campus.TenantId == tenant && !link.IsDeleted && !route.IsDeleted && !vehicle.IsDeleted &&
                !campus.IsDeleted && campus.IsActive && route.CampusId == vehicle.CampusId &&
                (!campusId.HasValue || campus.Id == campusId.Value)
            select new { link.Id, link.RouteId, link.VehicleId, link.EffectiveFrom, link.EffectiveTo,
                link.IsCurrent, link.RowVersion, CampusId = campus.Id, CampusName = campus.Name,
                RouteName = route.Name, vehicle.VehicleNumber, vehicle.Capacity };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.RouteName.Contains(term) || x.VehicleNumber.Contains(term) ||
                x.CampusName.Contains(term));
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var riderCounts = await _assignments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.State == TransportAssignmentState.Active &&
                x.RouteVehicleAssignmentId.HasValue && ids.Contains(x.RouteVehicleAssignmentId.Value))
            .GroupBy(x => x.RouteVehicleAssignmentId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        return ApiResponse<PagedResult<TransportRouteVehicleLinkRowDto>>.SuccessResponse(
            new PagedResult<TransportRouteVehicleLinkRowDto>
            {
                Items = rows.Select(x => new TransportRouteVehicleLinkRowDto
                {
                    Id = x.Id, CampusId = x.CampusId, CampusName = x.CampusName,
                    RouteId = x.RouteId, RouteName = x.RouteName,
                    VehicleId = x.VehicleId, VehicleNumber = x.VehicleNumber,
                    Capacity = x.Capacity, ActiveRiders = riderCounts.GetValueOrDefault(x.Id),
                    EffectiveFrom = x.EffectiveFrom, EffectiveTo = x.EffectiveTo,
                    IsCurrent = x.IsCurrent && x.EffectiveFrom <= today &&
                        (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today),
                    RowVersion = Convert.ToBase64String(x.RowVersion)
                }).ToList(),
                TotalCount = count, Page = page, PageSize = pageSize
            });
    }
}
