using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    // Operational report: no student identifiers, fares, driver identity or monetary writes.
    public async Task<ApiResponse<PagedResult<TransportCapacityReportRowDto>>> GetCapacityReportAsync(
        long? campusId, int page, int pageSize, string? search, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<TransportCapacityReportRowDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            campusId is <= 0 || search?.Trim().Length > 100)
            return ApiResponse<PagedResult<TransportCapacityReportRowDto>>.ErrorResponse("Invalid capacity report filters.");
        var tenant = _currentUser.TenantId;
        var query = from vehicle in _vehicles.GetQueryable().AsNoTracking()
                    join campus in _campuses.GetQueryable().AsNoTracking()
                        on vehicle.CampusId equals (long?)campus.Id
                    where vehicle.TenantId == tenant && campus.TenantId == tenant &&
                        !vehicle.IsDeleted && !campus.IsDeleted && campus.IsActive &&
                        (!campusId.HasValue || campus.Id == campusId.Value)
                    select new { vehicle.Id, vehicle.PublicId, CampusId = campus.Id,
                        CampusName = campus.Name, vehicle.VehicleNumber, vehicle.VehicleType,
                        vehicle.Capacity, vehicle.IsActive };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.VehicleNumber.Contains(term) ||
                (x.VehicleType != null && x.VehicleType.Contains(term)) || x.CampusName.Contains(term));
        var total = await query.CountAsync(ct);
        var vehicles = await query.OrderBy(x => x.CampusName).ThenBy(x => x.VehicleNumber).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = vehicles.Select(x => x.Id).ToArray();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var riders = await _assignments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && ids.Contains(x.VehicleId) &&
                x.State == TransportAssignmentState.Active && x.StartDate <= today &&
                (!x.EndDate.HasValue || x.EndDate.Value >= today))
            .GroupBy(x => x.VehicleId).Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var routes = await (from link in _vehicleRoutes.GetQueryable().AsNoTracking()
                            join route in _routes.GetQueryable().AsNoTracking() on link.RouteId equals route.Id
                            join vehicle in _vehicles.GetQueryable().AsNoTracking() on link.VehicleId equals vehicle.Id
                            where link.TenantId == tenant && route.TenantId == tenant &&
                                vehicle.TenantId == tenant && !link.IsDeleted && !route.IsDeleted &&
                                !vehicle.IsDeleted && route.CampusId.HasValue &&
                                route.CampusId == vehicle.CampusId && ids.Contains(link.VehicleId) &&
                                link.IsCurrent && link.EffectiveFrom <= today &&
                                (!link.EffectiveTo.HasValue || link.EffectiveTo.Value >= today)
                            group link by link.VehicleId into g
                            select new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        return ApiResponse<PagedResult<TransportCapacityReportRowDto>>.SuccessResponse(
            new PagedResult<TransportCapacityReportRowDto>
            {
                Items = vehicles.Select(x =>
                {
                    var count = riders.GetValueOrDefault(x.Id);
                    return new TransportCapacityReportRowDto
                    {
                        VehicleReference = x.PublicId, CampusId = x.CampusId,
                        CampusName = x.CampusName, VehicleNumber = x.VehicleNumber,
                        VehicleType = x.VehicleType, IsActive = x.IsActive,
                        Capacity = x.Capacity, ActiveRiders = count,
                        AvailableSeats = Math.Max(0, x.Capacity - count),
                        CurrentRouteLinks = routes.GetValueOrDefault(x.Id),
                        OverCapacity = count > x.Capacity,
                        UtilizationPercent = x.Capacity > 0
                            ? Math.Round(100m * count / x.Capacity, 1, MidpointRounding.AwayFromZero) : 0m
                    };
                }).ToList(),
                TotalCount = total, Page = page, PageSize = pageSize
            });
    }

    // Bounded, privacy-minimized export reuses the authorized paged report.
    public async Task<ApiResponse<string>> ExportCapacityCsvAsync(
        long? campusId, string? search, CancellationToken ct = default)
    {
        var first = await GetCapacityReportAsync(campusId, 1, 100, search, ct);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(first.Message ?? "Capacity report unavailable.", first.StatusCode);
        if (first.Data.TotalCount > 1000)
            return ApiResponse<string>.ErrorResponse("Export exceeds 1,000 vehicles. Filter by campus or search.", 413);
        var rows = new List<TransportCapacityReportRowDto>(first.Data.Items);
        for (var page = 2; page <= (first.Data.TotalCount + 99) / 100; page++)
        {
            var next = await GetCapacityReportAsync(campusId, page, 100, search, ct);
            if (!next.Success || next.Data == null || next.Data.TotalCount != first.Data.TotalCount)
                return ApiResponse<string>.ErrorResponse("Export changed during retrieval. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        var uniqueVehicles = new HashSet<Guid>();
        if (rows.Count != first.Data.TotalCount || rows.Any(x => !uniqueVehicles.Add(x.VehicleReference)))
            return ApiResponse<string>.ErrorResponse("Export changed during retrieval. Retry.", 409);
        var csv = new StringBuilder("Campus,Vehicle,Type,Active,Capacity,Active Riders,Available Seats,Current Route Links,Utilization %,Over Capacity\r\n");
        foreach (var row in rows)
        {
            csv.Append(CsvCell(row.CampusName)).Append(',').Append(CsvCell(row.VehicleNumber)).Append(',')
                .Append(CsvCell(row.VehicleType)).Append(',').Append(row.IsActive ? "Yes" : "No").Append(',')
                .Append(row.Capacity.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.ActiveRiders.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.AvailableSeats.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.CurrentRouteLinks.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.UtilizationPercent.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.OverCapacity ? "Yes" : "No").Append("\r\n");
        }
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }

    private static string CsvCell(string? input)
    {
        var value = (input ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
