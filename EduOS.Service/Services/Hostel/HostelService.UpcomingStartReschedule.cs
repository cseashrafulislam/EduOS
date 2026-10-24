using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    public static bool CanRescheduleUpcomingStay(DateOnly currentStart, DateOnly? endDate,
        DateOnly requestedStart, DateOnly today) =>
        currentStart > today && requestedStart > today && requestedStart <= today.AddYears(2) &&
        (!endDate.HasValue || requestedStart <= endDate.Value);

    /// <summary>Peak concurrent room reservations, including the proposed reservation, with inclusive dates.</summary>
    public static int PeakConcurrentStays(DateOnly start, DateOnly? end,
        IEnumerable<(DateOnly Start, DateOnly? End)> existing)
    {
        var last = end ?? DateOnly.MaxValue;
        var events = new List<(DateOnly Day, int Delta)> { (start, 1) };
        if (last < DateOnly.MaxValue) events.Add((last.AddDays(1), -1));
        foreach (var (otherStart, otherEnd) in existing)
        {
            var first = otherStart > start ? otherStart : start;
            var otherLast = otherEnd ?? DateOnly.MaxValue;
            var final = otherLast < last ? otherLast : last;
            if (first > final) continue;
            events.Add((first, 1));
            if (final < DateOnly.MaxValue) events.Add((final.AddDays(1), -1));
        }
        var occupied = 0;
        var peak = 0;
        foreach (var item in events.OrderBy(x => x.Day).ThenBy(x => x.Delta))
        {
            occupied += item.Delta;
            peak = Math.Max(peak, occupied);
        }
        return peak;
    }

    /// <summary>Reschedule a future reservation on the canonical allocation; preserves bed and Finance terms.</summary>
    public async Task<ApiResponse<StudentHostelAllocationDto>> RescheduleUpcomingAllocationStartAsync(
        long id, RescheduleUpcomingHostelStartRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<StudentHostelAllocationDto>();
        if (id <= 0 || request == null || request.StartDate == default ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return Error("Reservation, start date and row version are required.");
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
                        !bed.IsDeleted && !room.IsDeleted && !hostel.IsDeleted &&
                        bed.Id == allocation.HostelBedId
                    select new { Bed = bed, Room = room, Hostel = hostel }).FirstOrDefaultAsync(ct);
                var enrollment = await _enrollments.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted && x.Id == allocation.StudentEnrollmentId &&
                        x.StudentId == allocation.StudentId && x.IsCurrent && x.State == EnrollmentState.Active)
                    .Select(x => new { x.CampusId }).FirstOrDefaultAsync(ct);
                var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == allocation.StudentId &&
                    x.StatusCode == "Active", ct);
                if (source == null || enrollment == null || student == null ||
                    enrollment.CampusId != source.Hostel.CampusId ||
                    !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && !x.IsDeleted && x.Id == source.Hostel.CampusId &&
                        x.IsActive, ct))
                    return Error("Active enrollment and hostel campus ownership are required.", 409);
                if (!await CanManageCampusAsync(source.Hostel.CampusId, tenant, ct))
                    return Denied<StudentHostelAllocationDto>();
                var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                if (allocation.State != HostelAllocationState.Active ||
                    !CanRescheduleUpcomingStay(allocation.StartDate, allocation.EndDate, request.StartDate, today))
                    return Error("Only future reservations may be rescheduled within their existing end date.", 409);
                if (allocation.StartDate == request.StartDate)
                    return ApiResponse<StudentHostelAllocationDto>.SuccessResponse(
                        await MapAsync(allocation, ct), "Reservation already starts on the selected date.");
                if (!allocation.RowVersion.AsSpan().SequenceEqual(version))
                    return Error("Reservation changed. Reload and retry.", 409);
                if (!source.Bed.IsActive || !source.Room.IsActive || !source.Hostel.IsActive ||
                    source.Room.Capacity < 1)
                    return Error("Hostel bed or room is unavailable.", 409);
                if (!string.IsNullOrWhiteSpace(source.Hostel.GenderRestriction) &&
                    !string.Equals(source.Hostel.GenderRestriction, student.Gender, StringComparison.OrdinalIgnoreCase))
                    return Error("Hostel eligibility restriction is not satisfied.", 409);
                var end = allocation.EndDate ?? DateOnly.MaxValue;
                if (await _allocations.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id != allocation.Id &&
                    x.State != HostelAllocationState.Cancelled && x.StudentId == allocation.StudentId &&
                    x.StartDate <= end && (!x.EndDate.HasValue || x.EndDate.Value >= request.StartDate), ct))
                    return Error("Student already has an overlapping hostel stay.", 409);
                if (await _allocations.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id != allocation.Id &&
                    x.State != HostelAllocationState.Cancelled && x.HostelBedId == allocation.HostelBedId &&
                    x.StartDate <= end && (!x.EndDate.HasValue || x.EndDate.Value >= request.StartDate), ct))
                    return Error("Bed is reserved during the proposed stay.", 409);
                var roomStays = await (from stay in _allocations.GetQueryable().AsNoTracking()
                    join bed in _beds.GetQueryable().AsNoTracking() on stay.HostelBedId equals bed.Id
                    where stay.TenantId == tenant && bed.TenantId == tenant &&
                        !stay.IsDeleted && !bed.IsDeleted && stay.Id != allocation.Id &&
                        stay.State != HostelAllocationState.Cancelled &&
                        bed.HostelRoomId == source.Room.Id &&
                        stay.StartDate <= end && (!stay.EndDate.HasValue || stay.EndDate.Value >= request.StartDate)
                    select new { stay.StartDate, stay.EndDate }).Take(5001).ToListAsync(ct);
                if (roomStays.Count > 5000) return Error("Too many overlapping reservations to validate safely.", 409);
                if (PeakConcurrentStays(request.StartDate, allocation.EndDate,
                    roomStays.Select(x => (x.StartDate, x.EndDate))) > source.Room.Capacity)
                    return Error("Room capacity would be exceeded during the requested stay.", 409);
                var previous = allocation.StartDate;
                allocation.StartDate = request.StartDate;
                allocation.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                allocation.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(ct);
                _logger.LogInformation("Hostel pre-arrival reschedule: tenant {TenantId}, allocation {AllocationId}, from {OldDate} to {NewDate}",
                    tenant, allocation.Id, previous, request.StartDate);
                return ApiResponse<StudentHostelAllocationDto>.SuccessResponse(
                    await MapAsync(allocation, ct), "Upcoming hostel reservation rescheduled.");
            }, System.Data.IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return Error("Reservation changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel reschedule conflict for tenant {TenantId}", tenant);
            return Error("Hostel reservation conflicts with another change.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hostel reschedule failed for tenant {TenantId}", tenant);
            return Error("Hostel reservation could not be rescheduled.", 500);
        }
    }
}
