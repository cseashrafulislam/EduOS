using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    public static bool IsSupportedResidentRosterDate(DateOnly date, DateOnly today) =>
        date >= new DateOnly(2000, 1, 1) && date <= today;

    /// <summary>
    /// Scheduled occupants on a past/current date, including subsequently closed stays.
    /// This is not a physical check-in log. Historical inventory is read-only and
    /// may be retired, but every join remains tenant/campus scoped.
    /// </summary>
    public async Task<ApiResponse<PagedResult<HostelResidentRosterRowDto>>> GetResidentRosterAsync(
        DateOnly operatingDate, long? campusId, int page, int pageSize, string? search,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<PagedResult<HostelResidentRosterRowDto>>();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (!IsSupportedResidentRosterDate(operatingDate, today) ||
            campusId is <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue || search?.Trim().Length > 100)
            return ApiResponse<PagedResult<HostelResidentRosterRowDto>>.ErrorResponse(
                "Choose a valid historical date, campus and paging filters.");

        var tenant = _currentUser.TenantId;
        var query =
            from allocation in _allocations.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on allocation.StudentEnrollmentId equals enrollment.Id
            join student in _students.GetQueryable().AsNoTracking()
                on allocation.StudentId equals student.Id
            join bed in _beds.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                on allocation.HostelBedId equals bed.Id
            join room in _rooms.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                on bed.HostelRoomId equals room.Id
            join hostel in _hostels.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                on room.HostelId equals hostel.Id
            join campus in _campuses.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                on hostel.CampusId equals campus.Id
            where allocation.TenantId == tenant && enrollment.TenantId == tenant &&
                student.TenantId == tenant && bed.TenantId == tenant &&
                room.TenantId == tenant && hostel.TenantId == tenant &&
                campus.TenantId == tenant &&
                !allocation.IsDeleted && !enrollment.IsDeleted && !student.IsDeleted &&
                enrollment.StudentId == allocation.StudentId &&
                enrollment.CampusId == hostel.CampusId &&
                (allocation.State == HostelAllocationState.Active ||
                 allocation.State == HostelAllocationState.Closed) &&
                allocation.StartDate <= operatingDate &&
                (!allocation.EndDate.HasValue || allocation.EndDate.Value >= operatingDate) &&
                (!campusId.HasValue || hostel.CampusId == campusId.Value)
            select new
            {
                allocation.Id, allocation.StartDate, allocation.EndDate, allocation.State,
                CampusId = campus.Id, CampusName = campus.Name, HostelName = hostel.Name,
                room.RoomNumber, bed.BedNumber, student.StudentCode,
                StudentName = student.FullName
            };

        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.StudentCode.Contains(term) ||
                x.StudentName.Contains(term) || x.HostelName.Contains(term) ||
                x.RoomNumber.Contains(term) || x.BedNumber.Contains(term));

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(x => x.HostelName).ThenBy(x => x.RoomNumber)
            .ThenBy(x => x.BedNumber).ThenBy(x => x.StudentCode).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        return ApiResponse<PagedResult<HostelResidentRosterRowDto>>.SuccessResponse(
            new PagedResult<HostelResidentRosterRowDto>
            {
                Page = page, PageSize = pageSize, TotalCount = total,
                Items = rows.Select(x => new HostelResidentRosterRowDto
                {
                    AllocationId = x.Id, CampusName = x.CampusName,
                    HostelName = x.HostelName, RoomNumber = x.RoomNumber,
                    BedNumber = x.BedNumber, StudentCode = x.StudentCode,
                    StudentName = x.StudentName, StartDate = x.StartDate,
                    EndDate = x.EndDate,
                    CurrentState = x.State == HostelAllocationState.Closed ? "Closed" : "Active"
                }).ToList()
            });
    }

    /// <summary>Fresh authorization per page; bounded to 1,000 scheduled stays.</summary>
    public async Task<ApiResponse<string>> ExportResidentRosterCsvAsync(
        DateOnly operatingDate, long? campusId, string? search,
        CancellationToken cancellationToken = default)
    {
        var first = await GetResidentRosterAsync(operatingDate, campusId, 1, 100,
            search, cancellationToken);
        if (!first.Success || first.Data == null)
            return ApiResponse<string>.ErrorResponse(
                first.Message ?? "Resident roster unavailable.", first.StatusCode);
        var count = first.Data.TotalCount;
        if (count > 1000)
            return ApiResponse<string>.ErrorResponse(
                "Roster exceeds 1,000 stays. Narrow the campus or search.", 413);
        var rows = new List<HostelResidentRosterRowDto>(first.Data.Items);
        for (var page = 2; page <= (count + 99) / 100; page++)
        {
            var next = await GetResidentRosterAsync(operatingDate, campusId, page, 100,
                search, cancellationToken);
            if (!next.Success || next.Data == null ||
                next.Data.TotalCount != count || next.Data.Page != page)
                return ApiResponse<string>.ErrorResponse(
                    "Roster changed during export. Retry.", 409);
            rows.AddRange(next.Data.Items);
        }
        var ids = new HashSet<long>();
        if (rows.Count != count || rows.Any(x => !ids.Add(x.AllocationId)))
            return ApiResponse<string>.ErrorResponse(
                "Roster changed during export. Retry.", 409);

        var csv = new StringBuilder(
            "Operating Date,Campus,Hostel,Room,Bed,Student Code,Student Name,Start Date,End Date,Current Allocation State\r\n");
        foreach (var row in rows)
            csv.Append(operatingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(CsvCell(row.CampusName)).Append(',')
                .Append(CsvCell(row.HostelName)).Append(',')
                .Append(CsvCell(row.RoomNumber)).Append(',')
                .Append(CsvCell(row.BedNumber)).Append(',')
                .Append(CsvCell(row.StudentCode)).Append(',')
                .Append(CsvCell(row.StudentName)).Append(',')
                .Append(row.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty).Append(',')
                .Append(CsvCell(row.CurrentState)).Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
