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
    /// Self-service historical readback, scoped by authenticated student.UserId and tenant.
    /// Past enrolments and retired physical inventory remain readable without exposing
    /// other learners, guardian contacts or financial terms.
    /// </summary>
    public async Task<ApiResponse<PagedResult<HostelAllocationHistoryRowDto>>> GetMyAllocationHistoryAsync(
        int page, int pageSize, HostelAllocationState? state, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) return Denied<PagedResult<HostelAllocationHistoryRowDto>>();
        if (page < 1 || pageSize is < 1 or > 50 || (long)(page - 1) * pageSize > int.MaxValue ||
            (state.HasValue && state.Value != HostelAllocationState.Active &&
                state.Value != HostelAllocationState.Closed && state.Value != HostelAllocationState.Cancelled))
            return ApiResponse<PagedResult<HostelAllocationHistoryRowDto>>.ErrorResponse("Invalid stay history filter.");

        var tenant = _currentUser.TenantId;
        var user = _currentUser.UserId;
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
                room.TenantId == tenant && hostel.TenantId == tenant && campus.TenantId == tenant &&
                student.UserId == user && !student.IsDeleted && !enrollment.IsDeleted &&
                !allocation.IsDeleted && enrollment.StudentId == allocation.StudentId &&
                enrollment.CampusId == hostel.CampusId
            select new
            {
                allocation.Id, allocation.StartDate, allocation.EndDate, allocation.State,
                allocation.PreviousAllocationId, allocation.StudentId,
                CampusId = campus.Id, CampusName = campus.Name,
                StudentName = student.FullName, student.StudentCode,
                HostelName = hostel.Name, room.RoomNumber, bed.BedNumber,
                HasSuccessor = _allocations.GetQueryable().Any(next =>
                    next.TenantId == tenant && next.StudentId == allocation.StudentId &&
                    next.PreviousAllocationId == allocation.Id && !next.IsDeleted)
            };
        if (state.HasValue) query = query.Where(x => x.State == state.Value);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.StartDate).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new HostelAllocationHistoryRowDto
            {
                Id = x.Id, CampusId = x.CampusId, CampusName = x.CampusName,
                StudentName = x.StudentName, StudentCode = x.StudentCode,
                HostelName = x.HostelName, RoomNumber = x.RoomNumber, BedNumber = x.BedNumber,
                StartDate = x.StartDate, EndDate = x.EndDate, State = x.State,
                IsTransferSuccessor = x.PreviousAllocationId.HasValue,
                HasTransferSuccessor = x.HasSuccessor
            }).ToListAsync(cancellationToken);
        return ApiResponse<PagedResult<HostelAllocationHistoryRowDto>>.SuccessResponse(
            new PagedResult<HostelAllocationHistoryRowDto>
            {
                Page = page, PageSize = pageSize, TotalCount = total, Items = rows
            });
    }

    /// <summary>Only the current authorized page is exported; fresh identity checks on each request.</summary>
    public async Task<ApiResponse<string>> ExportMyAllocationHistoryPageCsvAsync(
        int page, HostelAllocationState? state, CancellationToken cancellationToken = default)
    {
        var result = await GetMyAllocationHistoryAsync(page, 20, state, cancellationToken);
        if (!result.Success || result.Data == null)
            return ApiResponse<string>.ErrorResponse(result.Message ?? "Stay history unavailable.", result.StatusCode);
        var csv = new StringBuilder("Campus,Hostel,Room,Bed,Start Date,End Date,Status,Transferred In,Transferred Onward\r\n");
        foreach (var row in result.Data.Items)
            csv.Append(CsvCell(row.CampusName)).Append(',')
                .Append(CsvCell(row.HostelName)).Append(',')
                .Append(CsvCell(row.RoomNumber)).Append(',')
                .Append(CsvCell(row.BedNumber)).Append(',')
                .Append(row.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(row.State.ToString()).Append(',')
                .Append(row.IsTransferSuccessor ? "Yes" : "No").Append(',')
                .Append(row.HasTransferSuccessor ? "Yes" : "No").Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
