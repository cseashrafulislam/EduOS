using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Entities.Hostel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Hostel;

/// <summary>Creates physical rooms in the canonical Hostel aggregate; Finance owns pricing.</summary>
public sealed partial class HostelService
{
    public async Task<ApiResponse<HostelRoomInventoryDto>> CreateRoomAsync(
        CreateHostelRoomRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<HostelRoomInventoryDto>();
        if (request == null || request.HostelReference == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.RoomNumber) ||
            request.RoomNumber.Trim().Length > 50 || request.Capacity is < 1 or > 1000)
            return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Valid hostel, room number and capacity are required.");
        var tenant = _currentUser.TenantId;
        var number = request.RoomNumber.Trim().ToUpperInvariant();
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var hostel = await _hostels.GetQueryable().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenant && !x.IsDeleted &&
                        x.PublicId == request.HostelReference, token);
                if (hostel == null) return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Hostel not found.", 404);
                if (!await CanManageCampusAsync(hostel.CampusId, tenant, token))
                    return Denied<HostelRoomInventoryDto>();
                var campusActive = await _campuses.GetQueryable().AsNoTracking()
                    .AnyAsync(x => x.TenantId == tenant && x.Id == hostel.CampusId &&
                        !x.IsDeleted && x.IsActive, token);
                if (!campusActive || !hostel.IsActive)
                    return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Active campus and hostel are required.", 409);
                var existing = await _rooms.GetQueryable().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenant && !x.IsDeleted &&
                        x.HostelId == hostel.Id && x.RoomNumber.ToUpper() == number, token);
                if (existing != null)
                {
                    if (existing.Capacity != request.Capacity)
                        return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Room number already exists with different capacity.", 409);
                    var activeBeds = await _beds.GetQueryable().AsNoTracking().CountAsync(x =>
                        x.TenantId == tenant && !x.IsDeleted && x.HostelRoomId == existing.Id && x.IsActive, token);
                    var occupied = await (from allocation in _allocations.GetQueryable().AsNoTracking()
                        join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
                        where allocation.TenantId == tenant && bed.TenantId == tenant &&
                            !allocation.IsDeleted && !bed.IsDeleted && bed.HostelRoomId == existing.Id &&
                            allocation.State == EduOS.Core.Enums.Domain.HostelAllocationState.Active
                        select bed.Id).Distinct().CountAsync(token);
                    return ApiResponse<HostelRoomInventoryDto>.SuccessResponse(new HostelRoomInventoryDto
                    {
                        Id = existing.Id, HostelName = hostel.Name, RoomNumber = existing.RoomNumber,
                        Capacity = existing.Capacity, ActiveBeds = activeBeds, OccupiedBeds = occupied,
                        IsActive = existing.IsActive, HostelActive = hostel.IsActive,
                        RowVersion = Convert.ToBase64String(existing.RowVersion)
                    }, "Room already exists; no duplicate created.");
                }
                var room = new HostelRoom
                {
                    TenantId = tenant, HostelId = hostel.Id, RoomNumber = number,
                    Capacity = request.Capacity, IsActive = true,
                    CreatedAt = _clock.GetUtcNow().UtcDateTime, CreatedBy = _currentUser.UserId
                    // RentPerBed is intentionally untouched: Finance owns monetary settings.
                };
                await _rooms.AddAsync(room);
                await _unitOfWork.SaveChangesAsync(token);
                return ApiResponse<HostelRoomInventoryDto>.SuccessResponse(new HostelRoomInventoryDto
                {
                    Id = room.Id, HostelName = hostel.Name, RoomNumber = room.RoomNumber,
                    Capacity = room.Capacity, ActiveBeds = 0, OccupiedBeds = 0,
                    IsActive = room.IsActive, HostelActive = hostel.IsActive,
                    RowVersion = Convert.ToBase64String(room.RowVersion)
                }, "Hostel room created.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel room creation conflict for tenant {TenantId}", tenant);
            return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Room creation conflicts with another change.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hostel room creation failed for tenant {TenantId}", tenant);
            return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Room creation failed.", 500);
        }
    }
}
