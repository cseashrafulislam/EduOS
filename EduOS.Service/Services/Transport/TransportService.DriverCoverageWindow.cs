using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    public static bool IsSupportedDriverCoverageWindow(DateOnly from, DateOnly to, DateOnly today) =>
        from >= today && from <= today.AddDays(365) &&
        to >= from && to.DayNumber - from.DayNumber <= 29;

    /// <summary>Evaluate each day; a gap on a non-peak day still requires attention.</summary>
    public static TransportDriverCoverageWindowRowDto ProjectDriverCoverageWindow(
        Guid reference, long campusId, string campusName, string vehicleNumber,
        bool vehicleActive, int capacity, DateOnly from, DateOnly to,
        IReadOnlyList<(DateOnly Start, DateOnly? End)> reservations,
        IReadOnlyList<(DateOnly Start, DateOnly? End)> routes,
        IReadOnlyList<(DateOnly Start, DateOnly? End, bool Usable, DateOnly? Expiry)> drivers)
    {
        if (to < from || to.DayNumber - from.DayNumber > 29)
            throw new ArgumentOutOfRangeException(nameof(to));
        var peak = 0;
        var peakDate = from;
        var minimumFree = Math.Max(0, capacity);
        var attentionDays = 0;
        DateOnly? firstAttention = null;
        var firstIssue = string.Empty;
        for (var day = from.DayNumber; day <= to.DayNumber; day++)
        {
            var date = DateOnly.FromDayNumber(day);
            var riderCount = reservations.Count(x => x.Start <= date && (!x.End.HasValue || x.End.Value >= date));
            var routeCount = routes.Count(x => x.Start <= date && (!x.End.HasValue || x.End.Value >= date));
            var assigned = drivers.Where(x => x.Start <= date && (!x.End.HasValue || x.End.Value >= date)).ToArray();
            var driver = assigned.Length == 1 ? assigned[0] : default;
            var state = ClassifyFutureDriverCoverage(vehicleActive, capacity, riderCount,
                routeCount, assigned.Length, assigned.Length == 1 && driver.Usable,
                assigned.Length == 1 ? driver.Expiry : null, date);
            if (riderCount > peak) { peak = riderCount; peakDate = date; }
            minimumFree = Math.Min(minimumFree, Math.Max(0, capacity - riderCount));
            if (state is not ("Ready" or "Idle"))
            {
                attentionDays++;
                if (!firstAttention.HasValue) { firstAttention = date; firstIssue = state; }
            }
        }
        return new TransportDriverCoverageWindowRowDto
        {
            VehicleReference = reference, CampusId = campusId, CampusName = campusName,
            VehicleNumber = vehicleNumber, FromDate = from, ToDate = to, Capacity = capacity,
            PeakReservedRiders = peak, PeakDate = peakDate, MinimumFreeSeats = minimumFree,
            AttentionDays = attentionDays, FirstAttentionDate = firstAttention, FirstIssue = firstIssue
        };
    }

    public async Task<ApiResponse<PagedResult<TransportDriverCoverageWindowRowDto>>> GetDriverCoverageWindowAsync(
        DateOnly fromDate, DateOnly toDate, long? campusId, int page, int pageSize,
        string? search, bool attentionOnly, CancellationToken ct = default)
    {
        if (!CanManageDriverRegistry()) return Denied<PagedResult<TransportDriverCoverageWindowRowDto>>();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (!IsSupportedDriverCoverageWindow(fromDate, toDate, today) || campusId is <= 0 ||
            search?.Trim().Length > 100 || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<TransportDriverCoverageWindowRowDto>>.ErrorResponse(
                "Select a future window of 1–30 days and valid campus/paging filters.");
        var tenant = _currentUser.TenantId;
        var query = from vehicle in _vehicles.GetQueryable().AsNoTracking()
                    join campus in _campuses.GetQueryable().AsNoTracking()
                        on vehicle.CampusId equals (long?)campus.Id
                    where vehicle.TenantId == tenant && campus.TenantId == tenant &&
                        !vehicle.IsDeleted && !campus.IsDeleted && campus.IsActive &&
                        (!campusId.HasValue || campus.Id == campusId.Value)
                    select new { vehicle.Id, vehicle.PublicId, CampusId = campus.Id,
                        CampusName = campus.Name, vehicle.VehicleNumber, vehicle.IsActive, vehicle.Capacity };
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.VehicleNumber.Contains(term) || x.CampusName.Contains(term));
        if (await query.CountAsync(ct) > 1000)
            return ApiResponse<PagedResult<TransportDriverCoverageWindowRowDto>>.ErrorResponse(
                "Dispatch scan exceeds 1,000 vehicles. Narrow campus or search.", 413);
        var fleet = await query.OrderBy(x => x.CampusName).ThenBy(x => x.VehicleNumber)
            .ThenBy(x => x.Id).ToListAsync(ct);
        var ids = fleet.Select(x => x.Id).ToArray();

        var bookings = await _assignments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && ids.Contains(x.VehicleId) &&
                x.State == TransportAssignmentState.Active && x.StartDate <= toDate &&
                (!x.EndDate.HasValue || x.EndDate.Value >= fromDate))
            .Select(x => new { x.VehicleId, x.StartDate, x.EndDate })
            .Take(10001).ToListAsync(ct);

        // Invalid/inactive or cross-campus route links cannot count as dispatch coverage.
        var routeLinks = await (from link in _vehicleRoutes.GetQueryable().AsNoTracking()
            join route in _routes.GetQueryable().AsNoTracking() on link.RouteId equals route.Id
            join vehicle in _vehicles.GetQueryable().AsNoTracking() on link.VehicleId equals vehicle.Id
            where link.TenantId == tenant && route.TenantId == tenant && vehicle.TenantId == tenant &&
                !link.IsDeleted && link.IsCurrent && !route.IsDeleted && route.IsActive &&
                !vehicle.IsDeleted && route.CampusId.HasValue && route.CampusId == vehicle.CampusId &&
                ids.Contains(link.VehicleId) && link.EffectiveFrom <= toDate &&
                (!link.EffectiveTo.HasValue || link.EffectiveTo.Value >= fromDate)
            select new { link.VehicleId, link.EffectiveFrom, link.EffectiveTo })
            .Take(10001).ToListAsync(ct);

        var driverLinks = await (from link in _driverLinks.GetQueryable().AsNoTracking()
            join driver in _drivers.GetQueryable().AsNoTracking() on link.TransportDriverId equals driver.Id into joined
            from driver in joined.DefaultIfEmpty()
            where link.TenantId == tenant && !link.IsDeleted && link.IsCurrent &&
                ids.Contains(link.VehicleId) && link.EffectiveFrom <= toDate &&
                (!link.EffectiveTo.HasValue || link.EffectiveTo.Value >= fromDate)
            select new { link.VehicleId, link.EffectiveFrom, link.EffectiveTo,
                Usable = driver != null && driver.TenantId == tenant && !driver.IsDeleted && driver.IsActive,
                Expiry = driver == null || driver.TenantId != tenant || driver.IsDeleted
                    ? (DateOnly?)null : driver.LicenseExpiresOn })
            .Take(10001).ToListAsync(ct);

        if (bookings.Count > 10000 || routeLinks.Count > 10000 || driverLinks.Count > 10000)
            return ApiResponse<PagedResult<TransportDriverCoverageWindowRowDto>>.ErrorResponse(
                "Dispatch scan exceeds 10,000 overlapping records per category. Narrow filters.", 413);
        var byRiders = bookings.GroupBy(x => x.VehicleId).ToDictionary(g => g.Key,
            g => g.Select(x => (Start: x.StartDate, End: x.EndDate)).ToArray());
        var byRoutes = routeLinks.GroupBy(x => x.VehicleId).ToDictionary(g => g.Key,
            g => g.Select(x => (Start: x.EffectiveFrom, End: x.EffectiveTo)).ToArray());
        var byDrivers = driverLinks.GroupBy(x => x.VehicleId).ToDictionary(g => g.Key,
            g => g.Select(x => (Start: x.EffectiveFrom, End: x.EffectiveTo, x.Usable, x.Expiry)).ToArray());

        var result = fleet.Select(x => ProjectDriverCoverageWindow(
            x.PublicId, x.CampusId, x.CampusName, x.VehicleNumber, x.IsActive, x.Capacity,
            fromDate, toDate,
            byRiders.TryGetValue(x.Id, out var riders) ? riders : Array.Empty<(DateOnly, DateOnly?)>(),
            byRoutes.TryGetValue(x.Id, out var routes) ? routes : Array.Empty<(DateOnly, DateOnly?)>(),
            byDrivers.TryGetValue(x.Id, out var drivers)
                ? drivers : Array.Empty<(DateOnly, DateOnly?, bool, DateOnly?)>()))
            .Where(x => !attentionOnly || x.NeedsAttention).ToList();
        return ApiResponse<PagedResult<TransportDriverCoverageWindowRowDto>>.SuccessResponse(
            new PagedResult<TransportDriverCoverageWindowRowDto>
            {
                Items = result.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                TotalCount = result.Count, Page = page, PageSize = pageSize
            });
    }

    public async Task<ApiResponse<string>> ExportDriverCoverageWindowCsvAsync(
        DateOnly fromDate, DateOnly toDate, long? campusId, string? search,
        bool attentionOnly, CancellationToken ct = default)
    {
        var first = await GetDriverCoverageWindowAsync(fromDate, toDate, campusId, 1, 100, search, attentionOnly, ct);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(first.Message, first.StatusCode);
        var rows = new List<TransportDriverCoverageWindowRowDto>(first.Data.Items);
        for (var page = 2; page <= (first.Data.TotalCount + 99) / 100; page++)
        {
            var next = await GetDriverCoverageWindowAsync(fromDate, toDate, campusId, page, 100, search, attentionOnly, ct);
            if (!next.Success || next.Data == null || next.Data.TotalCount != first.Data.TotalCount)
                return ApiResponse<string>.ErrorResponse("Dispatch window changed during export. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        var seen = new HashSet<Guid>();
        if (rows.Count != first.Data.TotalCount || rows.Any(x => !seen.Add(x.VehicleReference)))
            return ApiResponse<string>.ErrorResponse("Dispatch window changed during export. Retry.", 409);
        var csv = new StringBuilder("From,To,Campus,Vehicle,Capacity,Peak Riders,Peak Date,Minimum Free Seats,Attention Days,First Risk Date,First Issue\r\n");
        foreach (var row in rows)
            csv.Append(fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(CsvCell(row.CampusName)).Append(',').Append(CsvCell(row.VehicleNumber)).Append(',')
                .Append(row.Capacity.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.PeakReservedRiders.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.PeakDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.MinimumFreeSeats.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.AttentionDays.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.FirstAttentionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty).Append(',')
                .Append(CsvCell(row.FirstIssue)).Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
