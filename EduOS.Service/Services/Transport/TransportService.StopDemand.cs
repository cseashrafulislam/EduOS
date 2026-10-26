using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Transport;

public sealed partial class TransportService
{
    // Stop demand is a count of current transport assignments, not a measured passenger headcount.
    // A stop belongs to its canonical Route; never create a parallel stop inventory.
    public async Task<ApiResponse<PagedResult<TransportStopDemandRowDto>>> GetStopDemandAsync(
        long? campusId, int page, int pageSize, string? search, bool attentionOnly,
        CancellationToken ct = default, DateOnly? operatingDate = null)
    {
        if (!CanManage()) return Denied<PagedResult<TransportStopDemandRowDto>>();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var asOf = operatingDate ?? today;
        if (!IsSupportedStopDemandDate(asOf, today) ||
            page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            campusId is <= 0 || search?.Trim().Length > 100)
            return ApiResponse<PagedResult<TransportStopDemandRowDto>>.ErrorResponse("Invalid stop demand filters.");
        var tenant = _currentUser.TenantId;
        var query = from stop in _stops.GetQueryable().AsNoTracking()
                    join route in _routes.GetQueryable().AsNoTracking() on stop.RouteId equals route.Id
                    join campus in _campuses.GetQueryable().AsNoTracking() on route.CampusId equals (long?)campus.Id
                    where stop.TenantId == tenant && route.TenantId == tenant && campus.TenantId == tenant &&
                        !stop.IsDeleted && !route.IsDeleted && !campus.IsDeleted && campus.IsActive &&
                        (!campusId.HasValue || campus.Id == campusId.Value) &&
                        (!attentionOnly || !stop.IsActive || !route.IsActive)
                    select new { StopId = stop.Id, stop.Name, stop.SequenceNo, StopActive = stop.IsActive,
                        RouteId = route.Id, RouteReference = route.PublicId, RouteName = route.Name,
                        RouteCode = route.Code, RouteActive = route.IsActive,
                        CampusId = campus.Id, CampusName = campus.Name };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.Name.Contains(term) || x.RouteName.Contains(term) ||
                x.RouteCode.Contains(term) || x.CampusName.Contains(term));
        var total = await query.CountAsync(ct);
        var stops = await query.OrderBy(x => x.CampusName).ThenBy(x => x.RouteCode)
            .ThenBy(x => x.RouteId).ThenBy(x => x.SequenceNo).ThenBy(x => x.StopId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var stopIds = stops.Select(x => x.StopId).ToArray();
        var assignments = _assignments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && x.State == TransportAssignmentState.Active &&
            x.StartDate <= asOf && (!x.EndDate.HasValue || x.EndDate.Value >= asOf));
        // Join on the stop's actual route to avoid counting malformed cross-route references.
        var pickups = await (from assignment in assignments
                             join stop in _stops.GetQueryable().AsNoTracking()
                                 on assignment.PickupStopId equals (long?)stop.Id
                             where stop.TenantId == tenant && !stop.IsDeleted &&
                                 assignment.RouteId == stop.RouteId && stopIds.Contains(stop.Id)
                             group assignment by stop.Id into g
                             select new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var dropoffs = await (from assignment in assignments
                              join stop in _stops.GetQueryable().AsNoTracking()
                                  on assignment.DropStopId equals (long?)stop.Id
                              where stop.TenantId == tenant && !stop.IsDeleted &&
                                  assignment.RouteId == stop.RouteId && stopIds.Contains(stop.Id)
                              group assignment by stop.Id into g
                              select new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        return ApiResponse<PagedResult<TransportStopDemandRowDto>>.SuccessResponse(
            new PagedResult<TransportStopDemandRowDto>
            {
                Items = stops.Select(x =>
                {
                    var pickup = pickups.GetValueOrDefault(x.StopId);
                    var dropoff = dropoffs.GetValueOrDefault(x.StopId);
                    var inactive = !x.StopActive || !x.RouteActive;
                    return new TransportStopDemandRowDto
                    {
                        StopId = x.StopId, RouteReference = x.RouteReference,
                        CampusId = x.CampusId, CampusName = x.CampusName,
                        RouteCode = x.RouteCode, RouteName = x.RouteName, StopName = x.Name,
                        SequenceNo = x.SequenceNo, RouteActive = x.RouteActive, StopActive = x.StopActive,
                        CurrentPickups = pickup, CurrentDropoffs = dropoff,
                        NeedsAttention = inactive, InactiveWithRiders = inactive && (pickup > 0 || dropoff > 0)
                    };
                }).ToList(),
                TotalCount = total, Page = page, PageSize = pageSize
            });
    }

    /// <summary>Inclusive forecast horizon shared by paged demand and CSV export.</summary>
    public static bool IsSupportedStopDemandDate(DateOnly selected, DateOnly today) =>
        selected >= today && selected <= today.AddDays(365);

    public async Task<ApiResponse<string>> ExportStopDemandCsvAsync(
        long? campusId, string? search, bool attentionOnly, CancellationToken ct = default,
        DateOnly? operatingDate = null)
    {
        var asOf = operatingDate ?? DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var first = await GetStopDemandAsync(campusId, 1, 100, search, attentionOnly, ct, asOf);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(first.Message ?? "Stop demand unavailable.", first.StatusCode);
        if (first.Data.TotalCount > 1000)
            return ApiResponse<string>.ErrorResponse("Export exceeds 1,000 stops. Narrow the filters.", 413);
        var rows = new List<TransportStopDemandRowDto>(first.Data.Items);
        for (var page = 2; page <= (first.Data.TotalCount + 99) / 100; page++)
        {
            var next = await GetStopDemandAsync(campusId, page, 100, search, attentionOnly, ct, asOf);
            if (!next.Success || next.Data == null || next.Data.TotalCount != first.Data.TotalCount)
                return ApiResponse<string>.ErrorResponse("Stop demand changed during export. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        var unique = new HashSet<long>();
        if (rows.Count != first.Data.TotalCount || rows.Any(x => !unique.Add(x.StopId)))
            return ApiResponse<string>.ErrorResponse("Stop demand changed during export. Retry.", 409);
        var csv = new StringBuilder("Campus,Route Code,Route,Stop,Sequence,Route Active,Stop Active,Reserved Pickups,Reserved Dropoffs,Inactive With Riders,Operating Date\r\n");
        foreach (var row in rows)
            csv.Append(CsvCell(row.CampusName)).Append(',').Append(CsvCell(row.RouteCode)).Append(',')
                .Append(CsvCell(row.RouteName)).Append(',').Append(CsvCell(row.StopName)).Append(',')
                .Append(row.SequenceNo.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.RouteActive ? "Yes" : "No").Append(',')
                .Append(row.StopActive ? "Yes" : "No").Append(',')
                .Append(row.CurrentPickups.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.CurrentDropoffs.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.InactiveWithRiders ? "Yes" : "No").Append(',')
                .Append(asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
