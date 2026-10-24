using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Hostel;
public sealed partial class HostelService
{
    public async Task<ApiResponse<PagedResult<HostelRoomInventoryDto>>> GetRoomInventoryAsync(
        long? campusId, int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<PagedResult<HostelRoomInventoryDto>>();
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue ||
            campusId is <= 0 || search?.Trim().Length > 100)
            return ApiResponse<PagedResult<HostelRoomInventoryDto>>.ErrorResponse("Invalid room inventory filter.");
        var tenant = _currentUser.TenantId;
        var query = from room in _rooms.GetQueryable().AsNoTracking()
            join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
            join campus in _campuses.GetQueryable().AsNoTracking() on hostel.CampusId equals campus.Id
            where room.TenantId == tenant && hostel.TenantId == tenant && campus.TenantId == tenant &&
                campus.IsActive && (!campusId.HasValue || hostel.CampusId == campusId.Value)
            select new { room.Id, room.RoomNumber, room.Capacity, room.IsActive, room.RowVersion,
                HostelName = hostel.Name, HostelActive = hostel.IsActive, hostel.CampusId };
        if (!_currentUser.IsTenantAdmin)
        {
            var allowed = AllowedCampusIds(tenant);
            query = query.Where(x => allowed.Contains(x.CampusId));
        }
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.HostelName.Contains(term) || x.RoomNumber.Contains(term));
        var total = await query.CountAsync(cancellationToken);
        var rooms = await query.OrderBy(x => x.HostelName).ThenBy(x => x.RoomNumber).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var ids = rooms.Select(x => x.Id).ToArray();
        var activeBeds = await _beds.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && ids.Contains(x.HostelRoomId) && x.IsActive)
            .GroupBy(x => x.HostelRoomId).Select(g => new { RoomId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoomId, x => x.Count, cancellationToken);
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var reservations = await (from allocation in _allocations.GetQueryable().AsNoTracking()
            join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
            where allocation.TenantId == tenant && bed.TenantId == tenant &&
                ids.Contains(bed.HostelRoomId) && allocation.State == HostelAllocationState.Active &&
                (!allocation.EndDate.HasValue || allocation.EndDate.Value >= today)
            group allocation.HostelBedId by bed.HostelRoomId into g
            select new { RoomId = g.Key, Count = g.Distinct().Count() })
            .ToDictionaryAsync(x => x.RoomId, x => x.Count, cancellationToken);
        var occupied = await (from allocation in _allocations.GetQueryable().AsNoTracking()
            join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
            where allocation.TenantId == tenant && bed.TenantId == tenant &&
                ids.Contains(bed.HostelRoomId) && allocation.State == HostelAllocationState.Active &&
                allocation.StartDate <= today && (!allocation.EndDate.HasValue || allocation.EndDate.Value >= today)
            group allocation.HostelBedId by bed.HostelRoomId into g
            select new { RoomId = g.Key, Count = g.Distinct().Count() })
            .ToDictionaryAsync(x => x.RoomId, x => x.Count, cancellationToken);
        var items = rooms.Select(x => new HostelRoomInventoryDto
        {
            Id = x.Id, HostelName = x.HostelName, RoomNumber = x.RoomNumber, Capacity = x.Capacity,
            ActiveBeds = activeBeds.GetValueOrDefault(x.Id), OccupiedBeds = occupied.GetValueOrDefault(x.Id),
            HasActiveReservations = reservations.ContainsKey(x.Id),
            IsActive = x.IsActive, HostelActive = x.HostelActive,
            RowVersion = Convert.ToBase64String(x.RowVersion)
        }).ToList();
        return ApiResponse<PagedResult<HostelRoomInventoryDto>>.SuccessResponse(new PagedResult<HostelRoomInventoryDto>
        {
            Items = items, TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<HostelRoomInventoryDto>> SetRoomStateAsync(long roomId,
        SetHostelRoomStateRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<HostelRoomInventoryDto>();
        if (roomId <= 0 || request == null || !TryDecodeVersion(request.RowVersion, out var version))
            return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Room and row version are required.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var room = await _rooms.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == roomId, ct);
                if (room == null) return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Room not found.", 404);
                var hostel = await _hostels.GetQueryable().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == room.HostelId, ct);
                if (hostel == null || !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == hostel.CampusId && x.IsActive, ct))
                    return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Active campus not found.", 404);
                if (!await CanManageCampusAsync(hostel.CampusId, tenant, ct))
                    return Denied<HostelRoomInventoryDto>();
                var activeBeds = await _beds.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && x.HostelRoomId == room.Id && x.IsActive, ct);
                var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                var reserved = await (from allocation in _allocations.GetQueryable().AsNoTracking()
                    join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
                    where allocation.TenantId == tenant && bed.TenantId == tenant &&
                        bed.HostelRoomId == room.Id && allocation.State == HostelAllocationState.Active &&
                        (!allocation.EndDate.HasValue || allocation.EndDate.Value >= today)
                    select bed.Id).Distinct().CountAsync(ct);
                var occupied = await (from allocation in _allocations.GetQueryable().AsNoTracking()
                    join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
                    where allocation.TenantId == tenant && bed.TenantId == tenant &&
                        bed.HostelRoomId == room.Id && allocation.State == HostelAllocationState.Active &&
                        allocation.StartDate <= today && (!allocation.EndDate.HasValue || allocation.EndDate.Value >= today)
                    select bed.Id).Distinct().CountAsync(ct);
                HostelRoomInventoryDto Result() => new()
                {
                    Id = room.Id, HostelName = hostel.Name, RoomNumber = room.RoomNumber,
                    Capacity = room.Capacity, ActiveBeds = activeBeds, OccupiedBeds = occupied,
                    HasActiveReservations = reserved > 0,
                    IsActive = room.IsActive, HostelActive = hostel.IsActive,
                    RowVersion = Convert.ToBase64String(room.RowVersion)
                };
                if (room.IsActive == request.IsActive)
                    return ApiResponse<HostelRoomInventoryDto>.SuccessResponse(Result(), "Room already has the requested state.");
                if (!room.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Room changed. Reload and retry.", 409);
                if (!request.IsActive && reserved > 0)
                    return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Close active allocations before deactivating room.", 409);
                if (request.IsActive && (!hostel.IsActive || room.Capacity < 1 ||
                    activeBeds > room.Capacity || reserved > room.Capacity))
                    return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Hostel is inactive or room capacity is invalid.", 409);
                room.IsActive = request.IsActive;
                room.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                room.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(ct);
                return ApiResponse<HostelRoomInventoryDto>.SuccessResponse(Result(), "Room state updated.");
            }, System.Data.IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Room changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel room state conflict for tenant {TenantId}", tenant);
            return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Room state conflicts with another change.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hostel room state update failed for tenant {TenantId}", tenant);
            return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Room state update failed.", 500);
        }
    }
}
