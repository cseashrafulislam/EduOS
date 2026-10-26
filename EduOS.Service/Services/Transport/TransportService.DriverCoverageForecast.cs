using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    /// <summary>Future-day dispatch forecast over canonical vehicles, rider reservations and driver/route links.</summary>
    public async Task<ApiResponse<PagedResult<TransportDriverCoverageForecastRowDto>>> GetDriverCoverageForecastAsync(
        DateOnly operatingDate, long? campusId, int page, int pageSize, string? search,
        string? state, CancellationToken ct = default)
    {
        if (!CanManageDriverRegistry()) return Denied<PagedResult<TransportDriverCoverageForecastRowDto>>();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var mode = string.IsNullOrWhiteSpace(state) ? "ATTENTION" : state.Trim().ToUpperInvariant();
        if (operatingDate < today || operatingDate > today.AddDays(365) ||
            campusId is <= 0 || search?.Trim().Length > 100 ||
            page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            mode is not ("ATTENTION" or "ALL" or "READY" or "IDLE" or "OVER_CAPACITY" or
                "INACTIVE_VEHICLE" or "ROUTE_CONFLICT" or "NO_ROUTE" or "NO_DRIVER" or
                "DRIVER_CONFLICT" or "INACTIVE_DRIVER" or "MISSING_EXPIRY" or "EXPIRED"))
            return ApiResponse<PagedResult<TransportDriverCoverageForecastRowDto>>.ErrorResponse("Invalid future dispatch filters.");
        var tenant = _currentUser.TenantId;
        var vehicles = from vehicle in _vehicles.GetQueryable().AsNoTracking()
            join campus in _campuses.GetQueryable().AsNoTracking() on vehicle.CampusId equals (long?)campus.Id
            where vehicle.TenantId == tenant && campus.TenantId == tenant &&
                !vehicle.IsDeleted && !campus.IsDeleted && campus.IsActive &&
                (!campusId.HasValue || campus.Id == campusId.Value)
            select new { vehicle.Id, vehicle.PublicId, CampusId = campus.Id, CampusName = campus.Name,
                vehicle.VehicleNumber, vehicle.IsActive, vehicle.Capacity };
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            vehicles = vehicles.Where(x => x.VehicleNumber.Contains(term) || x.CampusName.Contains(term));
        // Filter after classification; page on the authorized, bounded fleet to avoid leaking cross-campus counts.
        var count = await vehicles.CountAsync(ct);
        if (count > 1000)
            return ApiResponse<PagedResult<TransportDriverCoverageForecastRowDto>>.ErrorResponse(
                "Dispatch forecast exceeds 1,000 vehicles. Narrow campus or search.", 413);
        var fleet = await vehicles.OrderBy(x => x.CampusName).ThenBy(x => x.VehicleNumber).ThenBy(x => x.Id)
            .ToListAsync(ct);
        var ids = fleet.Select(x => x.Id).ToArray();
        var riders = await _assignments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && ids.Contains(x.VehicleId) &&
                x.State == TransportAssignmentState.Active && x.StartDate <= operatingDate &&
                (!x.EndDate.HasValue || x.EndDate.Value >= operatingDate))
            .GroupBy(x => x.VehicleId).Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var routes = await _vehicleRoutes.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && ids.Contains(x.VehicleId) &&
                x.IsCurrent && x.EffectiveFrom <= operatingDate &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= operatingDate))
            .GroupBy(x => x.VehicleId).Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var links = await (from link in _driverLinks.GetQueryable().AsNoTracking()
            join driver in _drivers.GetQueryable().AsNoTracking() on link.TransportDriverId equals driver.Id into joined
            from driver in joined.DefaultIfEmpty()
            where link.TenantId == tenant && !link.IsDeleted && ids.Contains(link.VehicleId) &&
                link.IsCurrent && link.EffectiveFrom <= operatingDate &&
                (!link.EffectiveTo.HasValue || link.EffectiveTo.Value >= operatingDate)
            select new { link.VehicleId, DriverUsable = driver != null && driver.TenantId == tenant &&
                !driver.IsDeleted && driver.IsActive,
                LicenseExpiresOn = driver == null || driver.TenantId != tenant || driver.IsDeleted
                    ? (DateOnly?)null : driver.LicenseExpiresOn })
            .Take(10001).ToListAsync(ct);
        if (links.Count > 10000)
            return ApiResponse<PagedResult<TransportDriverCoverageForecastRowDto>>.ErrorResponse(
                "Dispatch driver-link scan exceeds 10,000 records. Narrow campus or search.", 413);
        var byVehicle = links.GroupBy(x => x.VehicleId).ToDictionary(x => x.Key, x => x.ToList());
        var result = new List<TransportDriverCoverageForecastRowDto>(fleet.Count);
        foreach (var vehicle in fleet)
        {
            byVehicle.TryGetValue(vehicle.Id, out var assigned);
            var driverCount = assigned?.Count ?? 0;
            var driver = driverCount == 1 ? assigned![0] : null;
            var riderCount = riders.GetValueOrDefault(vehicle.Id);
            var routeCount = routes.GetValueOrDefault(vehicle.Id);
            var readiness = ClassifyFutureDriverCoverage(vehicle.IsActive, vehicle.Capacity, riderCount,
                routeCount, driverCount, driver?.DriverUsable ?? false, driver?.LicenseExpiresOn, operatingDate);
            result.Add(new TransportDriverCoverageForecastRowDto
            {
                VehicleReference = vehicle.PublicId, CampusId = vehicle.CampusId,
                CampusName = vehicle.CampusName, VehicleNumber = vehicle.VehicleNumber,
                OperatingDate = operatingDate, Capacity = vehicle.Capacity, ReservedRiders = riderCount,
                RouteLinks = routeCount, DriverLinks = driverCount,
                LicenseExpiresOn = driver?.LicenseExpiresOn, Readiness = readiness,
                NeedsAttention = readiness != "Ready" && readiness != "Idle"
            });
        }
        var filtered = result.Where(x => mode == "ALL" ||
            (mode == "ATTENTION" && x.NeedsAttention) ||
            string.Equals(x.Readiness.Replace("_", ""), mode.Replace("_", ""),
                StringComparison.OrdinalIgnoreCase)).ToList();
        return ApiResponse<PagedResult<TransportDriverCoverageForecastRowDto>>.SuccessResponse(
            new PagedResult<TransportDriverCoverageForecastRowDto>
            {
                Items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                TotalCount = filtered.Count, Page = page, PageSize = pageSize
            });
    }

    public static string ClassifyFutureDriverCoverage(bool vehicleActive, int capacity, int riders,
        int routes, int drivers, bool driverActive, DateOnly? licenseExpiresOn, DateOnly operatingDate)
    {
        if (riders > capacity || capacity <= 0) return "OverCapacity";
        if (!vehicleActive && (riders > 0 || routes > 0)) return "InactiveVehicle";
        if (routes > 1) return "RouteConflict";
        if (riders > 0 && routes == 0) return "NoRoute";
        if (riders == 0 && routes == 0) return "Idle";
        if (drivers == 0) return "NoDriver";
        if (drivers > 1) return "DriverConflict";
        if (!driverActive) return "InactiveDriver";
        if (!licenseExpiresOn.HasValue) return "MissingExpiry";
        if (licenseExpiresOn.Value < operatingDate) return "Expired";
        return "Ready";
    }

    /// <summary>Bounded CSV export with the same tenant/campus/date/state policy as the paged view.</summary>
    public async Task<ApiResponse<string>> ExportDriverCoverageForecastCsvAsync(
        DateOnly operatingDate, long? campusId, string? search, string? state,
        CancellationToken ct = default)
    {
        var first = await GetDriverCoverageForecastAsync(operatingDate, campusId, 1, 100, search, state, ct);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(first.Message, first.StatusCode);
        if (first.Data.TotalCount > 1000)
            return ApiResponse<string>.ErrorResponse("Export exceeds 1,000 vehicles.", 413);
        var rows = new List<TransportDriverCoverageForecastRowDto>(first.Data.Items);
        for (var page = 2; page <= (first.Data.TotalCount + 99) / 100; page++)
        {
            var next = await GetDriverCoverageForecastAsync(operatingDate, campusId, page, 100, search, state, ct);
            if (!next.Success || next.Data == null || next.Data.TotalCount != first.Data.TotalCount)
                return ApiResponse<string>.ErrorResponse("Dispatch forecast changed during export. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        var unique = new HashSet<Guid>();
        if (rows.Count != first.Data.TotalCount || rows.Any(x => !unique.Add(x.VehicleReference)))
            return ApiResponse<string>.ErrorResponse("Dispatch forecast changed during export. Retry.", 409);
        var csv = new StringBuilder("Date,Campus,Vehicle,Capacity,Reserved Riders,Route Links,Driver Links,License Expiry,Readiness,Needs Attention\r\n");
        foreach (var row in rows)
            csv.Append(operatingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(CsvCell(row.CampusName)).Append(',').Append(CsvCell(row.VehicleNumber)).Append(',')
                .Append(row.Capacity.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.ReservedRiders.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.RouteLinks.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.DriverLinks.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.LicenseExpiresOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty)
                .Append(',').Append(CsvCell(row.Readiness)).Append(',')
                .Append(row.NeedsAttention ? "Yes" : "No").Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
