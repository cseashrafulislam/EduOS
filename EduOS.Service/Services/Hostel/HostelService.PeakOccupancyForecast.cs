using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    public static bool IsSupportedPeakOccupancyPeriod(DateOnly fromDate, DateOnly toDate, DateOnly today) =>
        fromDate >= today && fromDate <= today.AddDays(365) &&
        toDate >= fromDate && toDate.DayNumber - fromDate.DayNumber <= 89;

    /// <summary>
    /// Peak counts distinct physical beds on each day, not the union of beds reserved across
    /// the whole interval. Every inclusive day is checked for capacity and duplicate risks.
    /// </summary>
    public static HostelPeakOccupancyForecastRowDto ProjectPeakOccupancyForecastRoom(
        long roomId, string campusName, string hostelName, string roomNumber,
        int capacity, bool hostelActive, bool roomActive,
        IReadOnlyList<(long BedId, bool IsActive)> beds,
        IReadOnlyList<(long BedId, DateOnly StartDate, DateOnly? EndDate)> bookings,
        DateOnly fromDate, DateOnly toDate)
    {
        ArgumentNullException.ThrowIfNull(beds);
        ArgumentNullException.ThrowIfNull(bookings);
        if (toDate < fromDate || toDate.DayNumber - fromDate.DayNumber > 89)
            throw new ArgumentOutOfRangeException(nameof(toDate));

        var peak = -1;
        var peakDate = fromDate;
        var minimumAvailable = int.MaxValue;
        var maximumDuplicates = 0;
        var maximumInactive = 0;
        var attentionDays = 0;
        var activeBeds = beds.Where(x => x.IsActive).Select(x => x.BedId).Distinct().Count();
        for (var day = fromDate.DayNumber; day <= toDate.DayNumber; day++)
        {
            var date = DateOnly.FromDayNumber(day);
            var occupied = bookings.Where(x => x.StartDate <= date &&
                (!x.EndDate.HasValue || x.EndDate.Value >= date))
                .Select(x => x.BedId).ToArray();
            var snapshot = ProjectOccupancyForecastRoom(roomId, campusName, hostelName, roomNumber,
                capacity, hostelActive, roomActive, beds, occupied);
            if (snapshot.ReservedBeds > peak)
            {
                peak = snapshot.ReservedBeds;
                peakDate = date;
            }
            minimumAvailable = Math.Min(minimumAvailable, snapshot.AvailableBeds);
            maximumDuplicates = Math.Max(maximumDuplicates, snapshot.DuplicateReservations);
            maximumInactive = Math.Max(maximumInactive, snapshot.InactiveReservedBeds);
            if (snapshot.NeedsAttention) attentionDays++;
        }

        var status = maximumDuplicates > 0 ? "Duplicate bookings" :
            peak > capacity ? "Over capacity" :
            maximumInactive > 0 ? "Inactive bed reserved" :
            peak > 0 && (!hostelActive || !roomActive) ? "Inactive with reservations" :
            capacity <= 0 ? "No capacity" :
            !hostelActive || !roomActive ? "Inactive" :
            activeBeds > capacity ? "Excess active beds" :
            activeBeds == 0 ? "Unprovisioned" :
            minimumAvailable == 0 ? "Full at peak" : "Available";

        return new HostelPeakOccupancyForecastRowDto
        {
            RoomId = roomId, CampusName = campusName, HostelName = hostelName,
            RoomNumber = roomNumber, Capacity = capacity, ActiveBeds = activeBeds,
            PeakReservedBeds = peak, PeakDate = peakDate,
            MinimumAvailableBeds = minimumAvailable,
            MaximumDuplicateBookings = maximumDuplicates,
            MaximumInactiveReservedBeds = maximumInactive,
            AttentionDays = attentionDays, NeedsAttention = attentionDays > 0, Status = status
        };
    }

    /// <summary>Read-only, tenant/campus-authorized interval projection with bounded materialization.</summary>
    private async Task<ApiResponse<List<HostelPeakOccupancyForecastRowDto>>> BuildPeakOccupancyForecastAsync(
        DateOnly fromDate, DateOnly toDate, long? campusId, string? search,
        bool attentionOnly, CancellationToken cancellationToken)
    {
        if (!CanManage()) return Denied<List<HostelPeakOccupancyForecastRowDto>>();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (!IsSupportedPeakOccupancyPeriod(fromDate, toDate, today) ||
            campusId is <= 0 || search?.Trim().Length > 100)
            return ApiResponse<List<HostelPeakOccupancyForecastRowDto>>.ErrorResponse(
                "Select a future interval of at most 90 days starting within 365 days.");

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

        var rooms = await query.OrderBy(x => x.CampusName).ThenBy(x => x.HostelName)
            .ThenBy(x => x.RoomNumber).ThenBy(x => x.RoomId)
            .Take(1001).ToListAsync(cancellationToken);
        if (rooms.Count > 1000)
            return ApiResponse<List<HostelPeakOccupancyForecastRowDto>>.ErrorResponse(
                "Peak forecast exceeds 1,000 rooms. Narrow by campus or search.", 413);
        if (rooms.Count == 0)
            return ApiResponse<List<HostelPeakOccupancyForecastRowDto>>.SuccessResponse(new());

        var roomIds = rooms.Select(x => x.RoomId).ToArray();
        var beds = await _beds.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && roomIds.Contains(x.HostelRoomId))
            .Select(x => new { x.Id, x.HostelRoomId, x.IsActive })
            .Take(10001).ToListAsync(cancellationToken);
        if (beds.Count > 10000)
            return ApiResponse<List<HostelPeakOccupancyForecastRowDto>>.ErrorResponse(
                "Peak forecast exceeds 10,000 beds. Narrow the filters.", 413);

        // Closed records can reserve their historical dates. Only cancellation removes a booking.
        var bookings = await (from allocation in _allocations.GetQueryable().AsNoTracking()
            join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
            where allocation.TenantId == tenant && bed.TenantId == tenant &&
                  !allocation.IsDeleted && !bed.IsDeleted && roomIds.Contains(bed.HostelRoomId) &&
                  allocation.State != HostelAllocationState.Cancelled &&
                  allocation.StartDate <= toDate &&
                  (!allocation.EndDate.HasValue || allocation.EndDate.Value >= fromDate)
            select new
            {
                RoomId = bed.HostelRoomId, BedId = bed.Id,
                allocation.StartDate, allocation.EndDate
            }).Take(10001).ToListAsync(cancellationToken);
        if (bookings.Count > 10000)
            return ApiResponse<List<HostelPeakOccupancyForecastRowDto>>.ErrorResponse(
                "Peak forecast exceeds 10,000 reservations. Narrow the filters.", 413);

        var bedsByRoom = beds.ToLookup(x => x.HostelRoomId);
        var bookingsByRoom = bookings.ToLookup(x => x.RoomId);
        var rows = rooms.Select(room => ProjectPeakOccupancyForecastRoom(
            room.RoomId, room.CampusName, room.HostelName, room.RoomNumber,
            room.Capacity, room.HostelActive, room.RoomActive,
            bedsByRoom[room.RoomId].Select(x => (BedId: x.Id, x.IsActive)).ToArray(),
            bookingsByRoom[room.RoomId].Select(x => (x.BedId, x.StartDate, x.EndDate)).ToArray(),
            fromDate, toDate)).ToList();
        if (attentionOnly) rows = rows.Where(x => x.NeedsAttention).ToList();
        return ApiResponse<List<HostelPeakOccupancyForecastRowDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<PagedResult<HostelPeakOccupancyForecastRowDto>>> GetPeakOccupancyForecastAsync(
        DateOnly fromDate, DateOnly toDate, long? campusId, int page, int pageSize,
        string? search, bool attentionOnly, CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<HostelPeakOccupancyForecastRowDto>>.ErrorResponse("Invalid paging filters.");
        var result = await BuildPeakOccupancyForecastAsync(
            fromDate, toDate, campusId, search, attentionOnly, cancellationToken);
        if (!result.Success || result.Data == null)
            return ApiResponse<PagedResult<HostelPeakOccupancyForecastRowDto>>.ErrorResponse(
                result.Message ?? "Peak forecast unavailable.", result.StatusCode);
        return ApiResponse<PagedResult<HostelPeakOccupancyForecastRowDto>>.SuccessResponse(
            new PagedResult<HostelPeakOccupancyForecastRowDto>
            {
                Page = page, PageSize = pageSize, TotalCount = result.Data.Count,
                Items = result.Data.Skip((page - 1) * pageSize).Take(pageSize).ToList()
            });
    }

    public async Task<ApiResponse<string>> ExportPeakOccupancyForecastCsvAsync(
        DateOnly fromDate, DateOnly toDate, long? campusId, string? search,
        bool attentionOnly, CancellationToken cancellationToken = default)
    {
        var result = await BuildPeakOccupancyForecastAsync(
            fromDate, toDate, campusId, search, attentionOnly, cancellationToken);
        if (!result.Success || result.Data == null)
            return ApiResponse<string>.ErrorResponse(
                result.Message ?? "Peak forecast unavailable.", result.StatusCode);
        var csv = new StringBuilder(
            "From,To,Campus,Hostel,Room,Capacity,Active Beds,Peak Reserved Beds,Peak Date,Minimum Available Beds,Maximum Duplicate Bookings,Maximum Inactive Reserved Beds,Attention Days,Status\r\n");
        foreach (var row in result.Data)
            csv.Append(fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(CsvCell(row.CampusName)).Append(',')
                .Append(CsvCell(row.HostelName)).Append(',')
                .Append(CsvCell(row.RoomNumber)).Append(',')
                .Append(row.Capacity.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.ActiveBeds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.PeakReservedBeds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.PeakDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.MinimumAvailableBeds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.MaximumDuplicateBookings.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.MaximumInactiveReservedBeds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.AttentionDays.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(CsvCell(row.Status)).Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
