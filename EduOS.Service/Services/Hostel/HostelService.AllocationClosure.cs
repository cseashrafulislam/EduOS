using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    /// <summary>
    /// Close an occupied stay without altering Finance-owned rent/deposit records.
    /// Historical and same-day checkout are supported; future and pre-arrival closure
    /// must not make a still-occupied bed appear vacant.
    /// </summary>
    public async Task<ApiResponse<StudentHostelAllocationDto>> CloseAsync(
        long id, CloseStudentHostelAllocationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<StudentHostelAllocationDto>();
        if (id <= 0 || request == null || !TryDecodeVersion(request.RowVersion, out var version))
            return Error("Allocation and row version are required.");

        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var allocation = await _allocations.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == id, ct);
                if (allocation == null) return Error("Hostel allocation not found.", 404);

                // Verify both sides of the historical enrollment-to-bed campus boundary,
                // including for tenant administrators; no cross-campus closure is accepted.
                var enrollmentCampus = await (from enrollment in _enrollments.GetQueryable().AsNoTracking()
                    join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
                    where enrollment.TenantId == tenant && student.TenantId == tenant &&
                        !enrollment.IsDeleted && !student.IsDeleted &&
                        enrollment.Id == allocation.StudentEnrollmentId &&
                        enrollment.StudentId == allocation.StudentId
                    select (long?)enrollment.CampusId).FirstOrDefaultAsync(ct);
                var bedCampus = await (from bed in _beds.GetQueryable().AsNoTracking()
                    join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
                    join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
                    join campus in _campuses.GetQueryable().AsNoTracking() on hostel.CampusId equals campus.Id
                    where bed.TenantId == tenant && room.TenantId == tenant &&
                        hostel.TenantId == tenant && campus.TenantId == tenant &&
                        !bed.IsDeleted && !room.IsDeleted && !hostel.IsDeleted &&
                        !campus.IsDeleted && bed.Id == allocation.HostelBedId
                    select (long?)hostel.CampusId).FirstOrDefaultAsync(ct);
                if (!enrollmentCampus.HasValue || !bedCampus.HasValue ||
                    enrollmentCampus.Value != bedCampus.Value)
                    return Error("Allocation campus ownership is invalid.", 409);
                if (!await CanManageCampusAsync(enrollmentCampus.Value, tenant, ct))
                    return Denied<StudentHostelAllocationDto>();

                var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                var end = request.EndDate == default ? today : request.EndDate;
                // Exact retries preserve the original row version and audit fields.
                if (allocation.State == HostelAllocationState.Closed)
                {
                    if (allocation.EndDate.HasValue &&
                        (request.EndDate == default || allocation.EndDate.Value == end))
                        return ApiResponse<StudentHostelAllocationDto>.SuccessResponse(
                            await MapAsync(allocation, ct), "Allocation already closed.");
                    return Error("Allocation was already closed with a different end date.", 409);
                }
                if (allocation.State != HostelAllocationState.Active)
                    return Error("Allocation cannot be closed in its current state.", 409);
                if (!IsValidClosureDate(allocation.StartDate, end, today))
                    return Error("Checkout must be between the stay start and today. Cancel future reservations.", 409);
                if (!allocation.RowVersion.AsSpan().SequenceEqual(version))
                    return Error("Hostel allocation changed. Reload and retry.", 409);
                if (await _reservationFinancePolicy.HasPostedActivityAsync(tenant,
                    allocation.StudentId, allocation.StudentEnrollmentId, ct))
                    return Error("Posted finance activity requires an approved Finance adjustment before checkout.", 409);

                allocation.EndDate = end;
                allocation.State = HostelAllocationState.Closed;
                allocation.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                allocation.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(ct);
                _logger.LogInformation("Hostel checkout: tenant {TenantId}, allocation {AllocationId}, end {EndDate}",
                    tenant, allocation.Id, end);
                return ApiResponse<StudentHostelAllocationDto>.SuccessResponse(
                    await MapAsync(allocation, ct), "Allocation closed.");
            }, System.Data.IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error("Allocation was changed by another user.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel checkout conflict for tenant {TenantId}", tenant);
            return Error("Concurrent allocation closure conflict.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Hostel checkout transaction aborted for tenant {TenantId}", tenant);
            return Error("Concurrent allocation closure conflict.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hostel close failed for tenant {TenantId}", tenant);
            return Error("Hostel allocation could not be closed.", 500);
        }
    }

    public static bool IsValidClosureDate(DateOnly start, DateOnly end, DateOnly today) =>
        start <= today && end >= start && end <= today;
}
