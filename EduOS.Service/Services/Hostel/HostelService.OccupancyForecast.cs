using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    public static bool IsSupportedOccupancyForecastDate(DateOnly date, DateOnly today) =>
        date >= today && date <= today.AddDays(365);

    /// <summary>
    /// Advisory future-day room worklist. Uses canonical rooms, beds and allocations.
    /// Filters by tenant and permitted campus before bounded materialization, then filters
    /// risk before paging. Closed history with an effective future end still reserves its bed.
    /// </summary>
    public async Task<ApiResponse<PagedResult<HostelOccupancyForecastRowDto>>> GetOccupancyForecastAsync(
        DateOnly operatingDate, long? campusId, int page, int pageSize, string? search,
        bool attentionOnly, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<PagedResult<HostelOccupancyForecastRowDto>>();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (!IsSupportedOccupancyForecastDate(operatingDate, today) || campusId is <= 0 ||
            page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue || search?.Trim().Length > 100)
            return ApiResponse<PagedResult<HostelOccupancyForecastRowDto>>.ErrorResponse(
                "Choose today or a date within 365 days and valid paging filters.");

        var tenant = _currentUser.TenantId;
        var query = from room in _rooms.GetQueryable().AsNoTracking()
                    join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
                    join campus in _campuses.GetQueryable().AsNoTracking() on hostel.CampusId equals campus.Id
                    where room.TenantId == tenant && hostel.TenantId == tenant &&
                          campus.TenantId == tenant && !room.IsDeleted &&
                          !hostel.IsDeleted && !campus.IsDeleted && campus.IsActive &&
                          (!campusId.HasValue || hostel.CampusId == campusId.Value)
                    select new
                    {
                        RoomId = room.Id, room.RoomNumber, room.Capacity,
                        RoomActive = room.IsActive, HostelActive = hostel.IsActive,
                        HostelName = hostel.Name, CampusName = campus.Name, hostel.CampusId
                    };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.CampusName.Contains(term) ||
                x.HostelName.Contains(term) || x.RoomNumber.Contains(term));

        // Bounded by authorized candidate rooms, not the requested page: attention-only
        // TotalCount must describe the complete filtered cohort.
        var rooms = await query.OrderBy(x => x.CampusName).ThenBy(x => x.HostelName)
            .ThenBy(x => x.RoomNumber).ThenBy(x => x.RoomId)
            .Take(1001).ToListAsync(cancellationToken);
        if (rooms.Count > 1000)
            return ApiResponse<PagedResult<HostelOccupancyForecastRowDto>>.ErrorResponse(
                "Forecast exceeds 1,000 rooms. Narrow by campus or search.", 413);
        var roomIds = rooms.Select(x => x.RoomId).ToArray();
        var beds = await _beds.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && roomIds.Contains(x.HostelRoomId))
            .Select(x => new { x.Id, x.HostelRoomId, x.IsActive })
            .Take(10001).ToListAsync(cancellationToken);
        if (beds.Count > 10000)
            return ApiResponse<PagedResult<HostelOccupancyForecastRowDto>>.ErrorResponse(
                "Forecast exceeds 10,000 beds. Narrow the filters.", 413);

        var reservations = await (from allocation in _allocations.GetQueryable().AsNoTracking()
            join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
            where allocation.TenantId == tenant && bed.TenantId == tenant &&
                  !allocation.IsDeleted && !bed.IsDeleted && roomIds.Contains(bed.HostelRoomId) &&
                  allocation.State != HostelAllocationState.Cancelled &&
                  allocation.StartDate <= operatingDate &&
                  (!allocation.EndDate.HasValue || allocation.EndDate.Value >= operatingDate)
            select new { RoomId = bed.HostelRoomId, BedId = bed.Id })
            .Take(10001).ToListAsync(cancellationToken);
        if (reservations.Count > 10000)
            return ApiResponse<PagedResult<HostelOccupancyForecastRowDto>>.ErrorResponse(
                "Forecast exceeds 10,000 reservations. Narrow the filters.", 413);

        var bedsByRoom = beds.ToLookup(x => x.HostelRoomId);
        var reservationsByRoom = reservations.ToLookup(x => x.RoomId);
        var projected = rooms.Select(room => ProjectOccupancyForecastRoom(
            room.RoomId, room.CampusName, room.HostelName, room.RoomNumber,
            room.Capacity, room.HostelActive, room.RoomActive,
            bedsByRoom[room.RoomId].Select(x => (BedId: x.Id, x.IsActive)).ToArray(),
            reservationsByRoom[room.RoomId].Select(x => x.BedId).ToArray())).ToList();
        if (attentionOnly) projected = projected.Where(x => x.NeedsAttention).ToList();

        return ApiResponse<PagedResult<HostelOccupancyForecastRowDto>>.SuccessResponse(
            new PagedResult<HostelOccupancyForecastRowDto>
            {
                Page = page, PageSize = pageSize, TotalCount = projected.Count,
                Items = projected.Skip((page - 1) * pageSize).Take(pageSize).ToList()
            });
    }

    /// <summary>Pure, testable room projection; duplicate bookings never inflate physical bed occupancy.</summary>
    public static HostelOccupancyForecastRowDto ProjectOccupancyForecastRoom(
        long roomId, string campusName, string hostelName, string roomNumber,
        int capacity, bool hostelActive, bool roomActive,
        IReadOnlyList<(long BedId, bool IsActive)> beds, IReadOnlyList<long> reservationBedIds)
    {
        ArgumentNullException.ThrowIfNull(beds);
        ArgumentNullException.ThrowIfNull(reservationBedIds);
        var activeIds = beds.Where(x => x.IsActive).Select(x => x.BedId).ToHashSet();
        var allIds = beds.Select(x => x.BedId).ToHashSet();
        var distinctReservations = reservationBedIds.Where(allIds.Contains).ToHashSet();
        var duplicateCount = reservationBedIds.Count(x => allIds.Contains(x)) - distinctReservations.Count;
        var reserved = distinctReservations.Count;
        var inactiveReserved = distinctReservations.Count(x => !activeIds.Contains(x));
        var activeReserved = reserved - inactiveReserved;
        var activeBeds = activeIds.Count;
        var slots = hostelActive && roomActive
            ? Math.Max(0, Math.Min(Math.Max(0, capacity - reserved),
                Math.Max(0, activeBeds - activeReserved))) : 0;
        var issue = capacity <= 0 || duplicateCount > 0 || reserved > capacity || inactiveReserved > 0 ||
            (reserved > 0 && (!hostelActive || !roomActive)) ||
            activeBeds > capacity || (hostelActive && roomActive && capacity > 0 && activeBeds == 0);
        var status = capacity <= 0 ? "No capacity" :
            duplicateCount > 0 ? "Duplicate bookings" :
            reserved > capacity ? "Over capacity" :
            inactiveReserved > 0 ? "Inactive bed reserved" :
            reserved > 0 && (!hostelActive || !roomActive) ? "Inactive with reservations" :
            !hostelActive || !roomActive ? "Inactive" :
            activeBeds > capacity ? "Excess active beds" :
            capacity > 0 && activeBeds == 0 ? "Unprovisioned" :
            slots == 0 ? "Full" : "Available";
        return new HostelOccupancyForecastRowDto
        {
            RoomId = roomId, CampusName = campusName, HostelName = hostelName,
            RoomNumber = roomNumber, Capacity = capacity, ActiveBeds = activeBeds,
            ReservedBeds = reserved, AvailableBeds = slots,
            InactiveReservedBeds = inactiveReserved, DuplicateReservations = duplicateCount,
            Status = status, NeedsAttention = issue || status == "Full"
        };
    }

    public async Task<ApiResponse<string>> ExportOccupancyForecastCsvAsync(
        DateOnly operatingDate, long? campusId, string? search, bool attentionOnly,
        CancellationToken cancellationToken = default)
    {
        var first = await GetOccupancyForecastAsync(
            operatingDate, campusId, 1, 100, search, attentionOnly, cancellationToken);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(first.Message ?? "Forecast unavailable.", first.StatusCode);
        var count = first.Data.TotalCount;
        var rows = new List<HostelOccupancyForecastRowDto>(first.Data.Items);
        for (var page = 2; page <= (count + 99) / 100; page++)
        {
            var next = await GetOccupancyForecastAsync(
                operatingDate, campusId, page, 100, search, attentionOnly, cancellationToken);
            if (!next.Success || next.Data == null || next.Data.TotalCount != count || next.Data.Page != page)
                return ApiResponse<string>.ErrorResponse("Forecast changed during export. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        var seen = new HashSet<long>();
        if (rows.Count != count || rows.Any(x => !seen.Add(x.RoomId)))
            return ApiResponse<string>.ErrorResponse("Forecast changed during export. Retry.", 409);
        var csv = new StringBuilder(
            "Operating Date,Campus,Hostel,Room,Capacity,Active Beds,Reserved Beds,Available Beds,Inactive Reserved Beds,Duplicate Bookings,Status\r\n");
        foreach (var row in rows)
            csv.Append(operatingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(CsvCell(row.CampusName)).Append(',').Append(CsvCell(row.HostelName)).Append(',')
                .Append(CsvCell(row.RoomNumber)).Append(',')
                .Append(row.Capacity.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.ActiveBeds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.ReservedBeds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.AvailableBeds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.InactiveReservedBeds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.DuplicateReservations.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(CsvCell(row.Status)).Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
