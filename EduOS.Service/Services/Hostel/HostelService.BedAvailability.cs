using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    /// <summary>
    /// Future interval availability from canonical beds and allocations. This is advisory:
    /// AllocateAsync must independently recheck capacity in its write transaction.
    /// Both endpoints use the same bounded, campus-authorized cohort.
    /// </summary>
    public async Task<ApiResponse<PagedResult<HostelBedAvailabilityRowDto>>> GetBedAvailabilityAsync(
        long? campusId, DateOnly fromDate, DateOnly toDate, int page, int pageSize,
        string? search, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<PagedResult<HostelBedAvailabilityRowDto>>();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (campusId is <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue || search?.Trim().Length > 100 ||
            fromDate < today || toDate < fromDate || fromDate > today.AddDays(365) ||
            toDate.DayNumber - fromDate.DayNumber > 89)
            return ApiResponse<PagedResult<HostelBedAvailabilityRowDto>>.ErrorResponse(
                "Select a future start within 365 days and a period of at most 90 days.");

        var tenant = _currentUser.TenantId;
        var query = from bed in _beds.GetQueryable().AsNoTracking()
            join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
            join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
            join campus in _campuses.GetQueryable().AsNoTracking() on hostel.CampusId equals campus.Id
            where bed.TenantId == tenant && room.TenantId == tenant && hostel.TenantId == tenant &&
                campus.TenantId == tenant && !bed.IsDeleted && !room.IsDeleted &&
                !hostel.IsDeleted && !campus.IsDeleted && bed.IsActive && room.IsActive &&
                hostel.IsActive && campus.IsActive && room.Capacity > 0 &&
                (!campusId.HasValue || hostel.CampusId == campusId.Value)
            select new { BedId = bed.Id, RoomId = room.Id, bed.BedNumber,
                room.RoomNumber, room.Capacity, HostelName = hostel.Name, hostel.CampusId };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.HostelName.Contains(term) ||
                x.RoomNumber.Contains(term) || x.BedNumber.Contains(term));

        var candidateCount = await query.CountAsync(cancellationToken);
        if (candidateCount > 1000)
            return ApiResponse<PagedResult<HostelBedAvailabilityRowDto>>.ErrorResponse(
                "Availability exceeds 1,000 candidate beds. Narrow by campus or search.", 413);
        var candidates = await query.OrderBy(x => x.HostelName).ThenBy(x => x.RoomNumber)
            .ThenBy(x => x.BedNumber).ThenBy(x => x.BedId).ToListAsync(cancellationToken);
        var roomIds = candidates.Select(x => x.RoomId).Distinct().ToArray();
        // Closed history can overlap the requested period; cancelled allocations never reserve a bed.
        // Include inactive-bed reservations in the room capacity calculation.
        var reservations = await (from allocation in _allocations.GetQueryable().AsNoTracking()
            join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
            where allocation.TenantId == tenant && bed.TenantId == tenant &&
                !allocation.IsDeleted && !bed.IsDeleted && roomIds.Contains(bed.HostelRoomId) &&
                allocation.State != HostelAllocationState.Cancelled &&
                allocation.StartDate <= toDate &&
                (!allocation.EndDate.HasValue || allocation.EndDate.Value >= fromDate)
            select new { RoomId = bed.HostelRoomId, BedId = bed.Id })
            .Distinct().ToListAsync(cancellationToken);
        var reservedByRoom = reservations.GroupBy(x => x.RoomId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.BedId).ToHashSet());

        var available = new List<HostelBedAvailabilityRowDto>();
        foreach (var group in candidates.GroupBy(x => x.RoomId))
        {
            var reserved = reservedByRoom.GetValueOrDefault(group.Key) ?? new HashSet<long>();
            var capacity = group.First().Capacity;
            var freeSlots = Math.Max(0, capacity - reserved.Count);
            available.AddRange(group.Where(x => !reserved.Contains(x.BedId))
                .Take(freeSlots).Select(x => new HostelBedAvailabilityRowDto
                {
                    BedId = x.BedId, CampusId = x.CampusId, HostelName = x.HostelName,
                    RoomNumber = x.RoomNumber, BedNumber = x.BedNumber,
                    RoomCapacity = capacity, RemainingRoomSlots = freeSlots
                }));
        }
        var sorted = available.OrderBy(x => x.HostelName).ThenBy(x => x.RoomNumber)
            .ThenBy(x => x.BedNumber).ThenBy(x => x.BedId).ToList();
        return ApiResponse<PagedResult<HostelBedAvailabilityRowDto>>.SuccessResponse(
            new PagedResult<HostelBedAvailabilityRowDto>
            {
                Page = page, PageSize = pageSize, TotalCount = sorted.Count,
                Items = sorted.Skip((page - 1) * pageSize).Take(pageSize).ToList()
            });
    }

    public async Task<ApiResponse<string>> ExportBedAvailabilityCsvAsync(
        long? campusId, DateOnly fromDate, DateOnly toDate, string? search,
        CancellationToken cancellationToken = default)
    {
        var first = await GetBedAvailabilityAsync(campusId, fromDate, toDate, 1, 100, search, cancellationToken);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(first.Message ?? "Availability unavailable.", first.StatusCode);
        var rows = new List<HostelBedAvailabilityRowDto>(first.Data.Items);
        for (var page = 2; page <= (first.Data.TotalCount + 99) / 100; page++)
        {
            var next = await GetBedAvailabilityAsync(campusId, fromDate, toDate, page, 100, search, cancellationToken);
            if (!next.Success || next.Data == null || next.Data.TotalCount != first.Data.TotalCount)
                return ApiResponse<string>.ErrorResponse("Availability changed during export. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        var seen = new HashSet<long>();
        if (rows.Count != first.Data.TotalCount || rows.Any(x => !seen.Add(x.BedId)))
            return ApiResponse<string>.ErrorResponse("Availability changed during export. Retry.", 409);
        var csv = new StringBuilder("From,To,Hostel,Room,Bed,Room Capacity,Remaining Slots\r\n");
        foreach (var row in rows)
            csv.Append(fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(CsvCell(row.HostelName)).Append(',').Append(CsvCell(row.RoomNumber)).Append(',')
                .Append(CsvCell(row.BedNumber)).Append(',').Append(row.RoomCapacity).Append(',')
                .Append(row.RemainingRoomSlots).Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
