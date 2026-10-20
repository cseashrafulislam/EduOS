using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    /// <summary>
    /// Advisory seat forecast from canonical StudentTransport effective intervals.
    /// The allocation write must independently recheck capacity transactionally.
    /// </summary>
    public async Task<ApiResponse<PagedResult<TransportSeatForecastRowDto>>> GetSeatForecastAsync(
        long? campusId, DateOnly fromDate, DateOnly toDate, int page, int pageSize,
        string? search, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<TransportSeatForecastRowDto>>();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (fromDate < today || fromDate > today.AddYears(1) || toDate < fromDate ||
            toDate.DayNumber - fromDate.DayNumber > 89 ||
            page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue ||
            campusId is <= 0 || search?.Trim().Length > 100)
            return ApiResponse<PagedResult<TransportSeatForecastRowDto>>.ErrorResponse(
                "Select a future interval of at most 90 days within the next year and valid paging filters.");

        var tenant = _currentUser.TenantId;
        var query = from vehicle in _vehicles.GetQueryable().AsNoTracking()
                    join campus in _campuses.GetQueryable().AsNoTracking()
                        on vehicle.CampusId equals (long?)campus.Id
                    where vehicle.TenantId == tenant && campus.TenantId == tenant &&
                        !vehicle.IsDeleted && !campus.IsDeleted && campus.IsActive &&
                        (!campusId.HasValue || campus.Id == campusId.Value)
                    select new
                    {
                        vehicle.Id, vehicle.PublicId, CampusId = campus.Id,
                        CampusName = campus.Name, vehicle.VehicleNumber,
                        vehicle.Capacity, vehicle.IsActive
                    };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.VehicleNumber.Contains(term) || x.CampusName.Contains(term));

        var total = await query.CountAsync(ct);
        var vehicles = await query.OrderBy(x => x.CampusName).ThenBy(x => x.VehicleNumber)
            .ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = vehicles.Select(x => x.Id).ToArray();
        var reservations = await _assignments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && ids.Contains(x.VehicleId) &&
                x.State == TransportAssignmentState.Active && x.StartDate <= toDate &&
                (!x.EndDate.HasValue || x.EndDate.Value >= fromDate))
            .Select(x => new { x.VehicleId, x.StartDate, x.EndDate })
            .Take(10001).ToListAsync(ct);
        if (reservations.Count > 10000)
            return ApiResponse<PagedResult<TransportSeatForecastRowDto>>.ErrorResponse(
                "Forecast exceeds 10,000 reservations for this page. Narrow campus or search.", 413);

        var peaks = reservations.GroupBy(x => x.VehicleId)
            .ToDictionary(g => g.Key, g => PeakReservedSeats(
                g.Select(x => (x.StartDate, x.EndDate)), fromDate, toDate));
        return ApiResponse<PagedResult<TransportSeatForecastRowDto>>.SuccessResponse(
            new PagedResult<TransportSeatForecastRowDto>
            {
                Items = vehicles.Select(x =>
                {
                    var peak = peaks.TryGetValue(x.Id, out var result)
                        ? result : (Peak: 0, PeakDate: fromDate);
                    return new TransportSeatForecastRowDto
                    {
                        VehicleReference = x.PublicId, CampusId = x.CampusId,
                        CampusName = x.CampusName, VehicleNumber = x.VehicleNumber,
                        IsActive = x.IsActive, Capacity = x.Capacity,
                        PeakReservedSeats = peak.Peak, PeakDate = peak.PeakDate,
                        MinimumFreeSeats = Math.Max(0, x.Capacity - peak.Peak),
                        OverCapacity = peak.Peak > x.Capacity,
                        HasRisk = !x.IsActive || x.Capacity <= 0 || peak.Peak > x.Capacity
                    };
                }).ToList(),
                TotalCount = total, Page = page, PageSize = pageSize
            });
    }

    /// <summary>Inclusive effective intervals, counted at change points instead of per-day iteration.</summary>
    public static (int Peak, DateOnly PeakDate) PeakReservedSeats(
        IEnumerable<(DateOnly StartDate, DateOnly? EndDate)> reservations,
        DateOnly fromDate, DateOnly toDate)
    {
        if (toDate < fromDate) return (0, fromDate);
        var events = new SortedDictionary<DateOnly, int>();
        foreach (var reservation in reservations)
        {
            var end = reservation.EndDate ?? toDate;
            if (end < reservation.StartDate || end < fromDate || reservation.StartDate > toDate)
                continue;
            var start = reservation.StartDate < fromDate ? fromDate : reservation.StartDate;
            if (end > toDate) end = toDate;
            events[start] = events.GetValueOrDefault(start) + 1;
            if (end < toDate)
            {
                var after = end.AddDays(1);
                events[after] = events.GetValueOrDefault(after) - 1;
            }
        }
        var count = 0;
        var peak = 0;
        var peakDate = fromDate;
        foreach (var change in events)
        {
            count += change.Value;
            if (count > peak)
            {
                peak = count;
                peakDate = change.Key;
            }
        }
        return (peak, peakDate);
    }

    public async Task<ApiResponse<string>> ExportSeatForecastCsvAsync(
        long? campusId, DateOnly fromDate, DateOnly toDate, string? search,
        CancellationToken ct = default)
    {
        var first = await GetSeatForecastAsync(campusId, fromDate, toDate, 1, 100, search, ct);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(first.Message, first.StatusCode);
        if (first.Data.TotalCount > 1000)
            return ApiResponse<string>.ErrorResponse(
                "Export exceeds 1,000 vehicles. Narrow campus or search.", 413);
        var rows = new List<TransportSeatForecastRowDto>(first.Data.Items);
        for (var page = 2; page <= (first.Data.TotalCount + 99) / 100; page++)
        {
            var next = await GetSeatForecastAsync(campusId, fromDate, toDate, page, 100, search, ct);
            if (!next.Success || next.Data == null || next.Data.TotalCount != first.Data.TotalCount)
                return ApiResponse<string>.ErrorResponse("Forecast changed during export. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        var seen = new HashSet<Guid>();
        if (rows.Count != first.Data.TotalCount || rows.Any(x => !seen.Add(x.VehicleReference)))
            return ApiResponse<string>.ErrorResponse("Forecast changed during export. Retry.", 409);
        var csv = new StringBuilder("From,To,Campus,Vehicle,Active,Capacity,Peak Reserved Seats,Peak Date,Minimum Free Seats,Over Capacity,Needs Review\r\n");
        foreach (var row in rows)
            csv.Append(fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(CsvCell(row.CampusName)).Append(',').Append(CsvCell(row.VehicleNumber)).Append(',')
                .Append(row.IsActive ? "Yes" : "No").Append(',')
                .Append(row.Capacity.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.PeakReservedSeats.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.PeakDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.MinimumFreeSeats.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.OverCapacity ? "Yes" : "No").Append(',')
                .Append(row.HasRisk ? "Yes" : "No").Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
