using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    /// <summary>Available beds in the reservation's current room; never returns another campus's inventory.</summary>
    public async Task<ApiResponse<PagedResult<HostelBedOptionDto>>> GetUpcomingBedReassignmentOptionsAsync(
        long id, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<PagedResult<HostelBedOptionDto>>();
        if (id <= 0 || page < 1 || pageSize is < 1 or > 50 || (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<HostelBedOptionDto>>.ErrorResponse("Invalid reservation or paging.");
        var tenant = _currentUser.TenantId;
        var allocation = await _allocations.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && !x.IsDeleted && x.Id == id, cancellationToken);
        if (allocation == null) return ApiResponse<PagedResult<HostelBedOptionDto>>.ErrorResponse("Reservation not found.", 404);
        var source = await (from bed in _beds.GetQueryable().AsNoTracking()
            join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
            join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
            where bed.TenantId == tenant && room.TenantId == tenant && hostel.TenantId == tenant &&
                !bed.IsDeleted && !room.IsDeleted && !hostel.IsDeleted && bed.Id == allocation.HostelBedId
            select new { Room = room, Hostel = hostel }).FirstOrDefaultAsync(cancellationToken);
        var enrollmentCampus = await _enrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Id == allocation.StudentEnrollmentId && x.StudentId == allocation.StudentId)
            .Select(x => (long?)x.CampusId).FirstOrDefaultAsync(cancellationToken);
        if (source == null || !enrollmentCampus.HasValue || enrollmentCampus.Value != source.Hostel.CampusId ||
            !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id == source.Hostel.CampusId && x.IsActive && !x.IsDeleted, cancellationToken))
            return ApiResponse<PagedResult<HostelBedOptionDto>>.ErrorResponse("Reservation campus ownership is invalid.", 409);
        if (!await CanManageCampusAsync(enrollmentCampus.Value, tenant, cancellationToken))
            return Denied<PagedResult<HostelBedOptionDto>>();
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        if (allocation.State != HostelAllocationState.Active || allocation.StartDate <= today ||
            !source.Room.IsActive || !source.Hostel.IsActive)
            return ApiResponse<PagedResult<HostelBedOptionDto>>.ErrorResponse("Only upcoming reservations in active rooms can change beds.", 409);
        var query = _beds.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && x.IsActive && x.HostelRoomId == source.Room.Id &&
            x.Id != allocation.HostelBedId &&
            !_allocations.GetQueryable().Any(a => a.TenantId == tenant && !a.IsDeleted &&
                a.HostelBedId == x.Id && !a.EndDate.HasValue));
        var total = await query.CountAsync(cancellationToken);
        var beds = await query.OrderBy(x => x.BedNumber).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.BedNumber }).ToListAsync(cancellationToken);
        return ApiResponse<PagedResult<HostelBedOptionDto>>.SuccessResponse(new PagedResult<HostelBedOptionDto>
        {
            Page = page, PageSize = pageSize, TotalCount = total,
            Items = beds.Select(x => new HostelBedOptionDto
            {
                BedId = x.Id, BedNumber = x.BedNumber, RoomNumber = source.Room.RoomNumber,
                HostelName = source.Hostel.Name, RentPerBed = source.Room.RentPerBed,
                GenderRestriction = source.Hostel.GenderRestriction
            }).ToList()
        });
    }

    /// <summary>Same-room, pre-arrival correction; retains the canonical allocation and all financial terms.</summary>
    public async Task<ApiResponse<StudentHostelAllocationDto>> ReassignUpcomingBedAsync(
        long id, ReassignUpcomingHostelBedRequestDto request, CancellationToken cancellationToken = default)
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
                    .Where(x => x.TenantId == tenant && x.Id == allocation.StudentEnrollmentId && x.StudentId == allocation.StudentId)
                    .Select(x => (long?)x.CampusId).FirstOrDefaultAsync(ct);
                if (source == null || !enrollmentCampus.HasValue || enrollmentCampus.Value != source.Hostel.CampusId ||
                    !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.Id == source.Hostel.CampusId && x.IsActive && !x.IsDeleted, ct))
                    return Error("Reservation campus ownership is invalid.", 409);
                if (!await CanManageCampusAsync(enrollmentCampus.Value, tenant, ct))
                    return Denied<StudentHostelAllocationDto>();
                var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                if (allocation.State != HostelAllocationState.Active || allocation.StartDate <= today)
                    return Error("Only upcoming active reservations can change beds.", 409);
                // A successful retry must not update audit or concurrency values.
                if (allocation.HostelBedId == request.TargetBedId)
                    return ApiResponse<StudentHostelAllocationDto>.SuccessResponse(
                        await MapAsync(allocation, ct), "Reservation already uses the selected bed.");
                if (!allocation.RowVersion.AsSpan().SequenceEqual(version))
                    return Error("Reservation changed. Reload and retry.", 409);
                if (!source.Room.IsActive || !source.Hostel.IsActive || !source.Bed.IsActive)
                    return Error("Current hostel room or bed is inactive.", 409);
                var target = await _beds.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.IsActive && x.Id == request.TargetBedId &&
                    x.HostelRoomId == source.Room.Id, ct);
                if (target == null) return Error("Select an active available bed in the same room.", 409);
                if (await _allocations.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.HostelBedId == target.Id &&
                    !x.EndDate.HasValue, ct))
                    return Error("Selected bed is already reserved.", 409);
                var roomBedIds = _beds.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.HostelRoomId == source.Room.Id).Select(x => x.Id);
                var reservations = await _allocations.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && !x.EndDate.HasValue &&
                    roomBedIds.Contains(x.HostelBedId), ct);
                if (source.Room.Capacity <= 0 || reservations > source.Room.Capacity)
                    return Error("Room reservation capacity is invalid.", 409);
                var oldBedId = allocation.HostelBedId;
                allocation.HostelBedId = target.Id;
                allocation.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                allocation.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(ct);
                _logger.LogInformation("Hostel pre-arrival bed correction: tenant {TenantId}, allocation {AllocationId}, from bed {OldBedId} to {NewBedId}",
                    tenant, allocation.Id, oldBedId, target.Id);
                return ApiResponse<StudentHostelAllocationDto>.SuccessResponse(
                    await MapAsync(allocation, ct), "Upcoming reservation bed updated.");
            }, System.Data.IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return Error("Reservation changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel bed reassignment conflict for tenant {TenantId}", tenant);
            return Error("Destination bed conflicts with another reservation.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hostel bed reassignment failed for tenant {TenantId}", tenant);
            return Error("Reservation bed could not be changed.", 500);
        }
    }
}
