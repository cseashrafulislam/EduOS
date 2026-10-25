using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Hostel;

/// <summary>Correct the label of an unused canonical bed without rewriting allocation history.</summary>
public sealed partial class HostelService
{
    public static string NormalizeBedNumber(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    public static bool IsValidBedNumber(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 50 &&
        !value.Any(char.IsControl);

    public async Task<ApiResponse<HostelBedInventoryDto>> CorrectUnusedBedNumberAsync(
        long bedId, CorrectUnusedHostelBedNumberRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<HostelBedInventoryDto>();
        if (bedId <= 0 || request == null || !IsValidBedNumber(request.BedNumber) ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Bed number and row version are required.");
        var tenant = _currentUser.TenantId;
        var number = NormalizeBedNumber(request.BedNumber);
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var bed = await _beds.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == bedId, token);
                if (bed == null) return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Bed not found.", 404);
                var room = await _rooms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == bed.HostelRoomId, token);
                if (room == null) return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Room not found.", 404);
                var hostel = await _hostels.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == room.HostelId, token);
                if (hostel == null) return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Hostel not found.", 404);
                if (!await CanManageCampusAsync(hostel.CampusId, tenant, token))
                    return Denied<HostelBedInventoryDto>();
                if (!room.IsActive || !hostel.IsActive || !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == hostel.CampusId && x.IsActive, token))
                    return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Active room, hostel and campus are required.", 409);

                // IgnoreQueryFilters includes deleted historical allocations; never relabel a bed referenced by a stay.
                var history = await _allocations.GetQueryable().IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.HostelBedId == bed.Id, token);
                if (history) return ApiResponse<HostelBedInventoryDto>.ErrorResponse(
                    "Bed has allocation history. Its historical label cannot be changed.", 409);
                HostelBedInventoryDto Result() => new()
                {
                    Id = bed.Id, HostelRoomId = room.Id, HostelName = hostel.Name,
                    RoomNumber = room.RoomNumber, BedNumber = bed.BedNumber,
                    IsActive = bed.IsActive, IsOccupied = false, HasActiveReservations = false,
                    HasAllocationHistory = false, RowVersion = Convert.ToBase64String(bed.RowVersion)
                };
                // Exact-state retries are harmless, without changing audit fields or row version.
                if (NormalizeBedNumber(bed.BedNumber) == number)
                    return ApiResponse<HostelBedInventoryDto>.SuccessResponse(Result(), "Bed already has this number.");
                if (!bed.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Bed changed. Reload and retry.", 409);
                if (await _beds.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.HostelRoomId == room.Id &&
                    x.Id != bed.Id && x.BedNumber.ToUpper() == number, token))
                    return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Bed number already exists in this room.", 409);

                bed.BedNumber = number;
                bed.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                bed.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                _logger.LogInformation("Unused hostel bed number corrected: tenant {TenantId}, bed {BedId}", tenant, bed.Id);
                return ApiResponse<HostelBedInventoryDto>.SuccessResponse(Result(), "Bed number updated.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Bed changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel bed number correction conflict for tenant {TenantId}", tenant);
            return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Bed number conflicts with another change.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Hostel bed number correction transaction aborted for tenant {TenantId}", tenant);
            return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Concurrent bed change. Reload and retry.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hostel bed number correction failed for tenant {TenantId}", tenant);
            return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Bed number could not be updated.", 500);
        }
    }
}
