using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    public static string ClassifyCheckoutFollowUp(DateOnly start, DateOnly end, DateOnly today)
    {
        if (end < start) return "Invalid dates";
        if (end < today) return "Overdue checkout";
        if (end == today) return "Checkout today";
        return "Checkout upcoming";
    }

    /// <summary>
    /// Staff-only worklist for finite Active stays with an overdue or imminent checkout.
    /// No automatic state change: Finance settlement and physical departure are separate workflows.
    /// </summary>
    public async Task<ApiResponse<PagedResult<HostelCheckoutFollowUpRowDto>>> GetCheckoutFollowUpsAsync(
        long? campusId, int page, int pageSize, string? search, int daysAhead, bool overdueOnly,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<PagedResult<HostelCheckoutFollowUpRowDto>>();
        if (campusId is <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue || search?.Trim().Length > 100 ||
            daysAhead is < 0 or > 90)
            return ApiResponse<PagedResult<HostelCheckoutFollowUpRowDto>>.ErrorResponse(
                "Invalid checkout worklist filter.");
        var tenant = _currentUser.TenantId;
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var cutoff = today.AddDays(daysAhead);
        var query = from allocation in _allocations.GetQueryable().AsNoTracking()
            join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
            join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
            join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
            join campus in _campuses.GetQueryable().AsNoTracking() on hostel.CampusId equals campus.Id
            where allocation.TenantId == tenant && bed.TenantId == tenant &&
                room.TenantId == tenant && hostel.TenantId == tenant && campus.TenantId == tenant &&
                !allocation.IsDeleted && !bed.IsDeleted && !room.IsDeleted &&
                !hostel.IsDeleted && !campus.IsDeleted &&
                allocation.State == HostelAllocationState.Active &&
                allocation.EndDate.HasValue && allocation.EndDate.Value <= cutoff &&
                (allocation.StartDate <= today || allocation.EndDate.Value < allocation.StartDate) &&
                (!campusId.HasValue || hostel.CampusId == campusId.Value)
            select new
            {
                allocation.Id, allocation.StartDate, allocation.EndDate,
                CampusId = campus.Id, CampusName = campus.Name, HostelName = hostel.Name,
                room.RoomNumber, bed.BedNumber
            };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        if (overdueOnly) query = query.Where(x =>
            x.EndDate!.Value < today || x.EndDate.Value < x.StartDate);
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.CampusName.Contains(term) || x.HostelName.Contains(term) ||
                x.RoomNumber.Contains(term) || x.BedNumber.Contains(term));
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(x => x.EndDate).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var items = rows.Select(x =>
        {
            var end = x.EndDate!.Value;
            return new HostelCheckoutFollowUpRowDto
            {
                AllocationId = x.Id, CampusId = x.CampusId, CampusName = x.CampusName,
                HostelName = x.HostelName, RoomNumber = x.RoomNumber, BedNumber = x.BedNumber,
                StartDate = x.StartDate, EndDate = end,
                DaysUntilCheckout = end.DayNumber - today.DayNumber,
                FollowUp = ClassifyCheckoutFollowUp(x.StartDate, end, today)
            };
        }).ToList();
        return ApiResponse<PagedResult<HostelCheckoutFollowUpRowDto>>.SuccessResponse(
            new PagedResult<HostelCheckoutFollowUpRowDto>
            {
                Items = items, TotalCount = total, Page = page, PageSize = pageSize
            });
    }

    /// <summary>Bounded CSV uses the same authorized query and checks for pagination drift.</summary>
    public async Task<ApiResponse<string>> ExportCheckoutFollowUpsCsvAsync(
        long? campusId, string? search, int daysAhead, bool overdueOnly,
        CancellationToken cancellationToken = default)
    {
        var first = await GetCheckoutFollowUpsAsync(campusId, 1, 100, search,
            daysAhead, overdueOnly, cancellationToken);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(first.Message ?? "Checkout report unavailable.", first.StatusCode);
        var count = first.Data.TotalCount;
        if (count > 1000)
            return ApiResponse<string>.ErrorResponse("Export exceeds 1,000 allocations. Narrow the filters.", 413);
        var rows = new List<HostelCheckoutFollowUpRowDto>(first.Data.Items);
        for (var page = 2; page <= (count + 99) / 100; page++)
        {
            var next = await GetCheckoutFollowUpsAsync(campusId, page, 100, search,
                daysAhead, overdueOnly, cancellationToken);
            if (!next.Success || next.Data == null || next.Data.TotalCount != count ||
                next.Data.Page != page)
                return ApiResponse<string>.ErrorResponse("Checkout report changed during export. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        var seen = new HashSet<long>();
        if (rows.Count != count || rows.Any(x => !seen.Add(x.AllocationId)))
            return ApiResponse<string>.ErrorResponse("Checkout report changed during export. Retry.", 409);
        var csv = new StringBuilder("Campus,Hostel,Room,Bed,Start Date,Checkout Date,Days Until Checkout,Follow Up\r\n");
        foreach (var row in rows)
            csv.Append(CsvCell(row.CampusName)).Append(',')
                .Append(CsvCell(row.HostelName)).Append(',')
                .Append(CsvCell(row.RoomNumber)).Append(',')
                .Append(CsvCell(row.BedNumber)).Append(',')
                .Append(row.StartDate.ToString("yyyy-MM-dd")).Append(',')
                .Append(row.EndDate.ToString("yyyy-MM-dd")).Append(',')
                .Append(row.DaysUntilCheckout).Append(',')
                .Append(CsvCell(row.FollowUp)).Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
