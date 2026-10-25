using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Hostel;

/// <summary>Reorganize never-allocated physical beds without rewriting historical student stays.</summary>
public sealed partial class HostelService
{
    public static bool CanRelocateUnusedBed(bool activeBed, bool hasAllocationHistory,
        bool sameHostel, bool destinationActive, int physicalBeds, int capacity, bool duplicateNumber) =>
        activeBed && !hasAllocationHistory && sameHostel && destinationActive &&
        capacity is >= 1 and <= 1000 && physicalBeds >= 0 && physicalBeds < capacity && !duplicateNumber;

    public async Task<ApiResponse<PagedResult<HostelBedRelocationRoomDto>>> GetBedRelocationRoomsAsync(
        long bedId, int page, int pageSize, string? search, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<HostelBedRelocationRoomDto>>();
        if (bedId <= 0 || page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue || search?.Trim().Length > 100)
            return ApiResponse<PagedResult<HostelBedRelocationRoomDto>>.ErrorResponse("Invalid bed or room filter.");
        var tenant = _currentUser.TenantId;
        var bed = await _beds.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.Id == bedId, ct);
        if (bed == null) return ApiResponse<PagedResult<HostelBedRelocationRoomDto>>.ErrorResponse("Bed not found.", 404);
        var source = await _rooms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.Id == bed.HostelRoomId, ct);
        if (source == null) return ApiResponse<PagedResult<HostelBedRelocationRoomDto>>.ErrorResponse("Source room not found.", 404);
        var hostel = await _hostels.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.Id == source.HostelId, ct);
        if (hostel == null) return ApiResponse<PagedResult<HostelBedRelocationRoomDto>>.ErrorResponse("Hostel not found.", 404);
        if (!await CanManageCampusAsync(hostel.CampusId, tenant, ct))
            return Denied<PagedResult<HostelBedRelocationRoomDto>>();
        if (!bed.IsActive || !source.IsActive || !hostel.IsActive ||
            !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && !x.IsDeleted && x.Id == hostel.CampusId && x.IsActive, ct))
            return ApiResponse<PagedResult<HostelBedRelocationRoomDto>>.ErrorResponse("Active bed, room, hostel and campus are required.", 409);
        if (await _allocations.GetQueryable().IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.HostelBedId == bedId, ct))
            return ApiResponse<PagedResult<HostelBedRelocationRoomDto>>.ErrorResponse(
                "Beds with allocation history cannot be physically relocated.", 409);
        var label = bed.BedNumber.ToUpperInvariant();
        var query = _rooms.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && x.IsActive && x.HostelId == source.HostelId &&
            x.Id != source.Id && x.Capacity >= 1 && x.Capacity <= 1000 &&
            !_beds.GetQueryable().Any(b => b.TenantId == tenant && !b.IsDeleted &&
                b.HostelRoomId == x.Id && b.BedNumber.ToUpper() == label))
            .Select(x => new
            {
                x.Id, x.RoomNumber, x.Capacity, x.RowVersion,
                PhysicalBeds = _beds.GetQueryable().Count(b =>
                    b.TenantId == tenant && !b.IsDeleted && b.HostelRoomId == x.Id)
            });
        query = query.Where(x => x.PhysicalBeds < x.Capacity);
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.RoomNumber.Contains(term));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.RoomNumber).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<HostelBedRelocationRoomDto>>.SuccessResponse(
            new PagedResult<HostelBedRelocationRoomDto>
            {
                Page = page, PageSize = pageSize, TotalCount = total,
                Items = rows.Select(x => new HostelBedRelocationRoomDto
                {
                    Id = x.Id, RoomNumber = x.RoomNumber, Capacity = x.Capacity,
                    PhysicalBeds = x.PhysicalBeds, RowVersion = Convert.ToBase64String(x.RowVersion)
                }).ToList()
            });
    }

    public async Task<ApiResponse<HostelBedInventoryDto>> RelocateUnusedBedAsync(
        long bedId, RelocateUnusedHostelBedRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<HostelBedInventoryDto>();
        if (bedId <= 0 || request == null || request.TargetRoomId <= 0 ||
            !TryDecodeVersion(request.BedRowVersion, out var bedVersion) ||
            !TryDecodeVersion(request.TargetRoomRowVersion, out var targetVersion))
            return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Bed, destination and current row versions are required.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var bed = await _beds.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == bedId, token);
                if (bed == null) return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Bed not found.", 404);
                var source = await _rooms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == bed.HostelRoomId, token);
                var target = await _rooms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == request.TargetRoomId, token);
                if (source == null || target == null)
                    return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Room not found.", 404);
                var hostel = await _hostels.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == source.HostelId, token);
                if (hostel == null) return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Hostel not found.", 404);
                if (!await CanManageCampusAsync(hostel.CampusId, tenant, token))
                    return Denied<HostelBedInventoryDto>();
                if (!await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == hostel.CampusId && x.IsActive, token) ||
                    !hostel.IsActive || !source.IsActive)
                    return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Active source hostel and campus are required.", 409);
                var history = await _allocations.GetQueryable().IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.HostelBedId == bed.Id, token);
                // A historical allocation references this bed's room through its current FK.
                // Never relocate a bed with any prior/current/future allocation, including soft-deleted history.
                if (history) return ApiResponse<HostelBedInventoryDto>.ErrorResponse(
                    "Bed has allocation history. Transfer students through the allocation workflow instead.", 409);
                if (!bed.IsActive || target.HostelId != source.HostelId || !target.IsActive)
                    return ApiResponse<HostelBedInventoryDto>.ErrorResponse(
                        "Only active, never-allocated beds can move to an active room in the same hostel.", 409);
                HostelBedInventoryDto Result() => new()
                {
                    Id = bed.Id, HostelRoomId = bed.HostelRoomId, HostelName = hostel.Name,
                    RoomNumber = target.RoomNumber, BedNumber = bed.BedNumber, IsActive = bed.IsActive,
                    IsOccupied = false, HasActiveReservations = false, HasAllocationHistory = false,
                    RowVersion = Convert.ToBase64String(bed.RowVersion)
                };
                // Exact-state replay is safe: no audit/version changes and no duplicate physical bed.
                if (bed.HostelRoomId == target.Id)
                    return ApiResponse<HostelBedInventoryDto>.SuccessResponse(Result(), "Bed already belongs to this room.");
                if (!bed.RowVersion.AsSpan().SequenceEqual(bedVersion) ||
                    !target.RowVersion.AsSpan().SequenceEqual(targetVersion))
                    return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Bed or destination changed. Reload and retry.", 409);
                var physical = await _beds.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.HostelRoomId == target.Id, token);
                var label = bed.BedNumber.ToUpperInvariant();
                var duplicate = await _beds.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.HostelRoomId == target.Id &&
                    x.BedNumber.ToUpper() == label, token);
                if (!CanRelocateUnusedBed(bed.IsActive, history, target.HostelId == source.HostelId,
                    target.IsActive, physical, target.Capacity, duplicate))
                    return ApiResponse<HostelBedInventoryDto>.ErrorResponse(
                        "Destination is full, inactive, or already contains this bed number.", 409);
                bed.HostelRoomId = target.Id;
                bed.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                bed.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(token);
                _logger.LogInformation("Unused hostel bed relocated: tenant {TenantId}, bed {BedId}, from {FromRoom} to {ToRoom}",
                    tenant, bed.Id, source.Id, target.Id);
                return ApiResponse<HostelBedInventoryDto>.SuccessResponse(Result(), "Unused bed relocated.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Bed changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel bed relocation conflict for tenant {TenantId}", tenant);
            return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Bed relocation conflicts with another change.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Hostel bed relocation transaction aborted for tenant {TenantId}", tenant);
            return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Concurrent bed relocation. Reload and retry.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hostel bed relocation failed for tenant {TenantId}", tenant);
            return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Bed relocation failed.", 500);
        }
    }
}
