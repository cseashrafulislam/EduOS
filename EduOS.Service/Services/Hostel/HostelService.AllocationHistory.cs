using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    /// <summary>Campus-authorized, paged historical read over the canonical allocation aggregate.</summary>
    public async Task<ApiResponse<PagedResult<HostelAllocationHistoryRowDto>>> GetAllocationHistoryAsync(
        long? campusId, int page, int pageSize, string? search, HostelAllocationState? state,
        DateOnly? fromDate, DateOnly? toDate, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<PagedResult<HostelAllocationHistoryRowDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            campusId is <= 0 || search?.Trim().Length > 100 ||
            (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value) ||
            (state.HasValue && state.Value != HostelAllocationState.Active &&
                state.Value != HostelAllocationState.Closed && state.Value != HostelAllocationState.Cancelled))
            return ApiResponse<PagedResult<HostelAllocationHistoryRowDto>>.ErrorResponse("Invalid allocation history filter.");

        var tenant = _currentUser.TenantId;
        var query = from allocation in _allocations.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on allocation.StudentEnrollmentId equals enrollment.Id
            join student in _students.GetQueryable().AsNoTracking() on allocation.StudentId equals student.Id
            join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
            join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
            join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
            join campus in _campuses.GetQueryable().AsNoTracking() on hostel.CampusId equals campus.Id
            where allocation.TenantId == tenant && enrollment.TenantId == tenant &&
                student.TenantId == tenant && bed.TenantId == tenant && room.TenantId == tenant &&
                hostel.TenantId == tenant && campus.TenantId == tenant &&
                enrollment.StudentId == allocation.StudentId &&
                enrollment.CampusId == hostel.CampusId &&
                (!campusId.HasValue || hostel.CampusId == campusId.Value)
            select new
            {
                allocation.Id, allocation.StartDate, allocation.EndDate, allocation.State,
                CampusId = campus.Id, CampusName = campus.Name, StudentName = student.FullName,
                student.StudentCode, HostelName = hostel.Name, room.RoomNumber, bed.BedNumber
            };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.StudentName.Contains(term) || x.StudentCode.Contains(term) ||
                x.HostelName.Contains(term) || x.RoomNumber.Contains(term) || x.BedNumber.Contains(term));
        if (state.HasValue) query = query.Where(x => x.State == state.Value);
        if (fromDate.HasValue) query = query.Where(x => x.StartDate >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(x => x.StartDate <= toDate.Value);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.StartDate).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return ApiResponse<PagedResult<HostelAllocationHistoryRowDto>>.SuccessResponse(
            new PagedResult<HostelAllocationHistoryRowDto>
            {
                Page = page, PageSize = pageSize, TotalCount = total,
                Items = rows.Select(x => new HostelAllocationHistoryRowDto
                {
                    Id = x.Id, CampusId = x.CampusId, CampusName = x.CampusName,
                    StudentName = x.StudentName, StudentCode = x.StudentCode,
                    HostelName = x.HostelName, RoomNumber = x.RoomNumber, BedNumber = x.BedNumber,
                    StartDate = x.StartDate, EndDate = x.EndDate, State = x.State
                }).ToList()
            });
    }
}
