using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    public static bool CanShortenUpcomingNoChargeStay(
        DateOnly start, DateOnly? currentEnd, DateOnly requestedEnd, DateOnly today) =>
        start > today && currentEnd.HasValue && currentEnd.Value >= start &&
        requestedEnd >= start && requestedEnd < currentEnd.Value;

    /// <summary>Only future, zero-rent, zero-deposit, fee-head-free stays can release reserved days without Finance.</summary>
    public async Task<ApiResponse<StudentHostelAllocationDto>> ShortenUpcomingNoChargeStayAsync(
        long id, ShortenUpcomingNoChargeHostelStayRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<StudentHostelAllocationDto>();
        if (id <= 0 || request == null || request.EndDate == default ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return Error("Reservation, checkout date and row version are required.");
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
                    select new { Hostel = hostel }).FirstOrDefaultAsync(ct);
                var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == allocation.StudentEnrollmentId &&
                    x.StudentId == allocation.StudentId && x.IsCurrent &&
                    x.State == EnrollmentState.Active, ct);
                if (source == null || enrollment == null ||
                    enrollment.CampusId != source.Hostel.CampusId ||
                    !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && !x.IsDeleted &&
                        x.Id == source.Hostel.CampusId && x.IsActive, ct))
                    return Error("Active enrollment and matching hostel campus are required.", 409);
                if (!await CanManageCampusAsync(source.Hostel.CampusId, tenant, ct))
                    return Denied<StudentHostelAllocationDto>();
                if (allocation.State != HostelAllocationState.Active || !allocation.EndDate.HasValue)
                    return Error("Only active fixed-term reservations can be shortened.", 409);
                if (allocation.MonthlyRent != 0m || allocation.SecurityDeposit != 0m ||
                    allocation.FeeHeadId.HasValue)
                    return Error("Charged reservations require Finance billing integration.", 409);
                var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                if (allocation.StartDate <= today || allocation.EndDate.Value < allocation.StartDate)
                    return Error("Only valid, unstarted reservations can be shortened.", 409);
                if (request.EndDate == allocation.EndDate.Value)
                    return ApiResponse<StudentHostelAllocationDto>.SuccessResponse(
                        await MapAsync(allocation, ct), "Checkout date already matches.");
                if (!CanShortenUpcomingNoChargeStay(
                    allocation.StartDate, allocation.EndDate, request.EndDate, today))
                    return Error("New checkout must be on or after arrival and before the current checkout.", 409);
                if (!allocation.RowVersion.AsSpan().SequenceEqual(version))
                    return Error("Reservation changed. Reload and retry.", 409);
                var previous = allocation.EndDate.Value;
                allocation.EndDate = request.EndDate;
                allocation.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                allocation.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Upcoming no-charge hostel checkout shortened for tenant {TenantId}, allocation {AllocationId}, {OldEnd} to {NewEnd}",
                    tenant, allocation.Id, previous, request.EndDate);
                return ApiResponse<StudentHostelAllocationDto>.SuccessResponse(
                    await MapAsync(allocation, ct), "Upcoming no-charge checkout shortened.");
            }, System.Data.IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return Error("Reservation changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel checkout shortening conflict for tenant {TenantId}", tenant);
            return Error("Reservation conflicts with another change.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Hostel checkout shortening transaction aborted for tenant {TenantId}", tenant);
            return Error("Concurrent reservation change. Reload and retry.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hostel checkout shortening failed for tenant {TenantId}", tenant);
            return Error("Checkout date could not be shortened.", 500);
        }
    }
}
