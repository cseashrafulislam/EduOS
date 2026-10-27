using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    public async Task<ApiResponse<HostelRoomInventoryDto>> SetRoomCapacityAsync(
        long roomId, SetHostelRoomCapacityRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<HostelRoomInventoryDto>();
        if (roomId <= 0 || request == null || request.Capacity is < 1 or > 1000 ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Room, valid capacity and row version are required.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var room = await _rooms.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == roomId && !x.IsDeleted, token);
                if (room == null) return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Room not found.", 404);
                var hostel = await _hostels.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == room.HostelId && !x.IsDeleted, token);
                if (hostel == null || !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.Id == hostel.CampusId && x.IsActive && !x.IsDeleted, token))
                    return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Active campus not found.", 404);
                if (!await CanManageCampusAsync(hostel.CampusId, tenant, token))
                    return Denied<HostelRoomInventoryDto>();
                var physicalBeds = await _beds.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && x.HostelRoomId == room.Id && !x.IsDeleted, token);
                var activeBeds = await _beds.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && x.HostelRoomId == room.Id && x.IsActive && !x.IsDeleted, token);
                var occupied = await (from allocation in _allocations.GetQueryable().AsNoTracking()
                    join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
                    where allocation.TenantId == tenant && bed.TenantId == tenant && !bed.IsDeleted &&
                        bed.HostelRoomId == room.Id && allocation.State == HostelAllocationState.Active
                    select bed.Id).Distinct().CountAsync(token);
                HostelRoomInventoryDto Result() => new()
                {
                    Id = room.Id, HostelName = hostel.Name, RoomNumber = room.RoomNumber,
                    Capacity = room.Capacity, ActiveBeds = activeBeds, OccupiedBeds = occupied,
                    IsActive = room.IsActive, HostelActive = hostel.IsActive,
                    RowVersion = Convert.ToBase64String(room.RowVersion)
                };
                if (room.Capacity == request.Capacity)
                    return ApiResponse<HostelRoomInventoryDto>.SuccessResponse(Result(), "Room already has the requested capacity.");
                if (!room.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Room changed. Reload and retry.", 409);
                if (request.Capacity < physicalBeds || request.Capacity < occupied)
                    return ApiResponse<HostelRoomInventoryDto>.ErrorResponse(
                        "Capacity cannot be less than physical beds or active allocations.", 409);
                room.Capacity = request.Capacity;
                room.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                room.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                return ApiResponse<HostelRoomInventoryDto>.SuccessResponse(Result(), "Room capacity updated.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Room changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel room capacity conflict for tenant {TenantId}", tenant);
            return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Capacity conflicts with another change.", 409);
        }
        catch (Exception ex) when (ex is System.Transactions.TransactionAbortedException || HasHostelSqlSerializationConflict(ex))
        {
            _logger.LogWarning(ex, "Hostel room capacity SQL concurrency conflict for tenant {TenantId}", tenant);
            return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Concurrent hostel room capacity change. Reload and retry.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hostel room capacity update failed for tenant {TenantId}", tenant);
            return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Room capacity update failed.", 500);
        }
    }
}
