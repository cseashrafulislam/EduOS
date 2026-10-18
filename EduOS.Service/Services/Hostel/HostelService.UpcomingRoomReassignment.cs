using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    public async Task<ApiResponse<PagedResult<HostelBedOptionDto>>> GetUpcomingRoomTransferOptionsAsync(
        long id, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<PagedResult<HostelBedOptionDto>>();
        if (id <= 0 || page < 1 || pageSize is < 1 or > 50 || (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<HostelBedOptionDto>>.ErrorResponse("Invalid reservation or paging.");
        var tenant = _currentUser.TenantId;
        var allocation = await _allocations.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.Id == id, cancellationToken);
        if (allocation == null)
            return ApiResponse<PagedResult<HostelBedOptionDto>>.ErrorResponse("Reservation not found.", 404);
        var source = await (from bed in _beds.GetQueryable().AsNoTracking()
            join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
            join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
            where bed.TenantId == tenant && room.TenantId == tenant && hostel.TenantId == tenant &&
                !bed.IsDeleted && !room.IsDeleted && !hostel.IsDeleted && bed.Id == allocation.HostelBedId
            select new { Bed = bed, Room = room, Hostel = hostel }).FirstOrDefaultAsync(cancellationToken);
        var enrollmentCampus = await _enrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Id == allocation.StudentEnrollmentId &&
                x.StudentId == allocation.StudentId)
            .Select(x => (long?)x.CampusId).FirstOrDefaultAsync(cancellationToken);
        if (source == null || !enrollmentCampus.HasValue || enrollmentCampus.Value != source.Hostel.CampusId ||
            !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id == source.Hostel.CampusId && x.IsActive && !x.IsDeleted, cancellationToken))
            return ApiResponse<PagedResult<HostelBedOptionDto>>.ErrorResponse("Reservation campus ownership is invalid.", 409);
        if (!await CanManageCampusAsync(enrollmentCampus.Value, tenant, cancellationToken))
            return Denied<PagedResult<HostelBedOptionDto>>();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (allocation.State != HostelAllocationState.Active || allocation.StartDate <= today ||
            !source.Bed.IsActive || !source.Room.IsActive || !source.Hostel.IsActive)
            return ApiResponse<PagedResult<HostelBedOptionDto>>.ErrorResponse("Only future reservations in active accommodation can change rooms.", 409);
        var query = from bed in _beds.GetQueryable().AsNoTracking()
            join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
            where bed.TenantId == tenant && room.TenantId == tenant &&
                !bed.IsDeleted && !room.IsDeleted && bed.IsActive && room.IsActive &&
                room.HostelId == source.Hostel.Id && room.Id != source.Room.Id &&
                room.RentPerBed == source.Room.RentPerBed && room.Capacity > 0 &&
                !_allocations.GetQueryable().Any(a => a.TenantId == tenant && !a.IsDeleted &&
                    a.HostelBedId == bed.Id && !a.EndDate.HasValue) &&
                (from reservation in _allocations.GetQueryable()
                 join reservedBed in _beds.GetQueryable() on reservation.HostelBedId equals reservedBed.Id
                 where reservation.TenantId == tenant && !reservation.IsDeleted &&
                     !reservation.EndDate.HasValue && reservedBed.TenantId == tenant &&
                     reservedBed.HostelRoomId == room.Id
                 select reservation.Id).Count() < room.Capacity
            select new { BedId = bed.Id, bed.BedNumber, room.RoomNumber };
        var total = await query.CountAsync(cancellationToken);
        var beds = await query.OrderBy(x => x.RoomNumber).ThenBy(x => x.BedNumber).ThenBy(x => x.BedId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return ApiResponse<PagedResult<HostelBedOptionDto>>.SuccessResponse(new PagedResult<HostelBedOptionDto>
        {
            Page = page, PageSize = pageSize, TotalCount = total,
            Items = beds.Select(x => new HostelBedOptionDto
            {
                BedId = x.BedId, BedNumber = x.BedNumber, RoomNumber = x.RoomNumber,
                HostelName = source.Hostel.Name, RentPerBed = source.Room.RentPerBed,
                GenderRestriction = source.Hostel.GenderRestriction
            }).ToList()
        });
    }

    public async Task<ApiResponse<StudentHostelAllocationDto>> ReassignUpcomingRoomAsync(
        long id, ReassignUpcomingHostelRoomRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<StudentHostelAllocationDto>();
        if (id <= 0 || request == null || request.TargetBedId <= 0 ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return Error("Reservation, destination bed and row version are required.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var allocation = await _allocations.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == id, ct);
                if (allocation == null) return Error("Reservation not found.", 404);
                var source = await (from bed in _beds.GetQueryable().AsNoTracking()
                    join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
                    join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
                    where bed.TenantId == tenant && room.TenantId == tenant && hostel.TenantId == tenant &&
                        !bed.IsDeleted && !room.IsDeleted && !hostel.IsDeleted && bed.Id == allocation.HostelBedId
                    select new { Bed = bed, Room = room, Hostel = hostel }).FirstOrDefaultAsync(ct);
                var enrollmentCampus = await _enrollments.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.Id == allocation.StudentEnrollmentId &&
                        x.StudentId == allocation.StudentId)
                    .Select(x => (long?)x.CampusId).FirstOrDefaultAsync(ct);
                if (source == null || !enrollmentCampus.HasValue || enrollmentCampus.Value != source.Hostel.CampusId ||
                    !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.Id == source.Hostel.CampusId && x.IsActive && !x.IsDeleted, ct))
                    return Error("Reservation campus ownership is invalid.", 409);
                if (!await CanManageCampusAsync(enrollmentCampus.Value, tenant, ct))
                    return Denied<StudentHostelAllocationDto>();
                var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                if (allocation.State != HostelAllocationState.Active || allocation.StartDate <= today)
                    return Error("Only future active reservations can change rooms.", 409);
                if (allocation.HostelBedId == request.TargetBedId)
                    return ApiResponse<StudentHostelAllocationDto>.SuccessResponse(
                        await MapAsync(allocation, ct), "Reservation already uses the selected bed.");
                if (!allocation.RowVersion.AsSpan().SequenceEqual(version))
                    return Error("Reservation changed. Reload and retry.", 409);
                if (!source.Bed.IsActive || !source.Room.IsActive || !source.Hostel.IsActive)
                    return Error("Current accommodation is inactive.", 409);
                var target = await (from bed in _beds.GetQueryable().AsNoTracking()
                    join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
                    where bed.TenantId == tenant && room.TenantId == tenant &&
                        !bed.IsDeleted && !room.IsDeleted && bed.IsActive && room.IsActive &&
                        bed.Id == request.TargetBedId && room.Id != source.Room.Id &&
                        room.HostelId == source.Hostel.Id && room.RentPerBed == source.Room.RentPerBed
                    select new { Bed = bed, Room = room }).FirstOrDefaultAsync(ct);
                if (target == null) return Error("Choose an active bed in another room of the same hostel and tariff.", 409);
                if (await _allocations.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.HostelBedId == target.Bed.Id &&
                    !x.EndDate.HasValue, ct))
                    return Error("Destination bed is already reserved.", 409);
                var targetBedIds = _beds.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.HostelRoomId == target.Room.Id).Select(x => x.Id);
                var reserved = await _allocations.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && !x.EndDate.HasValue &&
                    targetBedIds.Contains(x.HostelBedId), ct);
                if (target.Room.Capacity <= 0 || reserved >= target.Room.Capacity)
                    return Error("Destination room is at capacity.", 409);
                var previousBedId = allocation.HostelBedId;
                allocation.HostelBedId = target.Bed.Id;
                allocation.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                allocation.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(ct);
                _logger.LogInformation("Hostel pre-arrival room correction: tenant {TenantId}, allocation {AllocationId}, from bed {OldBedId} to {NewBedId}",
                    tenant, allocation.Id, previousBedId, target.Bed.Id);
                return ApiResponse<StudentHostelAllocationDto>.SuccessResponse(
                    await MapAsync(allocation, ct), "Upcoming reservation room updated.");
            }, System.Data.IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return Error("Reservation changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel room reassignment conflict for tenant {TenantId}", tenant);
            return Error("Destination room conflicts with another reservation.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hostel room reassignment failed for tenant {TenantId}", tenant);
            return Error("Reservation room could not be changed.", 500);
        }
    }
}
