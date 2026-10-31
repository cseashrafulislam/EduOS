using EduOS.Core.Common;
using EduOS.Core.DTOs.Hostel;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Hostel;

public sealed partial class HostelService
{
    public static bool CanExtendNoChargeStay(DateOnly start, DateOnly? currentEnd, DateOnly requestedEnd, DateOnly today) =>
        currentEnd.HasValue && currentEnd.Value >= today && start <= currentEnd.Value &&
        requestedEnd > currentEnd.Value && requestedEnd <= today.AddDays(365) &&
        requestedEnd.DayNumber - start.DayNumber <= 89;

    /// <summary>Extend within the fixed-term booking limit; charged stays consume independently approved Finance posting.</summary>
    public async Task<ApiResponse<StudentHostelAllocationDto>> ExtendNoChargeStayAsync(
        long id, ExtendNoChargeHostelStayRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<StudentHostelAllocationDto>();
        if (id <= 0 || request == null || request.EndDate == default ||
            request.FinanceClearanceId is <= 0 ||
            !TryDecodeVersion(request.RowVersion, out var version))
            return Error("Reservation, checkout date and row version are required.");
        var tenant = _currentUser.TenantId;
        const string operation = "hostel.no-charge-stay-extension";
        var requestHash = HostelCommandFingerprint(operation, id,
            request.EndDate.ToString("yyyy-MM-dd"), Convert.ToBase64String(version),
            request.FinanceClearanceId?.ToString() ?? "none");
        var requestId = request.ClientRequestId == Guid.Empty
            ? DeriveHostelCommandRequestId(tenant, _currentUser.UserId, operation, id, requestHash)
            : request.ClientRequestId;
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
                var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id == allocation.StudentEnrollmentId &&
                    x.StudentId == allocation.StudentId && x.IsCurrent && x.State == EnrollmentState.Active, ct);
                if (source == null || enrollment == null || enrollment.CampusId != source.Hostel.CampusId ||
                    !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && !x.IsDeleted && x.Id == source.Hostel.CampusId && x.IsActive, ct))
                    return Error("Active enrollment and matching hostel campus are required.", 409);
                if (!await CanManageCampusAsync(source.Hostel.CampusId, tenant, ct))
                    return Denied<StudentHostelAllocationDto>();
                var replay = await _commandReceipts.ReplayAsync<StudentHostelAllocationDto>(tenant,
                    _currentUser.UserId, operation, requestId, id, requestHash, ct);
                if (replay.Conflict)
                    return Error("Request key was already used with different command details.", 409);
                if (replay.Found) return replay.Response!;
                if (allocation.State != HostelAllocationState.Active || !allocation.EndDate.HasValue)
                    return Error("Only active fixed-term stays can be extended.", 409);
                var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                if (allocation.EndDate.Value == request.EndDate && allocation.EndDate.Value >= today)
                {
                    var same = ApiResponse<StudentHostelAllocationDto>.SuccessResponse(
                        await MapAsync(allocation, ct), "Checkout date already matches.");
                    await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                        requestId, id, requestHash, same, ct);
                    await _unitOfWork.SaveChangesAsync(ct);
                    return same;
                }
                if (!CanExtendNoChargeStay(allocation.StartDate, allocation.EndDate, request.EndDate, today))
                    return Error("Extend before checkout within the original 90-day booking window.", 409);
                if (!allocation.RowVersion.AsSpan().SequenceEqual(version))
                    return Error("Reservation changed. Reload and retry.", 409);
                var financeExposure = await _reservationFinancePolicy.HasPostedActivityAsync(tenant,
                    allocation.StudentId, allocation.StudentEnrollmentId, ct);
                if ((financeExposure || allocation.MonthlyRent != 0m ||
                     allocation.SecurityDeposit != 0m || allocation.FeeHeadId.HasValue) &&
                    !request.FinanceClearanceId.HasValue)
                    return Error("Charged or invoiced stay requires Finance-approved period settlement.", 409);
                if (!source.Bed.IsActive || !source.Room.IsActive || !source.Hostel.IsActive ||
                    source.Room.Capacity < 1)
                    return Error("Hostel accommodation is unavailable.", 409);
                var extensionStart = allocation.EndDate.Value.AddDays(1);
                if (await _allocations.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id != allocation.Id &&
                    x.State != HostelAllocationState.Cancelled && x.StudentId == allocation.StudentId &&
                    x.StartDate <= request.EndDate &&
                    (!x.EndDate.HasValue || x.EndDate.Value >= extensionStart), ct))
                    return Error("Student has an overlapping stay during the extension.", 409);
                if (await _allocations.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && !x.IsDeleted && x.Id != allocation.Id &&
                    x.State != HostelAllocationState.Cancelled && x.HostelBedId == allocation.HostelBedId &&
                    x.StartDate <= request.EndDate &&
                    (!x.EndDate.HasValue || x.EndDate.Value >= extensionStart), ct))
                    return Error("Bed is reserved during the extension.", 409);
                var others = await (from stay in _allocations.GetQueryable().AsNoTracking()
                    join bed in _beds.GetQueryable().AsNoTracking() on stay.HostelBedId equals bed.Id
                    where stay.TenantId == tenant && bed.TenantId == tenant &&
                        !stay.IsDeleted && stay.Id != allocation.Id &&
                        stay.State != HostelAllocationState.Cancelled &&
                        bed.HostelRoomId == source.Room.Id && stay.StartDate <= request.EndDate &&
                        (!stay.EndDate.HasValue || stay.EndDate.Value >= extensionStart)
                    select new { stay.StartDate, stay.EndDate }).Take(5001).ToListAsync(ct);
                if (others.Count > 5000) return Error("Too many reservations to validate safely.", 413);
                if (PeakConcurrentStays(extensionStart, request.EndDate,
                    others.Select(x => (x.StartDate, x.EndDate))) > source.Room.Capacity)
                    return Error("Room capacity would be exceeded.", 409);
                if (request.FinanceClearanceId.HasValue)
                {
                    var approval = await ConsumeHostelFinanceClearanceAsync(request.FinanceClearanceId.Value,
                        allocation, source.Hostel.CampusId, HostelClearanceOperation.UpcomingEndChange,
                        request.EndDate, null, null, ct);
                    if (approval.Error != null) return Error(approval.Error, 409);
                }
                var previous = allocation.EndDate.Value;
                allocation.EndDate = request.EndDate;
                allocation.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
                allocation.UpdatedBy = _currentUser.UserId;
                await _unitOfWork.SaveChangesAsync(ct);
                _logger.LogInformation("Hostel stay extension tenant {TenantId}, allocation {AllocationId}, {OldEnd} to {NewEnd}",
                    tenant, allocation.Id, previous, request.EndDate);
                var success = ApiResponse<StudentHostelAllocationDto>.SuccessResponse(
                    await MapAsync(allocation, ct), "Hostel stay extended.");
                await _commandReceipts.RecordAsync(tenant, _currentUser.UserId, operation,
                    requestId, id, requestHash, success, ct);
                await _unitOfWork.SaveChangesAsync(ct);
                return success;
            }, System.Data.IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { return Error("Reservation changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hostel extension conflict tenant {TenantId}", tenant);
            return Error("Reservation conflicts with another change.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Hostel extension transaction aborted tenant {TenantId}", tenant);
            return Error("Concurrent reservation change. Reload and retry.", 409);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Hostel extension failed tenant {TenantId}", tenant);
            return Error("Stay could not be extended.", 500);
        }
    }
}
