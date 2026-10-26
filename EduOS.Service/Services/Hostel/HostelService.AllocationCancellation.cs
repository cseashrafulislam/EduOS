using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    /// <summary>Cancel a future reservation without creating a new allocation or touching Finance-owned values.</summary>
    public async Task<ApiResponse<StudentHostelAllocationDto>> CancelUpcomingAllocationAsync(long id,
        CancelStudentHostelAllocationRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<StudentHostelAllocationDto>();
        if (id <= 0 || request == null || !TryDecodeVersion(request.RowVersion, out var version))
            return Error("Allocation and row version are required.");
        var tenant = _currentUser.TenantId;
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var allocation = await _allocations.GetQueryable()
                    .FirstOrDefaultAsync(x => x.TenantId == tenant && x.Id == id && !x.IsDeleted, ct);
                if (allocation == null) return Error("Allocation not found.", 404);
                var enrollmentCampus = await _enrollments.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.Id == allocation.StudentEnrollmentId &&
                        x.StudentId == allocation.StudentId)
                    .Select(x => (long?)x.CampusId).FirstOrDefaultAsync(ct);
                var bedCampus = await (from bed in _beds.GetQueryable().AsNoTracking()
                    join room in _rooms.GetQueryable().AsNoTracking() on bed.HostelRoomId equals room.Id
                    join hostel in _hostels.GetQueryable().AsNoTracking() on room.HostelId equals hostel.Id
                    where bed.TenantId == tenant && room.TenantId == tenant && hostel.TenantId == tenant &&
                        bed.Id == allocation.HostelBedId
                    select (long?)hostel.CampusId).FirstOrDefaultAsync(ct);
                if (!enrollmentCampus.HasValue || !bedCampus.HasValue ||
                    enrollmentCampus.Value != bedCampus.Value)
                    return Error("Allocation campus ownership is invalid.", 409);
                if (!await CanManageCampusAsync(enrollmentCampus.Value, tenant, ct))
                    return Denied<StudentHostelAllocationDto>();
                if (allocation.State == HostelAllocationState.Cancelled)
                    return ApiResponse<StudentHostelAllocationDto>.SuccessResponse(
                        await MapAsync(allocation, ct), "Upcoming reservation already cancelled.");
                if (allocation.State != HostelAllocationState.Active)
                    return Error("Only active reservations can be cancelled.", 409);
                if (!allocation.RowVersion.AsSpan().SequenceEqual(version))
                    return Error("Allocation changed. Reload and retry.", 409);
                if (await _reservationFinancePolicy.HasPostedActivityAsync(tenant,
                    allocation.StudentId, allocation.StudentEnrollmentId, ct))
                    return Error("Posted finance activity requires an approved Finance adjustment before cancellation.", 409);
                var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                if (allocation.StartDate <= today)
                    return Error("Allocation has already started. Use the normal close workflow.", 409);
                allocation.State = HostelAllocationState.Cancelled;
                // The existing filtered uniqueness indexes require EndDate to be non-null to release the bed.
                // State=Cancelled ensures this is never interpreted as an occupied stay.
                allocation.EndDate = allocation.StartDate;
                allocation.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                allocation.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(ct);
                return ApiResponse<StudentHostelAllocationDto>.SuccessResponse(
                    await MapAsync(allocation, ct), "Upcoming reservation cancelled.");
            }, System.Data.IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error("Allocation changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel reservation cancellation conflict for tenant {TenantId}", tenant);
            return Error("Reservation cancellation conflicts with another request.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hostel reservation cancellation failed for tenant {TenantId}", tenant);
            return Error("Reservation cancellation failed.", 500);
        }
    }
}
