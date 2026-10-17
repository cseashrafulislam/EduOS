using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    // TenantAdmin-only readiness view over canonical vehicle, driver and assignment aggregates.
    // Capacity report already enforces tenant/campus visibility; no student or driver identities escape.
    public async Task<ApiResponse<PagedResult<TransportDriverReadinessRowDto>>> GetDriverReadinessAsync(
        long? campusId, int page, int pageSize, string? search, string? state, int withinDays,
        CancellationToken ct = default)
    {
        if (!CanManageDriverRegistry()) return Denied<PagedResult<TransportDriverReadinessRowDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<TransportDriverReadinessRowDto>>.ErrorResponse("Invalid page.");
        var response = await BuildDriverReadinessAsync(campusId, search, state, withinDays, ct);
        if (!response.Success || response.Data == null)
            return ApiResponse<PagedResult<TransportDriverReadinessRowDto>>.ErrorResponse(response.Message, response.StatusCode);
        var rows = response.Data;
        return ApiResponse<PagedResult<TransportDriverReadinessRowDto>>.SuccessResponse(
            new PagedResult<TransportDriverReadinessRowDto>
            {
                Items = rows.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                TotalCount = rows.Count, Page = page, PageSize = pageSize
            });
    }

    public async Task<ApiResponse<string>> ExportDriverReadinessCsvAsync(
        long? campusId, string? search, string? state, int withinDays, CancellationToken ct = default)
    {
        if (!CanManageDriverRegistry()) return Denied<string>();
        var response = await BuildDriverReadinessAsync(campusId, search, state, withinDays, ct);
        if (!response.Success || response.Data == null)
            return ApiResponse<string>.ErrorResponse(response.Message, response.StatusCode);
        var csv = new StringBuilder("Campus,Vehicle,Active,Capacity,Active Riders,Current Routes,Current Drivers,Readiness,License Expiry,Days Until Expiry\r\n");
        foreach (var row in response.Data)
            csv.Append(CsvCell(row.CampusName)).Append(',').Append(CsvCell(row.VehicleNumber)).Append(',')
                .Append(row.IsActive ? "Yes" : "No").Append(',')
                .Append(row.Capacity.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.ActiveRiders.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.CurrentRouteLinks.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.CurrentDriverLinks.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(CsvCell(row.ReadinessState)).Append(',')
                .Append(row.LicenseExpiresOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty)
                .Append(',').Append(row.DaysUntilExpiry?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)
                .Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }

    private async Task<ApiResponse<IReadOnlyList<TransportDriverReadinessRowDto>>> BuildDriverReadinessAsync(
        long? campusId, string? search, string? state, int withinDays, CancellationToken ct)
    {
        if (!CanManageDriverRegistry()) return Denied<IReadOnlyList<TransportDriverReadinessRowDto>>();
        var mode = string.IsNullOrWhiteSpace(state) ? "ATTENTION" : state.Trim().ToUpperInvariant();
        if (campusId is <= 0 || search?.Trim().Length > 100 || withinDays is < 1 or > 365 ||
            mode is not ("ATTENTION" or "ALL" or "READY" or "NO_DRIVER" or "CONFLICT" or
                "INACTIVE_DRIVER" or "INACTIVE_VEHICLE" or "MISSING_EXPIRY" or "EXPIRED" or "EXPIRING"))
            return ApiResponse<IReadOnlyList<TransportDriverReadinessRowDto>>.ErrorResponse("Invalid readiness filters.");

        var first = await GetCapacityReportAsync(campusId, 1, 100, search, ct);
        if (!first.Success || first.Data == null)
            return ApiResponse<IReadOnlyList<TransportDriverReadinessRowDto>>.ErrorResponse(first.Message, first.StatusCode);
        if (first.Data.TotalCount > 1000)
            return ApiResponse<IReadOnlyList<TransportDriverReadinessRowDto>>.ErrorResponse(
                "Readiness report exceeds 1,000 vehicles. Narrow campus or search.", 413);
        var vehicles = new List<TransportCapacityReportRowDto>(first.Data.Items);
        for (var page = 2; page <= (first.Data.TotalCount + 99) / 100; page++)
        {
            var next = await GetCapacityReportAsync(campusId, page, 100, search, ct);
            if (!next.Success || next.Data == null || next.Data.TotalCount != first.Data.TotalCount)
                return ApiResponse<IReadOnlyList<TransportDriverReadinessRowDto>>.ErrorResponse(
                    "Fleet changed during readiness retrieval. Retry.", 409);
            vehicles.AddRange(next.Data.Items);
        }
        var unique = new HashSet<Guid>();
        if (vehicles.Count != first.Data.TotalCount || vehicles.Any(x => !unique.Add(x.VehicleReference)))
            return ApiResponse<IReadOnlyList<TransportDriverReadinessRowDto>>.ErrorResponse(
                "Fleet changed during readiness retrieval. Retry.", 409);
        if (vehicles.Count == 0)
            return ApiResponse<IReadOnlyList<TransportDriverReadinessRowDto>>.SuccessResponse(
                Array.Empty<TransportDriverReadinessRowDto>());

        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var cutoff = today.AddDays(withinDays);
        var references = vehicles.Select(x => x.VehicleReference).ToArray();
        var links = await (from link in _driverLinks.GetQueryable().AsNoTracking()
                           join vehicle in _vehicles.GetQueryable().AsNoTracking()
                               on link.VehicleId equals vehicle.Id
                           join driver in _drivers.GetQueryable().AsNoTracking()
                               on link.TransportDriverId equals driver.Id into drivers
                           from driver in drivers.DefaultIfEmpty()
                           where link.TenantId == tenant && !link.IsDeleted &&
                               vehicle.TenantId == tenant && !vehicle.IsDeleted &&
                               references.Contains(vehicle.PublicId) && link.IsCurrent &&
                               link.EffectiveFrom <= today &&
                               (!link.EffectiveTo.HasValue || link.EffectiveTo.Value >= today)
                           select new
                           {
                               vehicle.PublicId,
                               DriverUsable = driver != null && driver.TenantId == tenant &&
                                   !driver.IsDeleted && driver.IsActive,
                               LicenseExpiresOn = driver == null || driver.TenantId != tenant ||
                                   driver.IsDeleted ? (DateOnly?)null : driver.LicenseExpiresOn
                           }).ToListAsync(ct);
        var byVehicle = links.GroupBy(x => x.PublicId).ToDictionary(x => x.Key, x => x.ToList());
        var rows = new List<TransportDriverReadinessRowDto>(vehicles.Count);
        foreach (var vehicle in vehicles)
        {
            byVehicle.TryGetValue(vehicle.VehicleReference, out var assigned);
            var count = assigned?.Count ?? 0;
            var link = count == 1 ? assigned![0] : null;
            var expiry = link?.LicenseExpiresOn;
            var readiness = !vehicle.IsActive ? "InactiveVehicle" :
                count == 0 ? "NoDriver" :
                count > 1 ? "Conflict" :
                !link!.DriverUsable ? "InactiveDriver" :
                !expiry.HasValue ? "MissingExpiry" :
                expiry.Value < today ? "Expired" :
                expiry.Value <= cutoff ? "Expiring" : "Ready";
            var attention = readiness != "Ready" &&
                (vehicle.IsActive || vehicle.ActiveRiders > 0 || vehicle.CurrentRouteLinks > 0);
            rows.Add(new TransportDriverReadinessRowDto
            {
                VehicleReference = vehicle.VehicleReference, CampusId = vehicle.CampusId,
                CampusName = vehicle.CampusName, VehicleNumber = vehicle.VehicleNumber,
                IsActive = vehicle.IsActive, Capacity = vehicle.Capacity,
                ActiveRiders = vehicle.ActiveRiders, CurrentRouteLinks = vehicle.CurrentRouteLinks,
                CurrentDriverLinks = count, ReadinessState = readiness, NeedsAttention = attention,
                LicenseExpiresOn = expiry,
                DaysUntilExpiry = expiry.HasValue ? expiry.Value.DayNumber - today.DayNumber : null
            });
        }
        IReadOnlyList<TransportDriverReadinessRowDto> filtered = rows.Where(x =>
            mode == "ALL" || (mode == "ATTENTION" && x.NeedsAttention) ||
            string.Equals(x.ReadinessState.Replace(" ", "").Replace("_", ""),
                mode.Replace("_", ""), StringComparison.OrdinalIgnoreCase)).ToList();
        return ApiResponse<IReadOnlyList<TransportDriverReadinessRowDto>>.SuccessResponse(filtered);
    }
}
