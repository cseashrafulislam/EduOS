using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Academic;

public sealed partial class AcademicSetupService
{
    public async Task<ApiResponse<PagedResult<RoomDto>>> GetRoomsAsync(
        string? search, long? campusId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Denied<PagedResult<RoomDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || search?.Length > 80 ||
            campusId is <= 0 || (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<RoomDto>>("Invalid room search or page.");
        var tenant = _currentUser.TenantId;
        var query = _rooms.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted);
        if (campusId.HasValue)
        {
            if (!await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id == campusId.Value && !x.IsDeleted, ct))
                return Error<PagedResult<RoomDto>>("Campus not found.", 404);
            query = query.Where(x => x.CampusId == campusId.Value);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Name.StartsWith(term) || x.Code.StartsWith(term));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.CampusId).ThenBy(x => x.Code).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var names = await CampusNamesForRoomsAsync(rows.Select(x => x.CampusId).Distinct().ToArray(), ct);
        return ApiResponse<PagedResult<RoomDto>>.SuccessResponse(new PagedResult<RoomDto>
        {
            Items = rows.Select(x => MapRoomWithCampus(x, names)).ToList(),
            TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<RoomDto>> GetRoomAsync(long id, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Denied<RoomDto>();
        if (id <= 0) return Error<RoomDto>("Valid room ID is required.");
        var tenant = _currentUser.TenantId;
        var row = await _rooms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == id && !x.IsDeleted, ct);
        if (row == null) return Error<RoomDto>("Room not found.", 404);
        var names = await CampusNamesForRoomsAsync([row.CampusId], ct);
        return ApiResponse<RoomDto>.SuccessResponse(MapRoomWithCampus(row, names));
    }

    public Task<ApiResponse<RoomDto>> UpdateRoomAsync(
        long id, UpdateAcademicRoomRequestDto request, CancellationToken ct = default)
    {
        if (!CanManageDepartments()) return Task.FromResult(Denied<RoomDto>());
        if (id <= 0 || request == null || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 100 || request.Capacity is < 0 or > 100000 ||
            request.RoomType?.Length > 100)
            return Task.FromResult(Error<RoomDto>("Invalid room name, type or capacity."));
        byte[] expected;
        try { expected = Convert.FromBase64String(request.RowVersion ?? string.Empty); }
        catch (FormatException) { return Task.FromResult(Error<RoomDto>("Invalid room version.")); }
        if (expected.Length != 8) return Task.FromResult(Error<RoomDto>("Valid room version is required."));
        return ExecuteWriteAsync("update academic room", async () =>
        {
            var tenant = _currentUser.TenantId;
            var row = await _rooms.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == id && !x.IsDeleted, ct);
            if (row == null) return Error<RoomDto>("Room not found.", 404);
            var name = request.Name.Trim();
            var type = Trim(request.RoomType);
            if (row.Name == name && row.Capacity == request.Capacity && row.RoomType == type)
                return ApiResponse<RoomDto>.SuccessResponse(MapRoom(row), "Room settings already applied.");
            if (!CryptographicOperations.FixedTimeEquals(row.RowVersion, expected))
                return Error<RoomDto>("Room changed concurrently. Reload before saving.", 409);
            // A room may already be used by timetable entries. Never reduce capacity here.
            // Status, campus and code cannot be changed by this endpoint.
            if (request.Capacity < row.Capacity)
                return Error<RoomDto>("Capacity reduction requires timetable reconciliation.", 409);
            row.Name = name;
            row.Capacity = request.Capacity;
            row.RoomType = type;
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedBy = _currentUser.UserId;
            _rooms.Update(row);
            await _unitOfWork.SaveChangesAsync(ct);
            return ApiResponse<RoomDto>.SuccessResponse(MapRoom(row), "Room metadata saved.");
        }, serializable: true);
    }

    private Task<Dictionary<long, string>> CampusNamesForRoomsAsync(long[] ids, CancellationToken ct) =>
        _campuses.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _currentUser.TenantId && ids.Contains(x.Id) && !x.IsDeleted)
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

    private static RoomDto MapRoomWithCampus(EduOS.Core.Entities.Academic.Room room, Dictionary<long, string> names)
    {
        var dto = MapRoom(room);
        dto.CampusName = names.GetValueOrDefault(room.CampusId) ?? string.Empty;
        return dto;
    }
}
