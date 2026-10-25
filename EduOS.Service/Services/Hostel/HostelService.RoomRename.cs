using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    public static string NormalizeRoomNumber(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    public static bool IsValidRoomNumber(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 50 &&
        !value.Any(char.IsControl);

    /// <summary>Correct the room label without changing its ID, beds, allocation history or Finance-owned rent.</summary>
    public async Task<ApiResponse<HostelRoomInventoryDto>> RenameRoomAsync(
        long roomId, RenameHostelRoomRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<HostelRoomInventoryDto>();
        if (roomId <= 0 || request == null || !IsValidRoomNumber(request.RoomNumber) ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Room number and row version are required.");
        var tenant = _currentUser.TenantId;
        var number = NormalizeRoomNumber(request.RoomNumber);
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var room = await _rooms.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == roomId, token);
                if (room == null) return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Room not found.", 404);
                var hostel = await _hostels.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == room.HostelId, token);
                if (hostel == null || !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == hostel.CampusId && x.IsActive, token))
                    return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Active campus not found.", 404);
                if (!await CanManageCampusAsync(hostel.CampusId, tenant, token))
                    return Denied<HostelRoomInventoryDto>();

                var activeBeds = await _beds.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.HostelRoomId == room.Id && x.IsActive, token);
                var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                var reservations = from allocation in _allocations.GetQueryable().AsNoTracking()
                    join bed in _beds.GetQueryable().AsNoTracking() on allocation.HostelBedId equals bed.Id
                    where allocation.TenantId == tenant && bed.TenantId == tenant &&
                        !allocation.IsDeleted && !bed.IsDeleted && bed.HostelRoomId == room.Id &&
                        allocation.State == HostelAllocationState.Active &&
                        (!allocation.EndDate.HasValue || allocation.EndDate.Value >= today)
                    select new { bed.Id, allocation.StartDate };
                var reserved = await reservations.Select(x => x.Id).Distinct().CountAsync(token);
                var occupied = await reservations.Where(x => x.StartDate <= today)
                    .Select(x => x.Id).Distinct().CountAsync(token);
                HostelRoomInventoryDto Result() => new()
                {
                    Id = room.Id, HostelName = hostel.Name, RoomNumber = room.RoomNumber,
                    Capacity = room.Capacity, ActiveBeds = activeBeds, OccupiedBeds = occupied,
                    HasActiveReservations = reserved > 0, IsActive = room.IsActive,
                    HostelActive = hostel.IsActive, RowVersion = Convert.ToBase64String(room.RowVersion)
                };
                // Exact-state retries must not modify audit timestamps or the concurrency token.
                if (NormalizeRoomNumber(room.RoomNumber) == number)
                    return ApiResponse<HostelRoomInventoryDto>.SuccessResponse(Result(), "Room already has this number.");
                if (!room.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Room changed. Reload and retry.", 409);
                if (await _rooms.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.HostelId == room.HostelId &&
                    x.Id != room.Id && x.RoomNumber.ToUpper() == number, token))
                    return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Room number already exists in this hostel.", 409);
                room.RoomNumber = number;
                room.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                room.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                _logger.LogInformation("Hostel room label corrected: tenant {TenantId}, room {RoomId}", tenant, room.Id);
                return ApiResponse<HostelRoomInventoryDto>.SuccessResponse(Result(), "Room number updated.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Room changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel room rename conflict for tenant {TenantId}", tenant);
            return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Room number conflicts with another change.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Hostel room rename transaction aborted for tenant {TenantId}", tenant);
            return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Concurrent room change. Reload and retry.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hostel room rename failed for tenant {TenantId}", tenant);
            return ApiResponse<HostelRoomInventoryDto>.ErrorResponse("Room number could not be updated.", 500);
        }
    }
}
