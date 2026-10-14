using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    public async Task<ApiResponse<IReadOnlyList<TransportCampusOptionDto>>> GetRouteSetupCampusesAsync(CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<IReadOnlyList<TransportCampusOptionDto>>();
        var tenant = _currentUser.TenantId;
        var query = _campuses.GetQueryable().AsNoTracking()
            .Where(c => c.TenantId == tenant && !c.IsDeleted && c.IsActive);
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(c => allowed.Contains(c.Id));
        }
        var rows = await query.OrderBy(c => c.Name).ThenBy(c => c.Id).Take(200)
            .Select(c => new TransportCampusOptionDto { Id = c.Id, Name = c.Name }).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<TransportCampusOptionDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<PagedResult<TransportRouteSetupRowDto>>> GetRouteSetupPageAsync(
        long? campusId, int page, int pageSize, string? search, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<TransportRouteSetupRowDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            campusId is <= 0 || search?.Trim().Length > 100)
            return ApiResponse<PagedResult<TransportRouteSetupRowDto>>.ErrorResponse("Invalid route filter.");
        var tenant = _currentUser.TenantId;
        var query = from route in _routes.GetQueryable().AsNoTracking()
            join campus in _campuses.GetQueryable().AsNoTracking() on route.CampusId equals (long?)campus.Id
            where route.TenantId == tenant && campus.TenantId == tenant &&
                !route.IsDeleted && !campus.IsDeleted && campus.IsActive &&
                (!campusId.HasValue || route.CampusId == campusId.Value)
            select new { route.Id, route.PublicId, route.CampusId, CampusName = campus.Name,
                route.Name, route.Code, route.IsActive, route.RowVersion };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => x.CampusId.HasValue && allowed.Contains(x.CampusId.Value));
        }
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.Name.Contains(term) || x.Code.Contains(term) || x.CampusName.Contains(term));
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.CampusName).ThenBy(x => x.Code).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var stops = await _stops.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && ids.Contains(x.RouteId))
            .GroupBy(x => x.RouteId).Select(g => new { RouteId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RouteId, x => x.Count, ct);
        return ApiResponse<PagedResult<TransportRouteSetupRowDto>>.SuccessResponse(new PagedResult<TransportRouteSetupRowDto>
        {
            Items = rows.Select(x => new TransportRouteSetupRowDto
            {
                Reference = x.PublicId, CampusId = x.CampusId!.Value, CampusName = x.CampusName,
                Name = x.Name, Code = x.Code, IsActive = x.IsActive,
                StopCount = stops.GetValueOrDefault(x.Id),
                RowVersion = Convert.ToBase64String(x.RowVersion)
            }).ToList(),
            TotalCount = count, Page = page, PageSize = pageSize
        });
    }
}
