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
        if (allocation.EndDate.HasValue && allocation.EndDate.Value < allocation.StartDate)
            return ApiResponse<PagedResult<HostelBedOptionDto>>.ErrorResponse("Reservation dates are invalid.", 409);
        var requestedEnd = allocation.EndDate ?? DateOnly.MaxValue;
        var query = from bed in _beds.GetQueryable().AsNoTracking()
            join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
            where bed.TenantId == tenant && room.TenantId == tenant &&
                !bed.IsDeleted && !room.IsDeleted && bed.IsActive && room.IsActive &&
                room.HostelId == source.Hostel.Id && room.Id != source.Room.Id &&
                room.RentPerBed == source.Room.RentPerBed && room.Capacity > 0
            select new { BedId = bed.Id, RoomId = room.Id, room.Capacity, bed.BedNumber, room.RoomNumber };
        var candidateCount = await query.CountAsync(cancellationToken);
        if (candidateCount > 1000)
            return ApiResponse<PagedResult<HostelBedOptionDto>>.ErrorResponse(
                "Too many destination beds. Narrow the inventory before reassignment.", 413);
        var candidates = await query.OrderBy(x => x.RoomNumber).ThenBy(x => x.BedNumber)
            .ThenBy(x => x.BedId).ToListAsync(cancellationToken);
        var roomIds = candidates.Select(x => x.RoomId).Distinct().ToArray();
        // Include reservations on inactive/deleted beds: they still consume physical room capacity.
        var reservations = await (from reservation in _allocations.GetQueryable().AsNoTracking()
            join reservedBed in _beds.GetQueryable().AsNoTracking() on reservation.HostelBedId equals reservedBed.Id
            where reservation.TenantId == tenant && reservedBed.TenantId == tenant &&
                !reservation.IsDeleted && reservation.State != HostelAllocationState.Cancelled &&
                reservation.Id != allocation.Id && roomIds.Contains(reservedBed.HostelRoomId) &&
                reservation.StartDate <= requestedEnd &&
                (!reservation.EndDate.HasValue || reservation.EndDate.Value >= allocation.StartDate)
            select new { RoomId = reservedBed.HostelRoomId, BedId = reservedBed.Id,
                reservation.StartDate, reservation.EndDate }).Take(10001).ToListAsync(cancellationToken);
        if (reservations.Count > 10000)
            return ApiResponse<PagedResult<HostelBedOptionDto>>.ErrorResponse(
                "Too many overlapping reservations. Narrow the inventory.", 413);
        var byRoom = reservations.GroupBy(x => x.RoomId).ToDictionary(x => x.Key, x => x.ToList());
        var available = candidates.Where(candidate =>
        {
            var bookings = byRoom.GetValueOrDefault(candidate.RoomId);
            if (bookings == null) return true;
            if (bookings.Any(x => x.BedId == candidate.BedId)) return false;
            return PeakOccupiedBedsForReservationInterval(
                bookings.Select(x => (x.BedId, x.StartDate, x.EndDate)).ToArray(),
                allocation.StartDate, allocation.EndDate) < candidate.Capacity;
        }).ToList();
        var total = available.Count;
        var beds = available.Skip((page - 1) * pageSize).Take(pageSize).ToList();
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
                if (await _reservationFinancePolicy.HasPostedActivityAsync(tenant,
                    allocation.StudentId, allocation.StudentEnrollmentId, ct))
                    return Error("Posted finance activity requires an approved Finance adjustment before room change.", 409);
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
                if (allocation.EndDate.HasValue && allocation.EndDate.Value < allocation.StartDate)
                    return Error("Reservation dates are invalid.", 409);
                var requestedEnd = allocation.EndDate ?? DateOnly.MaxValue;
                var reservations = await (from reservation in _allocations.GetQueryable().AsNoTracking()
                    join reservedBed in _beds.GetQueryable().AsNoTracking() on reservation.HostelBedId equals reservedBed.Id
                    where reservation.TenantId == tenant && reservedBed.TenantId == tenant &&
                        !reservation.IsDeleted && reservation.State != HostelAllocationState.Cancelled &&
                        reservation.Id != allocation.Id && reservedBed.HostelRoomId == target.Room.Id &&
                        reservation.StartDate <= requestedEnd &&
                        (!reservation.EndDate.HasValue || reservation.EndDate.Value >= allocation.StartDate)
                    select new { BedId = reservedBed.Id, reservation.StartDate, reservation.EndDate })
                    .Take(10001).ToListAsync(ct);
                if (reservations.Count > 10000)
                    return Error("Too many overlapping reservations in the destination room.", 413);
                if (reservations.Any(x => x.BedId == target.Bed.Id))
                    return Error("Destination bed is reserved during the requested stay.", 409);
                var peak = PeakOccupiedBedsForReservationInterval(
                    reservations.Select(x => (x.BedId, x.StartDate, x.EndDate)).ToArray(),
                    allocation.StartDate, allocation.EndDate);
                if (target.Room.Capacity <= 0 || peak >= target.Room.Capacity)
                    return Error("Destination room is at capacity during the requested stay.", 409);
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

    /// <summary>
    /// Peak number of distinct physical beds occupied within an inclusive reservation interval.
    /// Merge overlapping bookings per bed before sweeping change points. This also supports
    /// open-ended reservations without iterating every day through DateOnly.MaxValue.
    /// </summary>
    public static int PeakOccupiedBedsForReservationInterval(
        IReadOnlyList<(long BedId, DateOnly StartDate, DateOnly? EndDate)> bookings,
        DateOnly fromDate, DateOnly? toDate)
    {
        if (toDate.HasValue && toDate.Value < fromDate) return 0;
        var endLimit = toDate?.DayNumber ?? DateOnly.MaxValue.DayNumber;
        var changes = new SortedDictionary<int, int>();
        static void AddChange(SortedDictionary<int, int> changes, int day, int delta)
        {
            changes[day] = changes.GetValueOrDefault(day) + delta;
        }
        foreach (var bed in bookings.GroupBy(x => x.BedId))
        {
            var spans = bed.Where(x => !x.EndDate.HasValue || x.EndDate.Value >= x.StartDate)
                .Select(x => (Start: Math.Max(x.StartDate.DayNumber, fromDate.DayNumber),
                    End: Math.Min(x.EndDate?.DayNumber ?? DateOnly.MaxValue.DayNumber, endLimit)))
                .Where(x => x.Start <= x.End).OrderBy(x => x.Start).ThenBy(x => x.End).ToArray();
            if (spans.Length == 0) continue;
            var start = spans[0].Start;
            var end = spans[0].End;
            for (var i = 1; i < spans.Length; i++)
            {
                if ((long)spans[i].Start <= (long)end + 1)
                {
                    end = Math.Max(end, spans[i].End);
                    continue;
                }
                AddChange(changes, start, 1);
                if (end < DateOnly.MaxValue.DayNumber) AddChange(changes, end + 1, -1);
                start = spans[i].Start;
                end = spans[i].End;
            }
            AddChange(changes, start, 1);
            if (end < DateOnly.MaxValue.DayNumber) AddChange(changes, end + 1, -1);
        }
        var occupied = 0;
        var peak = 0;
        foreach (var change in changes)
        {
            occupied += change.Value;
            peak = Math.Max(peak, occupied);
        }
        return peak;
    }
}
