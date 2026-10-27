using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Entities.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Hostel;

/// <summary>Physical bed provisioning within the canonical HostelRoom aggregate.</summary>
public sealed partial class HostelService
{
    public async Task<ApiResponse<HostelBedInventoryDto>> CreateBedAsync(
        CreateHostelBedRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<HostelBedInventoryDto>();
        if (request == null || request.HostelRoomId <= 0 || string.IsNullOrWhiteSpace(request.BedNumber) ||
            request.BedNumber.Trim().Length > 50 || !TryDecodeVersion(request.RoomRowVersion, out var version))
            return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Valid room, bed number and room version are required.");
        var tenant = _currentUser.TenantId;
        var number = request.BedNumber.Trim().ToUpperInvariant();
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var room = await _rooms.GetQueryable().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenant && !x.IsDeleted && x.Id == request.HostelRoomId, token);
                if (room == null) return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Room not found.", 404);
                var hostel = await _hostels.GetQueryable().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenant && !x.IsDeleted && x.Id == room.HostelId, token);
                if (hostel == null) return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Hostel not found.", 404);
                if (!await CanManageCampusAsync(hostel.CampusId, tenant, token))
                    return Denied<HostelBedInventoryDto>();
                if (!await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == hostel.CampusId && x.IsActive, token) ||
                    !hostel.IsActive || !room.IsActive)
                    return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Active campus, hostel and room are required.", 409);

                var existing = await _beds.GetQueryable().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenant && !x.IsDeleted &&
                        x.HostelRoomId == room.Id && x.BedNumber.ToUpper() == number, token);
                if (existing != null)
                {
                    var occupied = await _allocations.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && !x.IsDeleted && x.HostelBedId == existing.Id &&
                        x.State == HostelAllocationState.Active, token);
                    return ApiResponse<HostelBedInventoryDto>.SuccessResponse(new HostelBedInventoryDto
                    {
                        Id = existing.Id, HostelRoomId = room.Id, HostelName = hostel.Name,
                        RoomNumber = room.RoomNumber, BedNumber = existing.BedNumber,
                        IsActive = existing.IsActive, IsOccupied = occupied,
                        RowVersion = Convert.ToBase64String(existing.RowVersion)
                    }, "Bed already exists; no duplicate created.");
                }
                if (!room.RowVersion.AsSpan().SequenceEqual(version))
                    return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Room changed. Reload and retry.", 409);
                if (room.Capacity is < 1 or > 1000)
                    return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Room capacity is invalid.", 409);
                var physicalBeds = await _beds.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.HostelRoomId == room.Id, token);
                if (physicalBeds >= room.Capacity)
                    return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Room is at physical bed capacity.", 409);
                var bed = new HostelBed
                {
                    TenantId = tenant, HostelRoomId = room.Id, BedNumber = number, IsActive = true,
                    CreatedAt = _clock.GetUtcNow().UtcDateTime, CreatedBy = _currentUser.UserId
                };
                await _beds.AddAsync(bed);
                await _unitOfWork.SaveChangesAsync(token);
                return ApiResponse<HostelBedInventoryDto>.SuccessResponse(new HostelBedInventoryDto
                {
                    Id = bed.Id, HostelRoomId = room.Id, HostelName = hostel.Name,
                    RoomNumber = room.RoomNumber, BedNumber = bed.BedNumber,
                    IsActive = true, IsOccupied = false,
                    RowVersion = Convert.ToBase64String(bed.RowVersion)
                }, "Hostel bed created.");
            }, System.Data.IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Room or bed changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel bed creation conflict for tenant {TenantId}", tenant);
            return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Bed creation conflicts with another change.", 409);
        }
        catch (Exception ex) when (ex is System.Transactions.TransactionAbortedException || HasHostelSqlSerializationConflict(ex))
        {
            _logger.LogWarning(ex, "Hostel bed creation SQL concurrency conflict for tenant {TenantId}", tenant);
            return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Concurrent bed provisioning change. Reload and retry.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hostel bed creation failed for tenant {TenantId}", tenant);
            return ApiResponse<HostelBedInventoryDto>.ErrorResponse("Bed creation failed.", 500);
        }
    }
}
